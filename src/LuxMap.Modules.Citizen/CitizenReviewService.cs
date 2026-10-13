using System.Net;
using LuxMap.Modules.Assets.Entities;
using LuxMap.Modules.Citizen.Entities;
using LuxMap.Modules.Faults;
using LuxMap.Modules.Faults.Entities;
using LuxMap.Persistence;
using LuxMap.Persistence.Audit;
using LuxMap.Shared.Authorization;
using LuxMap.Shared.Contracts.Enums;
using LuxMap.Shared.Contracts.Errors;
using LuxMap.Shared.Contracts.Paging;
using LuxMap.Shared.Http;
using LuxMap.Shared.Serialization;
using LuxMap.Shared.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LuxMap.Modules.Citizen;

/// <summary>
/// The Manager's review of what citizens sent (CITIZEN-QR P2c, D-5 / D-9 / D-17 / D-18): the queue, one report, its photos, and the
/// three decisions — accept as a new fault, merge into an open one, reject. A report is decided once.
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>Every decision locks the POLE row first</b>, the lock the public form takes before it files a send (P2b). Without it a send
/// in flight could join a report that was decided a moment earlier and never reach a Manager. Order: pole, then report, then (merge)
/// the fault — the public form takes pole → report, <c>PATCH /faults</c> and work orders take only faults, so no cycle.
/// </para>
/// <para>
/// One audit event per <c>SaveChanges</c> (BE-23a): accept is two saves in one transaction — the fault's <c>created</c>, then the
/// report's <c>accepted</c>; merge and reject are one save each.
/// </para>
/// </remarks>
public sealed class CitizenReviewService(
    LuxMapDbContext db,
    ICurrentActorAccessor actor,
    IAuditTrail audit,
    FaultReportService faults,
    ICitizenPhotoHinter hinter,
    CitizenHintBackoff backoff,
    IObjectStore store,
    CitizenOptions options,
    TimeProvider clock,
    ILogger<CitizenReviewService> log)
{
    public const int NoteMaxLength = 1000;

    private static readonly FaultStatus[] OpenStatuses = [.. FaultStatusSets.Open];

    private static readonly FaultType[] LampTypes = [FaultType.LampOut, FaultType.LampDim];

    /// <summary>D-17: which send kind backs which fault type. Pole damage and "other" back none.</summary>
    private static readonly Dictionary<FaultType, CitizenReportKind> Backing = new()
    {
        [FaultType.LampOut] = CitizenReportKind.LampOut,
        [FaultType.LampDim] = CitizenReportKind.LampDim,
    };

    private string ActorId => actor.UserId
        ?? throw new LuxMapException(ErrorCodes.Unauthenticated, HttpStatusCode.Unauthorized, "Authentication required.");

    /// <summary>
    /// The queue, newest send first: <c>last_submitted_at DESC</c>, then the id by the section 0 rule reversed
    /// (<c>length DESC, id DESC</c>) so equal times still come out in a stable, numeric order.
    /// </summary>
    public async Task<PagedResult<CitizenReportItem>> ListAsync(
        IReadOnlyList<string>? communes, IReadOnlyList<CitizenReportStatus> statuses, string? poleId, PageRequest page,
        CancellationToken ct)
    {
        var reports = db.Set<CitizenReport>().AsNoTracking().Where(r => statuses.Contains(r.Status));
        if (communes is not null) reports = reports.Where(r => communes.Contains(r.CommuneId));
        if (!string.IsNullOrWhiteSpace(poleId)) reports = reports.Where(r => r.PoleId == poleId.Trim());

        var total = await reports.CountAsync(ct);
        var found = await reports
            .OrderByDescending(r => r.LastSubmittedAt).ThenByDescending(r => r.ReportId.Length).ThenByDescending(r => r.ReportId)
            .Skip(page.Skip).Take(page.PageSize).ToListAsync(ct);

        var ids = found.Select(r => r.ReportId).ToArray();
        var sends = await db.Set<CitizenSubmission>().AsNoTracking().Where(s => ids.Contains(s.ReportId))
            .Select(s => new { s.ReportId, s.Kind, s.FarFromPole }).ToListAsync(ct);
        var photos = await PhotoCountsAsync(ids, ct);

        var items = found.Select(r =>
        {
            var mine = sends.Where(s => s.ReportId == r.ReportId).ToList();
            return Item(r, mine.Count, photos.GetValueOrDefault(r.ReportId), [.. mine.Select(s => s.Kind)], mine.Any(s => s.FarFromPole));
        }).ToList();
        return PagedResult<CitizenReportItem>.From(page, total, items);
    }

    /// <summary>
    /// One report with its pole, the open faults on that pole and every send. Photos are listed only when <paramref name="canReview"/>
    /// (BE-20: what is listed must open); for such a caller the photos still without a hint are run through the model first (D-9).
    /// </summary>
    public async Task<CitizenReportDetail> DetailAsync(string id, bool canReview, CancellationToken ct)
    {
        if (canReview) await HintPhotosAsync(id, ct);
        return await ReadDetailAsync(id, canReview, ct);
    }

    /// <summary>A photo of the report, through the report's commune filter. Anything else is the same 404.</summary>
    public async Task<Stream> OpenPhotoAsync(string reportId, string photoId, bool thumbnail, CancellationToken ct)
    {
        var photo = await db.Set<CitizenPhoto>().AsNoTracking()
            .Join(db.Set<CitizenSubmission>(), p => p.SubmissionId, s => s.SubmissionId, (p, s) => new { p.PhotoId, p.ObjectKey, p.ThumbnailKey, s.ReportId })
            .Where(x => x.PhotoId == photoId && x.ReportId == reportId)
            .SingleOrDefaultAsync(ct);
        if (photo is null || !await db.Set<CitizenReport>().AnyAsync(r => r.ReportId == reportId, ct))
        {
            throw new LuxMapException(ErrorCodes.CitizenPhotoNotFound, HttpStatusCode.NotFound,
                "That photo does not exist on that report, or the report is outside your permitted commune scope.");
        }

        return await store.OpenAsync(StorageBucket.Citizen, thumbnail ? photo.ThumbnailKey : photo.ObjectKey, ct);
    }

    /// <summary>
    /// Turns the report into a new <c>field_report</c> fault, <c>confirmed</c> by this Manager (D-18). The type must be backed by what a
    /// citizen said (D-17: out → <c>lamp_out</c>, dim → <c>lamp_dim</c>), and not already open on the pole — merge into that one instead.
    /// </summary>
    /// <remarks>
    /// "Already open" is checked at the moment of accepting, under the pole lock; it is not a system-wide invariant. A field report or
    /// a survey can still open a same-type fault on the pole, and a <c>PATCH /faults</c> reclassification is not locked against this
    /// (locking the pole from there would put a fault → pole order against this pole → fault one). Codex review of the P2c plan #2.
    /// </remarks>
    public async Task<CitizenReportDetail> AcceptAsync(string id, AcceptCitizenReportRequest request, CancellationToken ct)
    {
        if (request.FaultType is not { } type || !LampTypes.Contains(type))
        {
            throw new LuxMapException(ErrorCodes.ValidationFailed, HttpStatusCode.BadRequest,
                "fault_type is required: lamp_out or lamp_dim.",
                new Dictionary<string, object?> { ["field"] = "fault_type", ["allowed"] = LampTypes.Select(WireEnum.Name).ToArray() });
        }

        // The shared enum converter refuses numbers, but an undefined value must not reach the CHECK as text either.
        if (request.Severity is { } severity && !Enum.IsDefined(severity)) throw Invalid("severity", "severity must be low, medium, high or critical.");
        var note = Note(request.Note, "note", required: false);
        var seen = SeenCount(request.SubmissionCount);

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var report = await LockPendingAsync(id, seen, ct);

        var sends = await db.Set<CitizenSubmission>().AsNoTracking().Where(s => s.ReportId == report.ReportId)
            .Select(s => new { s.Kind, s.ReceivedAt }).ToListAsync(ct);
        var kinds = sends.Select(s => s.Kind).ToList();
        if (!kinds.Contains(Backing[type]))
        {
            throw new LuxMapException(ErrorCodes.CitizenReportNotAcceptable, HttpStatusCode.Conflict,
                "No send in this report says what that fault type says; choose a type a citizen reported, reject it, or handle the pole "
                + "through a work order.",
                new Dictionary<string, object?>
                {
                    ["report_id"] = report.ReportId, ["fault_type"] = WireEnum.Name(type),
                    ["kinds"] = kinds.Distinct().Order().Select(WireEnum.Name).ToArray(),
                });
        }

        var duplicates = await db.Set<Fault>().AsNoTracking()
            .Where(f => f.PoleId == report.PoleId && OpenStatuses.Contains(f.FaultStatus))
            .Where(f => f.OverrideFaultType == type || (f.OverrideFaultType == null && f.FaultType == type))
            .Select(f => f.FaultId).ToListAsync(ct);
        if (duplicates.Count > 0)
        {
            throw new LuxMapException(ErrorCodes.OpenFaultExists, HttpStatusCode.Conflict,
                "The pole already has an open fault of that type; merge the report into it instead of opening a second one.",
                new Dictionary<string, object?> { ["report_id"] = report.ReportId, ["fault_ids"] = duplicates.ToArray() });
        }

        var now = UtcMicrosecondClock.UtcNow(clock);
        var fault = await faults.StageReviewedReportAsync(new ReviewedReport(
            // The earliest send: a slow send can be received before the one that opened the report (P2b review #5).
            report.PoleId, type, request.Severity ?? Severity.Medium, sends.Min(s => s.ReceivedAt),
            Summary(report.ReportId, kinds), note, "citizen_report:" + report.ReportId), now, ct);
        await db.SaveChangesAsync(ct);

        Decide(report, CitizenReportStatus.Accepted, fault.FaultId, note, now, AuditAction.Accepted);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return await ReadDetailAsync(report.ReportId, canReview: true, ct);
    }

    /// <summary>Attaches the report to an OPEN fault on the SAME pole. The fault itself is not changed.</summary>
    public async Task<CitizenReportDetail> MergeAsync(string id, MergeCitizenReportRequest request, CancellationToken ct)
    {
        var faultId = request.FaultId?.Trim();
        if (string.IsNullOrEmpty(faultId)) throw Invalid("fault_id", "fault_id is required: the open fault to merge into.");
        var note = Note(request.Note, "note", required: false);
        var seen = SeenCount(request.SubmissionCount);

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var report = await LockPendingAsync(id, seen, ct);

        // Through the commune filter: a fault the Manager cannot see is the same 404 as one that does not exist.
        if (!await db.Set<Fault>().AnyAsync(f => f.FaultId == faultId, ct))
        {
            throw new LuxMapException("FAULT_NOT_FOUND", HttpStatusCode.NotFound,
                "That fault does not exist, or it is outside your permitted commune scope.",
                new Dictionary<string, object?> { ["fault_id"] = faultId });
        }

        await FaultLocks.LockAsync(db, [faultId], ct);
        var fault = await db.Set<Fault>().AsNoTracking().SingleAsync(f => f.FaultId == faultId, ct);
        if (!FaultStatusSets.IsOpen(fault.FaultStatus) || fault.PoleId != report.PoleId)
        {
            throw new LuxMapException(ErrorCodes.FaultNotMergeable, HttpStatusCode.Conflict,
                "A report merges only into an open fault (detected, confirmed, in_progress) on its own pole.",
                new Dictionary<string, object?>
                {
                    ["fault_id"] = fault.FaultId, ["fault_status"] = WireEnum.Name(fault.FaultStatus),
                    ["fault_pole_id"] = fault.PoleId, ["report_pole_id"] = report.PoleId,
                });
        }

        Decide(report, CitizenReportStatus.Merged, fault.FaultId, note, UtcMicrosecondClock.UtcNow(clock), AuditAction.Merged);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return await ReadDetailAsync(report.ReportId, canReview: true, ct);
    }

    /// <summary>Not a fault. The reason is required — it is the only record of why a citizen's report went nowhere.</summary>
    public async Task<CitizenReportDetail> RejectAsync(string id, RejectCitizenReportRequest request, CancellationToken ct)
    {
        var reason = Note(request.Reason, "reason", required: true);
        var seen = SeenCount(request.SubmissionCount);

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var report = await LockPendingAsync(id, seen, ct);
        Decide(report, CitizenReportStatus.Rejected, null, reason, UtcMicrosecondClock.UtcNow(clock), AuditAction.Rejected);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return await ReadDetailAsync(report.ReportId, canReview: true, ct);
    }

    /// <summary>
    /// Through the commune filter (404 outside scope), then the pole lock and the report lock, then the report read again — still
    /// <c>pending</c> (409 otherwise) and still holding the <paramref name="seen"/> sends the Manager read (409 otherwise). Must run
    /// inside the caller's transaction. The first read is a projection, so the read after the locks loads the row afresh.
    /// </summary>
    private async Task<CitizenReport> LockPendingAsync(string id, int seen, CancellationToken ct)
    {
        var scoped = await db.Set<CitizenReport>().AsNoTracking()
            .Where(r => r.ReportId == id).Select(r => new { r.ReportId, r.PoleId, r.CommuneId }).SingleOrDefaultAsync(ct)
            ?? throw NotFound();
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM pole WHERE pole_id = {scoped.PoleId} AND commune_id = {scoped.CommuneId} FOR UPDATE", ct);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM citizen_report WHERE report_id = {scoped.ReportId} AND commune_id = {scoped.CommuneId} FOR UPDATE", ct);

        var report = await db.Set<CitizenReport>().SingleAsync(r => r.ReportId == scoped.ReportId, ct);
        if (report.Status != CitizenReportStatus.Pending)
        {
            throw new LuxMapException(ErrorCodes.CitizenReportAlreadyDecided, HttpStatusCode.Conflict,
                "This report was already decided; a decision is made once.",
                new Dictionary<string, object?>
                {
                    ["report_id"] = report.ReportId, ["status"] = WireEnum.Name(report.Status), ["fault_id"] = report.FaultId,
                });
        }

        // Sends are only ever added, so their count is the version of what there is to read.
        var count = await db.Set<CitizenSubmission>().CountAsync(s => s.ReportId == report.ReportId, ct);
        if (count != seen)
        {
            throw new LuxMapException(ErrorCodes.CitizenReportChanged, HttpStatusCode.Conflict,
                "Citizens sent more since this report was opened; reload it before deciding.",
                new Dictionary<string, object?> { ["report_id"] = report.ReportId, ["submission_count"] = count, ["seen"] = seen });
        }

        return report;
    }

    private static int SeenCount(int? count)
        => count is { } value and >= 1 ? value
            : throw Invalid("submission_count", "submission_count is required: the number of sends the report showed when you read it.");

    private void Decide(CitizenReport report, CitizenReportStatus status, string? faultId, string? note, DateTime now, AuditAction action)
    {
        report.Status = status;
        report.FaultId = faultId;
        report.DecidedBy = ActorId;
        report.DecidedAt = now;
        report.DecisionNote = note;
        audit.Record(new(now, AuditActorKind.User, ActorId, actor.Role, report.CommuneId, AuditEntityType.CitizenReport,
            report.ReportId, action, new { status = CitizenReportStatus.Pending },
            new { report.ReportId, report.PoleId, report.Status, report.FaultId, report.DecidedBy, report.DecidedAt, report.DecisionNote }));
    }

    /// <summary>
    /// Runs the model on up to <c>Citizen:HintsPerRequest</c> photos of the report that have no hint yet, and keeps what it says with
    /// one <c>analyzed</c> audit event (actor <c>cv</c>) — a hint is an automated finding (D-R13), never a decision.
    /// </summary>
    /// <remarks>
    /// The model runs with no transaction and no lock held. Only the write locks the photo rows, and writes the photos STILL without a
    /// hint: two Managers opening the report at once may both run the model, but only one writes and only one event is recorded.
    /// </remarks>
    private async Task HintPhotosAsync(string reportId, CancellationToken ct)
    {
        var report = await db.Set<CitizenReport>().AsNoTracking().Where(r => r.ReportId == reportId)
            .Select(r => new { r.ReportId, r.CommuneId }).SingleOrDefaultAsync(ct);
        if (report is null) return;

        var todo = await db.Set<CitizenPhoto>().AsNoTracking()
            .Join(db.Set<CitizenSubmission>(), p => p.SubmissionId, s => s.SubmissionId, (p, s) => new { Photo = p, s.ReportId })
            .Where(x => x.ReportId == reportId && x.Photo.AiHint == null)
            .OrderBy(x => x.Photo.ReceivedAt).ThenBy(x => x.Photo.PhotoId.Length).ThenBy(x => x.Photo.PhotoId)
            .Select(x => new { x.Photo.PhotoId, x.Photo.ObjectKey, x.Photo.ThumbnailKey })
            .ToListAsync(ct);
        // A report holds at most a night's photo allowance, so reading every candidate is cheap; the cap is on the model runs.
        todo = [.. todo.Where(p => !backoff.IsCooling(p.PhotoId)).Take(options.HintsPerRequest)];

        var hints = new Dictionary<string, PhotoHint>(StringComparer.Ordinal);
        foreach (var photo in todo)
        {
            await using var original = new MemoryStream();
            await using var thumbnail = new MemoryStream();
            try
            {
                await using (var stored = await store.OpenAsync(StorageBucket.Citizen, photo.ObjectKey, ct)) await stored.CopyToAsync(original, ct);
                await using (var stored = await store.OpenAsync(StorageBucket.Citizen, photo.ThumbnailKey, ct)) await stored.CopyToAsync(thumbnail, ct);
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                // A missing object or an unreachable store: no hint this time. The report still opens; the photo endpoint reports it.
                log.LogWarning(error, "Citizen photo {PhotoId} could not be read for an AI hint", photo.PhotoId);
                backoff.Failed(photo.PhotoId);
                continue;
            }

            if (await hinter.HintAsync(original, thumbnail, ct) is { } hint) hints[photo.PhotoId] = hint;
            else backoff.Failed(photo.PhotoId);
        }

        if (hints.Count == 0) return;

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var ids = hints.Keys.ToArray();
        await db.Database.ExecuteSqlRawAsync("SELECT 1 FROM citizen_photo WHERE photo_id = ANY({0}) ORDER BY photo_id FOR UPDATE", [ids], ct);
        var rows = await db.Set<CitizenPhoto>().Where(p => ids.Contains(p.PhotoId) && p.AiHint == null).ToListAsync(ct);
        if (rows.Count == 0) return; // another opening wrote them first

        var now = UtcMicrosecondClock.UtcNow(clock);
        foreach (var row in rows)
        {
            var hint = hints[row.PhotoId];
            row.AiHint = hint.Label;
            row.AiConfidence = hint.Label == CitizenPhotoHint.Inconclusive ? null : hint.Confidence;
            row.AiArtifactVersion = hint.ArtifactVersion;
            row.AiCheckedAt = now;
        }

        audit.Record(new(now, AuditActorKind.Cv, null, null, report.CommuneId, AuditEntityType.CitizenReport, report.ReportId,
            AuditAction.Analyzed, null,
            new { photos = rows.Select(r => new { r.PhotoId, r.AiHint, r.AiConfidence, r.AiArtifactVersion }).ToArray() }));
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        db.ChangeTracker.Clear();
    }

    private async Task<CitizenReportDetail> ReadDetailAsync(string id, bool canReview, CancellationToken ct)
    {
        var report = await db.Set<CitizenReport>().AsNoTracking().SingleOrDefaultAsync(r => r.ReportId == id, ct) ?? throw NotFound();

        var pole = await db.Set<Pole>().AsNoTracking().Where(p => p.PoleId == report.PoleId)
            .Select(p => new { p.PoleId, p.SegmentId, p.Geom }).SingleAsync(ct);
        var status = await db.Set<PoleCurrentStatus>().AsNoTracking().Where(s => s.PoleId == report.PoleId)
            .Select(s => new { s.FixtureStatus, s.LastSeenAt }).SingleOrDefaultAsync(ct);

        var openFaults = await db.Set<Fault>().AsNoTracking()
            .Where(f => f.PoleId == report.PoleId && OpenStatuses.Contains(f.FaultStatus))
            .OrderBy(f => f.DetectedAt).ThenBy(f => f.FaultId.Length).ThenBy(f => f.FaultId)
            .Select(f => new CitizenReportOpenFault
            {
                FaultId = f.FaultId, FaultType = f.OverrideFaultType ?? f.FaultType, FaultStatus = f.FaultStatus,
                Severity = f.Severity, SourceChannel = f.SourceChannel, DetectedAt = f.DetectedAt,
            }).ToListAsync(ct);

        var sends = await db.Set<CitizenSubmission>().AsNoTracking().Where(s => s.ReportId == id)
            .OrderBy(s => s.ReceivedAt).ThenBy(s => s.SubmissionId.Length).ThenBy(s => s.SubmissionId).ToListAsync(ct);
        var sendIds = sends.Select(s => s.SubmissionId).ToArray();
        var photos = await db.Set<CitizenPhoto>().AsNoTracking().Where(p => sendIds.Contains(p.SubmissionId))
            .OrderBy(p => p.Position).ToListAsync(ct);

        var submissions = sends.Select(s =>
        {
            var mine = photos.Where(p => p.SubmissionId == s.SubmissionId).ToList();
            return new CitizenSubmissionItem
            {
                SubmissionId = s.SubmissionId, Kind = s.Kind, Description = s.Description, FarFromPole = s.FarFromPole,
                PhotosDropped = s.PhotosDropped, ReceivedAt = s.ReceivedAt, PhotoCount = mine.Count,
                Photos = canReview ? [.. mine.Select(p => Photo(report.ReportId, p))] : [],
            };
        }).ToList();

        var kinds = sends.Select(s => s.Kind).ToList();
        // What accept would take right now: backed by a send (D-17) and not already open on the pole (OPEN_FAULT_EXISTS).
        List<FaultType> acceptable = report.Status == CitizenReportStatus.Pending
            ? [.. LampTypes.Where(t => kinds.Contains(Backing[t]) && openFaults.All(f => f.FaultType != t))]
            : [];
        var item = Item(report, sends.Count, photos.Count, kinds, sends.Any(s => s.FarFromPole));
        return new CitizenReportDetail
        {
            ReportId = item.ReportId, PoleId = item.PoleId, CommuneId = item.CommuneId, DataSource = item.DataSource,
            NightOf = item.NightOf, Status = item.Status, CreatedAt = item.CreatedAt, LastSubmittedAt = item.LastSubmittedAt,
            SubmissionCount = item.SubmissionCount, PhotoCount = item.PhotoCount, Kinds = item.Kinds, FarFromPole = item.FarFromPole,
            FaultId = item.FaultId, DecidedBy = item.DecidedBy, DecidedAt = item.DecidedAt, DecisionNote = item.DecisionNote,
            Pole = new CitizenReportPole
            {
                PoleId = pole.PoleId, SegmentId = pole.SegmentId, Location = new ReportPoint(pole.Geom.Y, pole.Geom.X),
                FixtureStatus = status?.FixtureStatus ?? FixtureStatus.Unknown, LastSeenAt = status?.LastSeenAt,
            },
            OpenFaults = openFaults,
            Submissions = submissions,
            AllowedActions = AllowedActions(report, canReview, acceptable.Count > 0, openFaults.Count),
            AcceptableFaultTypes = acceptable,
        };
    }

    /// <summary>
    /// What the caller may press now — never an action certain to be refused. <c>accept</c> needs a type it would take
    /// (<see cref="CitizenReportDetail.AcceptableFaultTypes"/>); <c>merge</c> needs an open fault on the pole.
    /// </summary>
    private static List<string> AllowedActions(CitizenReport report, bool canReview, bool acceptable, int openFaults)
    {
        if (!canReview || report.Status != CitizenReportStatus.Pending) return [];
        var actions = new List<string>();
        if (acceptable) actions.Add("accept");
        if (openFaults > 0) actions.Add("merge");
        actions.Add("reject");
        return actions;
    }

    private async Task<Dictionary<string, int>> PhotoCountsAsync(IReadOnlyCollection<string> reportIds, CancellationToken ct)
        => await db.Set<CitizenPhoto>().AsNoTracking()
            .Join(db.Set<CitizenSubmission>(), p => p.SubmissionId, s => s.SubmissionId, (p, s) => s.ReportId)
            .Where(reportId => reportIds.Contains(reportId))
            .GroupBy(reportId => reportId).Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);

    private static CitizenReportItem Item(CitizenReport r, int sends, int photos, IReadOnlyCollection<CitizenReportKind> kinds, bool far) => new()
    {
        ReportId = r.ReportId, PoleId = r.PoleId, CommuneId = r.CommuneId, DataSource = r.DataSource, NightOf = r.NightOf,
        Status = r.Status, CreatedAt = r.CreatedAt, LastSubmittedAt = r.LastSubmittedAt, SubmissionCount = sends, PhotoCount = photos,
        Kinds = [.. kinds.Distinct().Order()], FarFromPole = far, FaultId = r.FaultId, DecidedBy = r.DecidedBy,
        DecidedAt = r.DecidedAt, DecisionNote = r.DecisionNote,
    };

    private static CitizenPhotoItem Photo(string reportId, CitizenPhoto p)
    {
        var path = $"/api/v1/citizen-reports/{reportId}/photos/{p.PhotoId}";
        return new CitizenPhotoItem
        {
            PhotoId = p.PhotoId, Position = p.Position, ThumbnailUrl = path + "/thumbnail", OriginalUrl = path + "/original",
            AiHint = p.AiHint is { } label
                ? new CitizenPhotoAiHint { Label = label, Confidence = p.AiConfidence, ArtifactVersion = p.AiArtifactVersion!, CheckedAt = p.AiCheckedAt!.Value }
                : null,
        };
    }

    /// <summary>The fault's <c>note</c>: what citizens reported, in the server's words — never their free text (it stays on the report).</summary>
    private static string Summary(string reportId, IReadOnlyCollection<CitizenReportKind> kinds)
        => $"Người dân báo qua mã QR ({reportId}, {kinds.Count} lượt gửi): "
           + string.Join(", ", kinds.Distinct().Order().Select(CitizenNotices.KindText)) + ".";

    private static string? Note(string? text, string field, bool required)
    {
        var trimmed = text?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return required ? throw Invalid(field, $"{field} is required.") : null;
        }

        return trimmed.Length > NoteMaxLength ? throw Invalid(field, $"{field} is at most {NoteMaxLength} characters.") : trimmed;
    }

    private static LuxMapException NotFound()
        => new(ErrorCodes.CitizenReportNotFound, HttpStatusCode.NotFound,
            "That citizen report does not exist, or it is outside your permitted commune scope.");

    private static LuxMapException Invalid(string field, string message)
        => new(ErrorCodes.ValidationFailed, HttpStatusCode.BadRequest, message,
            new Dictionary<string, object?> { ["field"] = field });
}
