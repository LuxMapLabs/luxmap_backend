using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using LuxMap.Modules.Assets.Entities;
using LuxMap.Modules.Citizen.Configurations;
using LuxMap.Modules.Citizen.Entities;
using LuxMap.Modules.Notifications;
using LuxMap.Modules.WorkOrders;
using LuxMap.Persistence;
using LuxMap.Persistence.Audit;
using LuxMap.Shared.Contracts;
using LuxMap.Shared.Contracts.Errors;
using LuxMap.Shared.Http;
using LuxMap.Shared.Serialization;
using LuxMap.Shared.Storage;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using Npgsql;

namespace LuxMap.Modules.Citizen;

/// <summary>One uploaded photo, not yet read: its declared length and a way to open it (as often as needed).</summary>
public sealed record PhotoUpload(long Length, Func<Stream> Open);

/// <summary>A send from the public form, as the controller read it. Strings are raw; this service validates.</summary>
public sealed record PublicReportInput(
    string? Code, string? Kind, string? Description, double? Lat, double? Lng, Guid? ClientOpId, IReadOnlyList<PhotoUpload> Photos);

/// <summary>What the public page learns about a code: enough to show the citizen they scanned the right pole, nothing more (D-4).</summary>
public sealed record PublicQrInfo
{
    public required string SegmentName { get; init; }
    public required string CommuneName { get; init; }
}

/// <summary>The answer to a send. No internal id, no queue state — only what the citizen's page needs.</summary>
public sealed record PublicReportReceipt
{
    public required Guid ClientOpId { get; init; }
    public required DateTime ReceivedAt { get; init; }

    /// <summary>Photos kept. Fewer than sent when the night's allowance was used up — the text is always kept.</summary>
    public required int PhotosKept { get; init; }
}

