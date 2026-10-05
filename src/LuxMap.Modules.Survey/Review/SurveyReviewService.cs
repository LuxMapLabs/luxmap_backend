using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using LuxMap.Modules.Assets.Entities;
using LuxMap.Modules.Faults.Entities;
using LuxMap.Modules.Notifications;
using LuxMap.Modules.Survey.Entities;
using LuxMap.Modules.WorkOrders.Entities;
using LuxMap.Persistence;
using LuxMap.Persistence.Audit;
using LuxMap.Shared.Authorization;
using LuxMap.Shared.Contracts;
using LuxMap.Shared.Contracts.Enums;
using LuxMap.Shared.Contracts.Paging;
using LuxMap.Shared.Http;
using LuxMap.Shared.Serialization;
using LuxMap.Shared.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace LuxMap.Modules.Survey.Review;

public sealed record ReviewSweepRequest(Guid ClientOpId, long RunId, [Required] string Decision,
    string? Note, [Required] uint? ExpectedVersion);
public sealed record ReviewSweepResponse(string SweepId, SweepStatus Status, long? AcceptedRunId,
    string? ReviewedBy, DateTime? ReviewedAt, string? Note, uint Version);
public sealed record SurveyResultItem(long? ObservationId, string PoleId, long RunId, long? PassId, string? Direction,
    DateTime EvaluatedAt, string? CvState, double? CvConfidence, double? PeakLux, long? BaselineId,
    double? BaselineValue, double? BaselineRatio, FixtureStatus ClassifiedAs, bool DimEvaluationEligible,
    string[] ReasonCodes, string[] QualityFlags, string? FrameId, double? AssociationConfidence, FixtureStatus PublishedAs, bool IsRepresentative);

public sealed class SurveyReviewService(LuxMapDbContext db, ICurrentActorAccessor actor,
    ICommuneScopeAccessor scope, IAuditTrail audit, IObjectStore store, IOptions<SurveyReviewOptions> options)
{
    private string ActorId => actor.UserId ?? throw Error("UNAUTHENTICATED", HttpStatusCode.Unauthorized);
    private readonly SurveyMediaAccess access = new(db, scope);
    private IQueryable<SurveySweep> Visible() => access.VisibleSweeps();
    private async Task<SurveySweep> Find(string id, CancellationToken ct) => await Visible().AsNoTracking()
        .SingleOrDefaultAsync(s => s.SweepId == id, ct) ?? throw Error("SWEEP_NOT_FOUND", HttpStatusCode.NotFound);

    private async Task RequireWholeRun(SurveyProcessingRun run, bool review, CancellationToken ct)
    {
        if (!await access.CoversWholeRun(run, ct))
            throw Error(review ? "COMMUNE_FORBIDDEN" : "SWEEP_NOT_FOUND", review ? HttpStatusCode.Forbidden : HttpStatusCode.NotFound);
    }

    public async Task<PagedResult<SurveyResultItem>> Results(string id, long? runId, PageRequest page, CancellationToken ct)
    {
        var sweep = await Find(id, ct);
        var runs = db.Set<SurveyProcessingRun>().Where(r => r.SweepId == id && r.ResultState == "succeeded");
        var run = await (runId.HasValue ? runs.Where(r => r.RunId == runId) : runs.OrderByDescending(r => r.Attempt))
            .FirstOrDefaultAsync(ct) ?? throw Error("RUN_NOT_FOUND", HttpStatusCode.NotFound);
        await RequireWholeRun(run, false, ct);
        var query = db.Set<PoleObservation>().AsNoTracking().Where(o => o.RunId == run.RunId);
        var count = await query.CountAsync(ct);
        var rows = await query.Include(o => o.Pass).OrderBy(o => o.ObservedAt).ThenBy(o => o.PoleId.Length)
            .ThenBy(o => o.PoleId).ThenBy(o => o.Pass.PassNo).Skip(page.Skip).Take(page.PageSize).ToArrayAsync(ct);
        // Fetch all passes for the page's poles before choosing: a conflicting pass can be on another page.
        var poleIds = rows.Select(o => o.PoleId).Distinct().ToArray();
        var allPasses = await query.Where(o => poleIds.Contains(o.PoleId)).ToArrayAsync(ct);
        var observedIds = await query.Select(o => o.PoleId).Distinct().ToArrayAsync(ct);
        var unobserved = SurveyPublicationRules.UnobservedPoles(run.GisSnapshot, observedIds);
        var missingPage = unobserved.Skip(Math.Max(0, page.Skip - count)).Take(page.PageSize - rows.Length)
            .Select(poleId => UnobservedPreview(poleId, run.RunId, sweep)).ToArray();
        return PagedResult<SurveyResultItem>.From(page, count + unobserved.Length, Preview(rows, allPasses).Concat(missingPage).ToArray());
    }

    public static SurveyResultItem[] Preview(PoleObservation[] page, PoleObservation[] allPasses)
    {
        var choices = allPasses.GroupBy(o => o.PoleId).ToDictionary(g => g.Key, SurveyPublicationRules.Choose);
        return page.Select(o => new SurveyResultItem(o.ObservationId,
            o.PoleId, o.RunId, o.PassId, o.Pass.Direction, o.ObservedAt, o.CvState, o.CvConfidence, o.PeakLux,
            o.BaselineId, o.BaselineValue, o.BaselineRatio, o.ClassifiedAs, o.DimEvaluationEligible,
            SurveyPublicationRules.Flags(o.ReasonCodes), SurveyPublicationRules.Flags(o.QualityFlags),
            o.RepresentativeFrameId, o.AssociationConfidence, choices[o.PoleId].Status,
            choices[o.PoleId].Observation.ObservationId == o.ObservationId)).ToArray();
    }

    public static SurveyResultItem UnobservedPreview(string poleId, long runId, SurveySweep sweep)
        => new(null, poleId, runId, null, null,
            sweep.AtElapsed(sweep.EndedElapsedNs) ?? throw new InvalidOperationException("A completed sweep must have a valid end time."),
            null, null, null, null, null, null, FixtureStatus.Unknown, false,
            ["not_observed"], [], null, null, FixtureStatus.Unknown, false);

    public async Task<Stream> Thumbnail(string frameId, CancellationToken ct)
    {
        var visible = Visible();
        var frame = await db.Set<SurveyFrame>().AsNoTracking().Where(f => f.FrameId == frameId
            && visible.Any(s => s.SweepId == f.SweepId)).SingleOrDefaultAsync(ct)
            ?? throw Error("FRAME_NOT_FOUND", HttpStatusCode.NotFound);
        // The whole image can show neighbouring communes: SurveyMediaAccess checks every run and the
        // snapshot communes before the object is opened. Assignee scope was applied by Visible().
        if (!await access.CanReadMedia(frame.SweepId, ct)) throw Error("FRAME_NOT_FOUND", HttpStatusCode.NotFound);
        return await store.OpenAsync(StorageBucket.Survey, frame.ThumbnailKey, ct);
    }

    public async Task<ReviewSweepResponse> Review(string id, ReviewSweepRequest request, CancellationToken ct)
    {
        if (request.ClientOpId == Guid.Empty || request.RunId <= 0 || request.ExpectedVersion is null
            || request.Decision is not ("accept" or "return") || request.Decision == "return" && string.IsNullOrWhiteSpace(request.Note))
            throw Error("VALIDATION_FAILED", HttpStatusCode.BadRequest);
        request = request with { Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim() };
        var actorId = ActorId;
        var hash = Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { id, actorId, request }, LuxMapJsonOptions.Default)));
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var initial = await Find(id, ct);
        // Match ingest/worker lock ordering. Re-read after every lock to avoid stale tracked xmin.
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM work_order WHERE work_order_id = {initial.WorkOrderId} FOR UPDATE", ct);
        await Find(id, ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM survey_sweep WHERE sweep_id = {id} FOR UPDATE", ct);
        var sweep = await db.Set<SurveySweep>().SingleAsync(s => s.SweepId == id, ct);
        await db.Entry(sweep).ReloadAsync(ct);
        var run = await db.Set<SurveyProcessingRun>().SingleOrDefaultAsync(r => r.RunId == request.RunId && r.SweepId == id && r.ResultState == "succeeded", ct)
            ?? throw Error("INVALID_REVIEW_RUN", HttpStatusCode.Conflict);
        await RequireWholeRun(run, true, ct);
        if (sweep.ReviewClientOpId == request.ClientOpId)
        {
            if (sweep.ReviewRequestHash != hash) throw Error("IDEMPOTENCY_CONFLICT", HttpStatusCode.Conflict);
            return Response(sweep);
        }
        if (sweep.Status != SweepStatus.AwaitingReview) throw Error("SWEEP_ALREADY_REVIEWED", HttpStatusCode.Conflict);
        if (sweep.Version != request.ExpectedVersion) throw Error("VERSION_CONFLICT", HttpStatusCode.Conflict);
        if (await db.Set<SurveySweep>().AnyAsync(s => s.ReviewedBy == actorId && s.ReviewClientOpId == request.ClientOpId, ct))
            throw Error("IDEMPOTENCY_CONFLICT", HttpStatusCode.Conflict);
        var observations = await db.Set<PoleObservation>().Include(o => o.Pass).Where(o => o.RunId == run.RunId).ToArrayAsync(ct);
        var targets = SurveyPublicationRules.PublicationSet(run.GisSnapshot, observations);
        // Only accepting publishes, so only accepting needs the poles locked and still compatible with the
        // run. A return records a decision; refusing it after an asset correction would strand the sweep.
        var poles = new Dictionary<string, Pole>();
        if (request.Decision == "accept")
        {
            // Lock the parent pole, including poles without a current-status row, in a global order.
            foreach (var poleId in targets.Select(c => c.PoleId).Order(StringComparer.Ordinal))
                await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM pole WHERE pole_id = {poleId} FOR UPDATE", ct);
            var ids = targets.Select(c => c.PoleId).ToArray();
            poles = await db.Set<Pole>().AsNoTracking().Where(p => ids.Contains(p.PoleId)).ToDictionaryAsync(p => p.PoleId, ct);
            if (targets.Any(c => !poles.TryGetValue(c.PoleId, out var pole) || pole.DataSource != sweep.DataSource
                || c.Choice is { } choice && pole.CommuneId != choice.Observation.CommuneId)
                || SurveyPublicationRules.ExpectedPoles(run.GisSnapshot).Any(p => poles[p.PoleId].CommuneId != p.CommuneId))
                throw Error("SURVEY_SCOPE_CHANGED", HttpStatusCode.Conflict);
        }
        var now = UtcMicrosecondClock.UtcNow();
        var before = new { sweep.Status };
        sweep.Status = request.Decision == "accept" ? SweepStatus.Accepted : SweepStatus.Returned;
        sweep.AcceptedRunId = request.Decision == "accept" ? run.RunId : null;
        sweep.ReviewClientOpId = request.ClientOpId; sweep.ReviewRequestHash = hash;
        sweep.ReviewedBy = actorId; sweep.ReviewedAt = now; sweep.ReviewNote = request.Note; sweep.UpdatedAt = now;
        audit.Record(new(now, AuditActorKind.User, actorId, actor.Role, sweep.CommuneId, AuditEntityType.SurveySweep,
            id, request.Decision == "accept" ? AuditAction.Confirmed : AuditAction.Returned, before,
            new { sweep.Status, sweep.AcceptedRunId, request.RunId, request.ClientOpId }, request.Note));
        if (request.Decision == "return")
        {
            // BE-27: whoever holds the work order NOW acts on it; the engineer who filmed it learns too.
            var holder = await db.Set<WorkOrder>().Where(w => w.WorkOrderId == sweep.WorkOrderId).Select(w => w.AssignedTo).SingleOrDefaultAsync(ct);
            Notifier.Stage(db, SurveyNotices.Returned(sweep, request.Note), [holder, sweep.CapturedBy], actorId, now);
        }
        try
        {
            await db.SaveChangesAsync(ct);
            if (request.Decision == "accept") await Publish(sweep, run, targets, observations, poles, now, ct);
            await tx.CommitAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        { throw Error("IDEMPOTENCY_CONFLICT", HttpStatusCode.Conflict); }
        return Response(sweep);
    }

    private async Task Publish(SurveySweep sweep, SurveyProcessingRun run, PublicationTarget[] targets,
        PoleObservation[] observations, Dictionary<string, Pole> poles, DateTime now, CancellationToken ct)
    {
        var settings = options.Value;
        var modelVersion = run.ModelVersionId is null ? null : await db.Set<ArtifactVersion>()
            .Where(v => v.VersionId == run.ModelVersionId).Select(v => v.Version).SingleAsync(ct);
        foreach (var commune in targets.GroupBy(c => poles[c.PoleId].CommuneId).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var faults = new List<(PublicationChoice Choice, FaultType Type)>();
            foreach (var target in commune)
            {
                var choice = target.Choice;
                var history = SurveyPublicationRules.History(target, sweep, run.RunId, poles[target.PoleId].CommuneId, now, ActorId);
                db.Add(history);
                var current = await db.Set<PoleCurrentStatus>().SingleOrDefaultAsync(p => p.PoleId == target.PoleId, ct);
                if (SurveyPublicationRules.IsNewer(history.EvaluatedAt, current?.LastEvaluatedAt ?? current?.LastSeenAt))
                {
                    if (current is null) { current = new() { PoleId = target.PoleId, CommuneId = history.CommuneId }; db.Add(current); }
                    current.FixtureStatus = history.ClassifiedAs; current.StatusConfidence = history.StatusConfidence;
                    current.LastSeenAt = history.ClassifiedAs == FixtureStatus.Unknown ? null : history.EvaluatedAt;
                    current.LastEvaluatedAt = history.EvaluatedAt; current.LastRunId = run.RunId;
                    current.LastSweepId = sweep.SweepId; current.UpdatedAt = now;
                    if (choice is not null && SurveyPublicationRules.FaultFor(choice.Status) is { } type) faults.Add((choice, type));
                }
                if (choice is not null && choice.Status != FixtureStatus.Unknown)
                    await BuildBaselines(sweep, run, observations.Where(x => x.PoleId == target.PoleId).ToArray(), now, ct);
            }
            audit.Record(new(now, AuditActorKind.User, ActorId, actor.Role, commune.Key, AuditEntityType.SurveySweep,
                sweep.SweepId, AuditAction.Completed, null,
                new { run.RunId, pole_ids = commune.Select(c => c.PoleId).ToArray(), settings.BaselineMinimumMembers }));
            await db.SaveChangesAsync(ct);
            // EF cannot translate `override ?? fault_type` on text-stored enums, nor Contains on a set:
            // two branches over the effective type, and the open set (still FaultStatusSets.Open) as an array.
            var open = FaultStatusSets.Open.ToArray();
            foreach (var (choice, type) in faults)
            {
                var o = choice.Observation;
                FaultType? overridden = type;
                if (await db.Set<Fault>().AnyAsync(f => f.PoleId == o.PoleId && open.Contains(f.FaultStatus)
                    && (f.OverrideFaultType == overridden || (f.OverrideFaultType == null && f.FaultType == type)), ct)) continue;
                if (modelVersion is null) throw Error("SURVEY_MODEL_MISSING", HttpStatusCode.Conflict);
                var sql = $"SELECT {PrefixedIds.Fault.DefaultValueSql} AS \"Value\"";
                var faultId = await db.Database.SqlQueryRaw<string>(sql).SingleAsync(ct);
                var pole = poles[o.PoleId];
                var fault = new Fault { FaultId = faultId, PoleId = o.PoleId, CommuneId = o.CommuneId, SegmentId = pole.SegmentId,
                    FaultType = type, FaultStatus = FaultStatus.Detected, SourceChannel = SourceChannel.Cv,
                    DataSource = sweep.DataSource, OriginObservationId = o.ObservationId, DetectionModelVersion = modelVersion,
                    Severity = SurveyPublicationRules.SeverityFor(type, pole.NearSensitivePoi, settings),
                    StatusConfidence = choice.Confidence, DetectedAt = o.ObservedAt, CreatedAt = now, UpdatedAt = now };
                db.Add(fault);
                audit.Record(new(now, AuditActorKind.Cv, null, null, o.CommuneId, AuditEntityType.Fault, faultId,
                    AuditAction.Created, null, new { fault.FaultId, fault.PoleId, fault.FaultType, fault.FaultStatus,
                        fault.Severity, fault.DataSource, fault.OriginObservationId, fault.DetectionModelVersion, run.RunId }));
                await db.SaveChangesAsync(ct);
            }
        }
    }

    private async Task BuildBaselines(SurveySweep sweep, SurveyProcessingRun run, PoleObservation[] current, DateTime now, CancellationToken ct)
    {
        var poleId = current[0].PoleId;
        var fixture = await db.Set<Fixture>().AsNoTracking().SingleOrDefaultAsync(f => f.PoleId == poleId && f.RemovedDate == null, ct);
        var candidates = await db.Set<PoleObservation>().Include(o => o.Pass).Include(o => o.Run)
            .Where(o => o.PoleId == poleId && o.DataSource == sweep.DataSource
                && db.Set<SurveySweep>().Any(s => s.SweepId == o.Run.SweepId && s.Status == SweepStatus.Accepted && s.AcceptedRunId == o.RunId)
                && db.Set<LuminanceHistory>().Any(h => h.PoleId == poleId && h.RunId == o.RunId && h.ClassifiedAs != FixtureStatus.Unknown))
            .ToArrayAsync(ct);
        int version = await db.Set<LuminanceBaseline>().Where(b => b.PoleId == poleId).Select(b => (int?)b.Version).MaxAsync(ct) ?? 0;
        foreach (var direction in current.Where(SurveyPublicationRules.Eligible).Select(o => o.Pass.Direction).Distinct())
        {
            if (!SurveyPublicationRules.Members(current, direction, fixture?.InstallDate).Any()) continue;
            var members = SurveyPublicationRules.Members(candidates.Concat(current).DistinctBy(o => o.ObservationId),
                direction, fixture?.InstallDate).ToArray();
            var value = SurveyPublicationRules.Median(members, direction, options.Value.BaselineMinimumMembers);
            if (value is null) continue;
            var baseline = new LuminanceBaseline { PoleId = poleId, CommuneId = current[0].CommuneId,
                FixtureId = fixture?.FixtureId, Version = ++version, DataSource = sweep.DataSource, Direction = direction, Value = value.Value,
                MemberCount = members.Length, AlgorithmVersionId = run.ClassificationVersionId ?? run.AlgorithmVersionId,
                CreatedAt = now, CreatedBy = ActorId };
            db.Add(baseline);
            db.AddRange(members.Select(o => new BaselineMember { Baseline = baseline, ObservationId = o.ObservationId }));
        }
    }

    private static ReviewSweepResponse Response(SurveySweep s) => new(s.SweepId, s.Status, s.AcceptedRunId, s.ReviewedBy, s.ReviewedAt, s.ReviewNote, s.Version);
    private static LuxMapException Error(string code, HttpStatusCode status) => new(code, status, code.Replace('_', ' '));
}