/// <summary>
/// CITIZEN-QR P2b — the two anonymous endpoints (D-1…D-16, review P1 #1–#5). See <see cref="CitizenScopeFactory"/> for why
/// nothing here touches the request's own (empty-scope) context.
/// </summary>
public sealed partial class PublicReportService(
    CitizenScopeFactory scopes,
    IImageSanitizer sanitizer,
    IObjectStore store,
    CitizenOptions options,
    WorkOrderAgendaOptions nights,
    ICorrelationIdAccessor correlation,
    TimeProvider clock)
{
    [GeneratedRegex("^[0-9A-Za-z]{16}$")]
    private static partial Regex CodeShape();

    public async Task<PublicQrInfo> LookupAsync(string? code, CancellationToken ct)
    {
        var target = await ResolveAsync(code, ct);
        await using var db = scopes.Open(target.CommuneId);
        return await db.Set<Pole>().AsNoTracking()
            .Where(p => p.PoleId == target.PoleId)
            .Select(p => new PublicQrInfo
            {
                SegmentName = db.Set<RoadSegment>().IgnoreQueryFilters()
                    .Where(s => s.SegmentId == p.SegmentId).Select(s => s.SegmentName).First(),
                CommuneName = db.Set<AdministrativeUnit>()
                    .Where(u => u.CommuneId == p.CommuneId).Select(u => u.Name).First(),
            })
            .SingleOrDefaultAsync(ct) ?? throw NotFound();
    }

    /// <remarks>
    /// Order (Codex review P2b #2, #4–#7): validate → code → content hash (photos streamed, never held) → early replay → photo allowance
    /// (cheap, unlocked) → clean EVERY photo before any is stored → pole lock → the code re-read (a revoke during the upload wins) →
    /// replay re-checked → allowance re-checked against the real cleaned bytes → objects stored → rows. Objects are stored under the
    /// lock so a send that loses the allowance or a replay race never leaves photos behind; they are a few hundred KB each after
    /// cleaning, so the lock is held for seconds, not for an upload.
    /// </remarks>
    public async Task<PublicReportReceipt> SubmitAsync(PublicReportInput input, CancellationToken ct)
    {
        var send = Validate(input);
        var target = await ResolveAsync(input.Code, ct);
        var hash = await RequestHashAsync(input.Code!.Trim(), send, input.Photos, ct);

        if (await ReplayAsync(send.ClientOpId, target.QrId, hash, ct) is { } replay) return replay;

        var now = UtcMicrosecondClock.UtcNow(clock);
        var night = nights.NightOf(new DateTimeOffset(now, TimeSpan.Zero));
        var (from, to) = (nights.NightStartUtc(night), nights.NightStartUtc(night.AddDays(1)));

        await using var db = scopes.Open(target.CommuneId);
        var farFromPole = send.Position is { } here && await DistanceAsync(db, target.PoleId, here, ct) > options.FarFromPoleMeters;

        // Early allowance (no lock): spares the decode when the night is already used up. Declared lengths stand in for bytes here.
        var dropped = input.Photos.Count == 0 ? null
            : await AllowanceAsync(db, target.QrId, input.Photos.Count, input.Photos.Sum(p => p.Length), from, to, ct);
        var cleaned = new List<byte[]>();
        if (dropped is null)
        {
            var limits = new ImageLimits(options.PhotoMaxBytes, options.PhotoMaxPixels, options.PhotoMaxLongEdge);
            foreach (var photo in input.Photos)
            {
                await using var stream = photo.Open();
                cleaned.Add(await sanitizer.SanitizeJpegAsync(stream, limits, ct));
            }
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM pole WHERE pole_id = {target.PoleId} AND commune_id = {target.CommuneId} FOR UPDATE", ct);

        // Revoking takes this same pole lock (P2a), so this read cannot race it: a code withdrawn while the photos were being cleaned
        // no longer files anything (Codex review P2b #4).
        if (!await db.Set<QrCode>().AnyAsync(qr => qr.QrId == target.QrId && qr.RevokedAt == null, ct)) throw NotFound();

        // The same send may have won the lock first; answer its receipt rather than store a second set of photos.
        if (await ReplayAsync(send.ClientOpId, target.QrId, hash, ct) is { } raced) return raced;

        if (cleaned.Count > 0 && await AllowanceAsync(db, target.QrId, cleaned.Count, cleaned.Sum(c => (long)c.Length), from, to, ct) is { } late)
        {
            dropped = late;
            cleaned.Clear();
        }

        var kept = new List<(string PhotoId, StoredImage Stored)>();
        foreach (var clean in cleaned)
        {
            var photoId = await NextIdAsync(db, PrefixedIds.CitizenPhoto, ct);
            // Object first, row second (BE-11 rule 3) — an object is orphaned only if the row write below fails.
            kept.Add((photoId, await store.StoreImageAsync(StorageBucket.Citizen, photoId, new MemoryStream(clean, false), ct)));
        }

        var report = await db.Set<CitizenReport>().SingleOrDefaultAsync(
            r => r.PoleId == target.PoleId && r.NightOf == night && r.Status == CitizenReportStatus.Pending, ct);
        var opened = report is null;
        if (report is null)
        {
            var dataSource = await db.Set<Pole>().Where(p => p.PoleId == target.PoleId).Select(p => p.DataSource).SingleAsync(ct);
            report = new CitizenReport
            {
                ReportId = await NextIdAsync(db, PrefixedIds.CitizenReport, ct), PoleId = target.PoleId, CommuneId = target.CommuneId,
                DataSource = dataSource, NightOf = night, Status = CitizenReportStatus.Pending, CreatedAt = now, LastSubmittedAt = now,
            };
            db.Add(report);
        }
        else if (now > report.LastSubmittedAt)
        {
            // A slower send can arrive after a faster one opened the item: the newest time wins, never an older one (review #5).
            report.LastSubmittedAt = now;
        }

        var submission = new CitizenSubmission
        {
            SubmissionId = await NextIdAsync(db, PrefixedIds.CitizenSubmission, ct), ReportId = report.ReportId, QrId = target.QrId,
            PoleId = target.PoleId, CommuneId = target.CommuneId, ClientOpId = send.ClientOpId, RequestHash = hash, Kind = send.Kind,
            Description = send.Description, ReporterLat = send.Position?.Lat, ReporterLng = send.Position?.Lng,
            FarFromPole = farFromPole, PhotosDropped = input.Photos.Count > 0 ? dropped : null, ReceivedAt = now,
        };
        db.Add(submission);
        foreach (var ((photoId, stored), position) in kept.Select((photo, index) => (photo, index + 1)))
        {
            db.Add(new CitizenPhoto
            {
                PhotoId = photoId, SubmissionId = submission.SubmissionId, Position = position, ObjectKey = stored.OriginalKey,
                ThumbnailKey = stored.ThumbnailKey, ByteCount = stored.OriginalBytes, ThumbnailBytes = stored.ThumbnailBytes, ReceivedAt = now,
            });
        }

        // One event per save (BE-23a), actor `public` (D-15). Never the code, never the description: the row holds those.
        CitizenScopeFactory.AuditFor(db, correlation).Record(new(now, AuditActorKind.Public, null, null, target.CommuneId,
            AuditEntityType.CitizenReport, report.ReportId, AuditAction.Submitted, null,
            new
            {
                report.ReportId, submission.SubmissionId, opened_report = opened, kind = send.Kind,
                photos_kept = kept.Count, photos_dropped = submission.PhotosDropped, far_from_pole = farFromPole,
            }));

        // A repeat for the same pole and night joins the item already in the queue: no second notice.
        if (opened)
        {
            Notifier.Stage(db, CitizenNotices.Received(report, send.Kind),
                await Notifier.ManagersCoveringAsync(db, [target.CommuneId], ct), null, now);
        }

        try
        {
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch (DbUpdateException error) when (error.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: CitizenSubmissionConfiguration.ClientOpIdIndex })
        {
            // Only reachable if the same key raced past both replay checks under different poles — answer what was stored.
            await transaction.RollbackAsync(ct);
            return await ReplayAsync(send.ClientOpId, target.QrId, hash, ct) ?? throw Conflict();
        }

        return new PublicReportReceipt { ClientOpId = send.ClientOpId, ReceivedAt = now, PhotosKept = kept.Count };
    }

    private sealed record Send(Guid ClientOpId, CitizenReportKind Kind, string? Description, (double Lat, double Lng)? Position);

    private sealed record Target(string QrId, string PoleId, string CommuneId);

    private Send Validate(PublicReportInput input)
    {
        if (input.ClientOpId is not { } key || key == Guid.Empty) throw Invalid("client_op_id", "client_op_id is required, as a UUID.");
        if (WireEnum.ParseCsv<CitizenReportKind>(input.Kind, "kind") is not [var kind])
            throw Invalid("kind", "kind is required: lamp_out, lamp_dim, pole_damaged or other.");
        var description = string.IsNullOrWhiteSpace(input.Description) ? null : input.Description.Trim();
        if (description is { Length: > 1000 }) throw Invalid("description", "description is at most 1000 characters.");
        (double, double)? position = (input.Lat, input.Lng) switch
        {
            (null, null) => null,
            ({ } lat, { } lng) when double.IsFinite(lat) && double.IsFinite(lng) && lat is >= -90 and <= 90 && lng is >= -180 and <= 180
                => (lat, lng),
            _ => throw Invalid("lat", "lat and lng come together, as finite WGS 84 degrees."),
        };
        if (input.Photos.Count > options.MaxPhotos) throw Invalid("photos", $"At most {options.MaxPhotos} photos.");
        if (input.Photos.Any(photo => photo.Length > options.PhotoMaxBytes))
        {
            throw new LuxMapException(ErrorCodes.ValidationFailed, HttpStatusCode.RequestEntityTooLarge,
                $"A photo may be at most {options.PhotoMaxBytes} bytes.",
                new Dictionary<string, object?> { ["field"] = "photos", ["max_bytes"] = options.PhotoMaxBytes });
        }
        return new Send(key, kind, description, position);
    }

    /// <summary>
    /// Code → the pole and commune it names, UNFILTERED and nothing else. Malformed, unknown and revoked codes take the same
    /// path to the same 404 (D-4); a malformed one never reaches the database.
    /// </summary>
    private async Task<Target> ResolveAsync(string? code, CancellationToken ct)
    {
        var value = code?.Trim();
        if (value is null || !CodeShape().IsMatch(value)) throw NotFound();
        await using var db = scopes.Unscoped();
        return await db.Set<QrCode>().IgnoreQueryFilters().AsNoTracking()
            .Where(qr => qr.Code == value && qr.RevokedAt == null)
            .Select(qr => new Target(qr.QrId, qr.PoleId, qr.CommuneId))
            .SingleOrDefaultAsync(ct) ?? throw NotFound();
    }

    /// <summary>The same key, code and content again → the first receipt; the key under anything else → 409 (review P1 #4).</summary>
    private async Task<PublicReportReceipt?> ReplayAsync(Guid key, string qrId, string hash, CancellationToken ct)
    {
        await using var db = scopes.Unscoped();
        var earlier = await db.Set<CitizenSubmission>().IgnoreQueryFilters().AsNoTracking()
            .Where(s => s.ClientOpId == key)
            .Select(s => new
            {
                s.SubmissionId, s.QrId, s.RequestHash, s.ReceivedAt,
                Photos = db.Set<CitizenPhoto>().Count(p => p.SubmissionId == s.SubmissionId),
            })
            .SingleOrDefaultAsync(ct);
        if (earlier is null) return null;
        if (earlier.QrId != qrId || earlier.RequestHash != hash) throw Conflict();
        return new PublicReportReceipt { ClientOpId = key, ReceivedAt = earlier.ReceivedAt, PhotosKept = earlier.Photos };
    }

    /// <summary>
    /// Null when <paramref name="count"/> more photos of <paramref name="incomingBytes"/> fit this code's night and the system's night;
    /// otherwise why not. The incoming bytes count against the budget (Codex review P2b #6).
    /// </summary>
    private async Task<PhotosDroppedReason?> AllowanceAsync(
        LuxMapDbContext db, string qrId, int count, long incomingBytes, DateTime from, DateTime to, CancellationToken ct)
    {
        var used = await db.Set<CitizenPhoto>()
            .Join(db.Set<CitizenSubmission>(), p => p.SubmissionId, s => s.SubmissionId, (p, s) => new { s.QrId, p.ReceivedAt })
            .CountAsync(x => x.QrId == qrId && x.ReceivedAt >= from && x.ReceivedAt < to, ct);
        if (used + count > options.PhotosPerCodePerNight) return PhotosDroppedReason.OverQuota;

        // System-wide, so it reads past this commune's filter on purpose — a sum of bytes, no row leaves this method. Not serialised
        // across communes: two sends at once in different communes may overshoot by their own photos, the accepted margin.
        var bytes = await db.Set<CitizenPhoto>().IgnoreQueryFilters()
            .Where(p => p.ReceivedAt >= from && p.ReceivedAt < to)
            .SumAsync(p => (long?)(p.ByteCount + p.ThumbnailBytes), ct) ?? 0;
        return bytes + incomingBytes > options.PhotoBudgetBytesPerNight ? PhotosDroppedReason.OverBudget : null;
    }

    /// <summary>
    /// SHA-256 over the code and the send's content, photos included: what makes a retry "the same send". Photos are hashed as
    /// streams — nothing is held in memory before a sanitizer slot is taken.
    /// </summary>
    private static async Task<string> RequestHashAsync(string code, Send send, IReadOnlyList<PhotoUpload> photos, CancellationToken ct)
    {
        var text = new StringBuilder()
            .Append(code).Append('\n').Append(send.Kind).Append('\n').Append(send.Description).Append('\n')
            .Append(send.Position is { } p ? FormattableString.Invariant($"{p.Lat:R},{p.Lng:R}") : "").Append('\n');
        foreach (var photo in photos)
        {
            await using var stream = photo.Open();
            text.Append(Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, ct))).Append('\n');
        }

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }

    private static async Task<double> DistanceAsync(LuxMapDbContext db, string poleId, (double Lat, double Lng) here, CancellationToken ct)
    {
        var point = new Point(here.Lng, here.Lat) { SRID = 4326 };
        return await db.Set<Pole>().Where(p => p.PoleId == poleId)
            .Select(p => SpatialFunctions.DistanceMeters(p.Geom, point)).SingleAsync(ct);
    }

    private static async Task<string> NextIdAsync(LuxMapDbContext db, PrefixedIdSpec spec, CancellationToken ct)
    {
        // The same draw the column DEFAULT makes (FaultReportService.NextFaultId); the SQL is a constant of PrefixedIds, never input.
        var sql = $"SELECT {spec.DefaultValueSql} AS \"Value\"";
        return await db.Database.SqlQueryRaw<string>(sql).SingleAsync(ct);
    }

    private static LuxMapException NotFound()
        => new(ErrorCodes.PublicQrNotFound, HttpStatusCode.NotFound, "This QR code is not in use.");

    private static LuxMapException Conflict()
        => new(ErrorCodes.IdempotencyConflict, HttpStatusCode.Conflict, "This client_op_id was already used for a different report.");

    private static LuxMapException Invalid(string field, string message)
        => new(ErrorCodes.ValidationFailed, HttpStatusCode.BadRequest, message, new Dictionary<string, object?> { ["field"] = field });
}
