using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using LuxMap.Modules.Assets.Entities;
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
using Npgsql;

namespace LuxMap.Modules.Survey.Ingest;

/// <summary>SELF-SIGNED P2a ingest only. No processing or publication occurs on submission.</summary>
public sealed class SurveyIngestService(LuxMapDbContext db, ICurrentActorAccessor actor, IAuditTrail audit, IObjectStore store)
{
    public const long ClipLimit = 300L * 1024 * 1024;
    public const int RawLimit = 10 * 1024 * 1024;
    private string ActorId => actor.UserId ?? throw Error("UNAUTHENTICATED", HttpStatusCode.Unauthorized);

    // All reads first intersect sweep scope, the parent's commune/assignee filter and all target routes.
    private IQueryable<SurveySweep> Visible() => db.Set<SurveySweep>().Where(s => db.Set<WorkOrder>().Any(w =>
        w.WorkOrderId == s.WorkOrderId && !db.Set<WorkOrderSegment>().Any(link => link.WorkOrderId == w.WorkOrderId
            && !db.Set<RoadSegment>().Any(road => road.SegmentId == link.SegmentId))));

    private async Task<SurveySweep> Find(string id, CancellationToken ct)
        => await Visible().SingleOrDefaultAsync(x => x.SweepId == id, ct) ?? throw Error("SWEEP_NOT_FOUND", HttpStatusCode.NotFound);

    private async Task<WorkOrder> LockOrder(string id, bool write, CancellationToken ct)
    {
        // Lock first, then read through LINQ — the FaultLocks pattern. A FromSql "SELECT *" cannot be the
        // source: it omits the system column xmin that the row version maps to (42703 at runtime), and the
        // read below still goes through the commune and assignee query filters.
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM work_order WHERE work_order_id = {id} FOR UPDATE", ct);
        var wo = await db.Set<WorkOrder>().SingleOrDefaultAsync(x => x.WorkOrderId == id, ct)
            ?? throw Error("WORK_ORDER_NOT_FOUND", HttpStatusCode.NotFound);
        if (write && wo.AssignedTo != ActorId) throw Error("WORK_ORDER_NOT_FOUND", HttpStatusCode.NotFound);
        var ids = await db.Set<WorkOrderSegment>().Where(x => x.WorkOrderId == id).Select(x => x.SegmentId).ToArrayAsync(ct);
        if (wo.TaskKind != TaskKind.Survey || ids.Length == 0
            || await db.Set<RoadSegment>().CountAsync(x => ids.Contains(x.SegmentId), ct) != ids.Length)
            throw Error("WORK_ORDER_NOT_FOUND", HttpStatusCode.NotFound);
        return wo;
    }

    private async Task<SurveySweep> LockSweep(string id, CancellationToken ct)
    {
        var initial = await Find(id, ct);
        var wo = await LockOrder(initial.WorkOrderId, true, ct);
        if (initial.Status == SweepStatus.Uploading && wo.WoStatus != WorkOrderStatus.InProgress)
            throw Error("INVALID_STATE_TRANSITION", HttpStatusCode.Conflict);
        // Reload after the lock: a previous concurrent uploader/submitter may have committed meanwhile.
        db.Entry(initial).State = EntityState.Detached;
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM survey_sweep WHERE sweep_id = {id} FOR UPDATE", ct);
        return await db.Set<SurveySweep>().SingleAsync(x => x.SweepId == id, ct);
    }

    public async Task<(bool Created, SweepResponse Response)> Create(CreateSweepRequest request, CancellationToken ct)
    {
        var elapsed = SurveyRawParser.ParseDigits(request.ElapsedAnchorNs, "elapsed_anchor_ns");
        var started = SurveyRawParser.ParseDigits(request.StartedElapsedNs, "started_elapsed_ns");
        if (request.ClientOpId == Guid.Empty || request.BootSessionId == Guid.Empty || request.UtcAnchor == default
            || request.UtcAnchor.Kind != DateTimeKind.Utc || !double.IsFinite(request.UtcUncertaintyMs)
            || request.UtcUncertaintyMs < 0 || request.DataSource is null)
            throw SurveyRawParser.Invalid(0, "clock / client_op_id / data_source");
        // Normalize typed values, so JSON field order and whitespace do not change idempotency.
        var hash = Hash(JsonSerializer.SerializeToUtf8Bytes(request with { ElapsedAnchorNs = elapsed.ToString(System.Globalization.CultureInfo.InvariantCulture), StartedElapsedNs = started.ToString(System.Globalization.CultureInfo.InvariantCulture), UtcAnchor = new DateTime(request.UtcAnchor.Ticks / 10 * 10, DateTimeKind.Utc) }, LuxMapJsonOptions.Default));
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var wo = await LockOrder(request.WorkOrderId, true, ct);
        var existing = await Visible().SingleOrDefaultAsync(x => x.CapturedBy == ActorId && x.ClientOpId == request.ClientOpId, ct);
        if (existing is not null)
        {
            if (existing.CreateRequestHash != hash) throw Error("IDEMPOTENCY_CONFLICT", HttpStatusCode.Conflict);
            return (false, await Response(existing, ct));
        }
        if (wo.WoStatus != WorkOrderStatus.InProgress) throw Error("INVALID_STATE_TRANSITION", HttpStatusCode.Conflict);
        var now = UtcMicrosecondClock.UtcNow();
        // SQL expression comes only from the immutable server-side ID specification.
        var idSql = $"SELECT {PrefixedIds.SurveySweep.DefaultValueSql} AS \"Value\"";
        var id = await db.Database.SqlQueryRaw<string>(idSql).SingleAsync(ct);
        var sweep = new SurveySweep { SweepId = id, WorkOrderId = wo.WorkOrderId, CommuneId = wo.CommuneId,
            CapturedBy = ActorId, ClientOpId = request.ClientOpId, BootSessionId = request.BootSessionId,
            ElapsedAnchorNs = elapsed, StartedElapsedNs = started, UtcAnchor = new DateTime(request.UtcAnchor.Ticks / 10 * 10, DateTimeKind.Utc),
            UtcUncertaintyMs = request.UtcUncertaintyMs, DataSource = request.DataSource.Value,
            CreateRequestHash = hash, CreatedAt = now, UpdatedAt = now };
        db.Add(sweep);
        Record(sweep, AuditAction.Created, null, now);
        try { await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            await transaction.RollbackAsync(ct);
            db.ChangeTracker.Clear();
            existing = await Visible().SingleOrDefaultAsync(x => x.CapturedBy == ActorId && x.ClientOpId == request.ClientOpId, ct);
            if (existing is null || existing.CreateRequestHash != hash) throw Error("IDEMPOTENCY_CONFLICT", HttpStatusCode.Conflict);
            return (false, await Response(existing, ct));
        }
        return (true, await Response(sweep, ct));
    }

    public async Task<SurveyClipResponse> Clip(string id, int clipNo, Stream body, long? length, string sha, CancellationToken ct)
    {
        ValidateHash(sha);
        if (clipNo < 0 || length is null or <= 0) throw SurveyRawParser.Invalid(0, "clip_no / content_length");
        if (length > ClipLimit) throw Error("UPLOAD_TOO_LARGE", HttpStatusCode.RequestEntityTooLarge);
        // Check under the locks, then RELEASE them before streaming: a clip takes minutes over mobile data,
        // and holding the work-order row for that long would block every Manager action on the order.
        var existing = await ExistingClip(id, clipNo, sha, length.Value, ct);
        if (existing is not null) return existing;
        // Object first, row after (BE-11 rule 3). The key carries the hash, so a racing retry of the same
        // clip overwrites identical bytes, and a losing upload of different bytes leaves a reclaimable orphan.
        var key = $"sweeps/{id}/clips/{clipNo}/{sha}.mp4";
        var stored = await store.StoreStreamAsync(StorageBucket.Video, key, body, new(length.Value, sha, ClipLimit, "video/mp4", true), ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        existing = await ExistingClip(id, clipNo, sha, length.Value, ct);
        if (existing is not null) return existing;
        var clip = new SurveyVideoClip { SweepId = id, ClipNo = clipNo, ObjectKey = stored.Key, Sha256 = stored.Sha256,
            ByteCount = stored.ByteCount, ContentType = "video/mp4", StoredAt = UtcMicrosecondClock.UtcNow() };
        db.Add(clip);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return new(clip.ClipNo, clip.Sha256, clip.ByteCount, clip.ContentType);
    }

    // Returns the stored clip for an idempotent retry, or null when a new upload may proceed. Without an
    // ambient transaction it opens (and releases) its own, so the row locks never outlive the check.
    private async Task<SurveyClipResponse?> ExistingClip(string id, int clipNo, string sha, long length, CancellationToken ct)
    {
        await using var own = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        var sweep = await LockSweep(id, ct);
        var old = await db.Set<SurveyVideoClip>().AsNoTracking().SingleOrDefaultAsync(x => x.SweepId == id && x.ClipNo == clipNo, ct);
        if (old is not null)
        {
            if (old.Sha256 != sha || old.ByteCount != length) throw Error("IDEMPOTENCY_CONFLICT", HttpStatusCode.Conflict);
            return new(old.ClipNo, old.Sha256, old.ByteCount, old.ContentType);
        }
        Uploading(sweep);
        return null;
    }

    public async Task<SurveyRawResponse> Raw(string id, SurveyRawKind kind, Stream body, CancellationToken ct)
    {
        // Raw files are small and capped independently. Keep exact bytes (including BOM/newlines).
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int n;
        while ((n = await body.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + n > RawLimit) throw Error("UPLOAD_TOO_LARGE", HttpStatusCode.RequestEntityTooLarge);
            buffer.Write(chunk, 0, n);
        }
        var bytes = buffer.ToArray();
        var sha = Hash(bytes);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var sweep = await LockSweep(id, ct);
        var old = await db.Set<SurveyRawFile>().SingleOrDefaultAsync(x => x.SweepId == id && x.Kind == kind, ct);
        if (old is not null)
        {
            if (old.Sha256 != sha) throw Error("IDEMPOTENCY_CONFLICT", HttpStatusCode.Conflict);
            return new(old.Kind, old.Sha256, old.ByteCount, old.SchemaVersion);
        }
        Uploading(sweep);
        var parsed = SurveyRawParser.Parse(bytes, kind, sweep);
        buffer.Position = 0;
        var key = $"sweeps/{id}/raw/{JsonNamingPolicy.SnakeCaseLower.ConvertName(kind.ToString())}/{sha}";
        var stored = await store.StoreStreamAsync(StorageBucket.Video, key, buffer,
            new(bytes.LongLength, sha, RawLimit, kind == SurveyRawKind.CaptureConfig ? "application/json" : "application/x-ndjson"), ct);
        db.AddRange(parsed.Gps);
        db.AddRange(parsed.Lux);
        db.Add(new SurveyRawFile { SweepId = id, Kind = kind, Sha256 = sha, ObjectKey = stored.Key, ByteCount = stored.ByteCount, SchemaVersion = 1 });
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return new(kind, sha, stored.ByteCount, 1);
    }

    public async Task<(bool Queued, SweepResponse Response)> Submit(string id, SubmitSweepRequest request, CancellationToken ct)
    {
        if (request.ClientOpId == Guid.Empty || request.Manifest?.Clips is null || request.Manifest.Clips.Any(x => x is null))
            throw SurveyRawParser.Invalid(0, "manifest / client_op_id");
        var ended = SurveyRawParser.ParseDigits(request.EndedElapsedNs, "ended_elapsed_ns");
        var manifest = request.Manifest;
        foreach (var hash in manifest.Clips.Select(x => x.Sha256).Concat([manifest.GpsHash, manifest.LuxHash, manifest.ConfigHash])) ValidateHash(hash);
        if (manifest.Clips.Any(x => x.ClipNo < 0) || manifest.Clips.Select(x => x.ClipNo).Distinct().Count() != manifest.Clips.Length)
            throw SurveyRawParser.Invalid(0, "manifest");
        var requestHash = Hash(JsonSerializer.SerializeToUtf8Bytes(request with {
            EndedElapsedNs = ended.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Manifest = manifest with { Clips = manifest.Clips.OrderBy(x => x.ClipNo).ToArray() } }, LuxMapJsonOptions.Default));
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var sweep = await LockSweep(id, ct);
        if (ended < sweep.StartedElapsedNs) throw SurveyRawParser.Invalid(0, "ended_elapsed_ns");
        if (sweep.Status != SweepStatus.Uploading)
        {
            if (sweep.SubmissionRequestHash != requestHash) throw Error("IDEMPOTENCY_CONFLICT", HttpStatusCode.Conflict);
            return (false, await Response(sweep, ct));
        }
        var clips = await db.Set<SurveyVideoClip>().Where(x => x.SweepId == id).ToArrayAsync(ct);
        var raw = await db.Set<SurveyRawFile>().Where(x => x.SweepId == id).ToArrayAsync(ct);
        var missing = new List<string>();
        if (clips.Length == 0) missing.Add("clips");
        foreach (var kind in Enum.GetValues<SurveyRawKind>()) if (!raw.Any(x => x.Kind == kind)) missing.Add(JsonNamingPolicy.SnakeCaseLower.ConvertName(kind.ToString()));
        if (missing.Count > 0) throw new LuxMapException("UPLOAD_INCOMPLETE", HttpStatusCode.Conflict,
            "Upload is incomplete.", new Dictionary<string, object?> { ["missing"] = missing });
        var rawHashes = new Dictionary<SurveyRawKind, string> { [SurveyRawKind.GpsTrack] = manifest.GpsHash,
            [SurveyRawKind.LuxLog] = manifest.LuxHash, [SurveyRawKind.CaptureConfig] = manifest.ConfigHash };
        if (clips.Length != manifest.Clips.Length || clips.Any(x => !manifest.Clips.Any(m => m.ClipNo == x.ClipNo && m.Sha256 == x.Sha256))
            || raw.Any(x => rawHashes[x.Kind] != x.Sha256)) throw Error("MANIFEST_MISMATCH", HttpStatusCode.Conflict);
        var before = new { sweep.Status, sweep.ProcessingStatus };
        var now = UtcMicrosecondClock.UtcNow();
        sweep.Status = SweepStatus.Queued;
        sweep.ProcessingStatus = SweepProcessingStatus.Queued;
        sweep.SubmittedAt = now;
        sweep.EndedElapsedNs = ended;
        sweep.SubmissionRequestHash = requestHash;
        sweep.UpdatedAt = now;
        Record(sweep, AuditAction.Submitted, before, now);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return (true, await Response(sweep, ct));
    }

    public async Task<SweepResponse> Detail(string id, CancellationToken ct) => await Response(await Find(id, ct), ct);

    public async Task<PagedResult<SweepResponse>> List(string? workOrder, string? segment, SweepProcessingStatus? processing,
        DataSource? source, PageRequest page, CancellationToken ct)
    {
        var query = Visible().AsNoTracking();
        if (workOrder is not null) query = query.Where(x => x.WorkOrderId == workOrder);
        if (segment is not null) query = query.Where(x => db.Set<WorkOrderSegment>().Any(w => w.WorkOrderId == x.WorkOrderId && w.SegmentId == segment));
        if (processing is not null) query = query.Where(x => x.ProcessingStatus == processing);
        if (source is not null) query = query.Where(x => x.DataSource == source);
        var count = await query.CountAsync(ct);
        var sweeps = await query.OrderBy(x => x.CreatedAt).ThenBy(x => x.SweepId.Length).ThenBy(x => x.SweepId)
            .Skip(page.Skip).Take(page.PageSize).ToArrayAsync(ct);
        return PagedResult<SweepResponse>.From(page, count, await Responses(sweeps, ct));
    }

    private async Task<SweepResponse> Response(SurveySweep s, CancellationToken ct) => (await Responses([s], ct))[0];

    // Three queries per PAGE, not per sweep: a 200-row page would otherwise issue 600.
    private async Task<SweepResponse[]> Responses(IReadOnlyList<SurveySweep> sweeps, CancellationToken ct)
    {
        var orders = sweeps.Select(x => x.WorkOrderId).Distinct().ToArray();
        var ids = sweeps.Select(x => x.SweepId).ToArray();
        var segments = (await db.Set<WorkOrderSegment>().Where(x => orders.Contains(x.WorkOrderId)).OrderBy(x => x.Position)
            .Select(x => new { x.WorkOrderId, x.SegmentId }).ToArrayAsync(ct)).ToLookup(x => x.WorkOrderId, x => x.SegmentId);
        var clips = (await db.Set<SurveyVideoClip>().Where(x => ids.Contains(x.SweepId)).OrderBy(x => x.ClipNo).ToArrayAsync(ct))
            .ToLookup(x => x.SweepId, x => new SurveyClipResponse(x.ClipNo, x.Sha256, x.ByteCount, x.ContentType));
        var raw = (await db.Set<SurveyRawFile>().Where(x => ids.Contains(x.SweepId)).OrderBy(x => x.Kind).ToArrayAsync(ct))
            .ToLookup(x => x.SweepId, x => new SurveyRawResponse(x.Kind, x.Sha256, x.ByteCount, x.SchemaVersion));
        return sweeps.Select(s => Build(s, segments[s.WorkOrderId].ToArray(), clips[s.SweepId].ToArray(), raw[s.SweepId].ToArray())).ToArray();
    }

    private static SweepResponse Build(SurveySweep s, string[] segments, SurveyClipResponse[] clips, SurveyRawResponse[] raw)
    {
        DateTime? At(long? elapsed)
        {
            if (elapsed is null) return null;
            var ticks = (elapsed.Value - s.ElapsedAnchorNs) / 100;
            if (ticks > DateTime.MaxValue.Ticks - s.UtcAnchor.Ticks || ticks < -s.UtcAnchor.Ticks) return null;
            return new DateTime((s.UtcAnchor.Ticks + ticks) / 10 * 10, DateTimeKind.Utc);
        }
        return new(s.SweepId, s.WorkOrderId, At(s.StartedElapsedNs), At(s.EndedElapsedNs), segments,
            0, null, s.ProcessingStatus, s.Status, s.DataSource, s.SubmittedAt, clips, raw);
    }

    private void Record(SurveySweep s, AuditAction action, object? before, DateTime now)
        => audit.Record(new(now, AuditActorKind.User, ActorId, actor.Role, s.CommuneId, AuditEntityType.SurveySweep,
            s.SweepId, action, before, new { s.SweepId, s.WorkOrderId, s.CapturedBy, s.ClientOpId, s.Status, s.ProcessingStatus, s.DataSource, s.SubmittedAt, s.CreateRequestHash, s.SubmissionRequestHash, s.ElapsedAnchorNs, s.StartedElapsedNs, s.EndedElapsedNs, s.UtcAnchor, s.UtcUncertaintyMs }));
    private static void Uploading(SurveySweep s)
    {
        if (s.Status != SweepStatus.Uploading) throw Error("INVALID_STATE_TRANSITION", HttpStatusCode.Conflict);
    }
    private static void ValidateHash(string? hash)
    {
        if (hash is not { Length: 64 } || hash.Any(c => !(c is >= '0' and <= '9' or >= 'a' and <= 'f')))
            throw SurveyRawParser.Invalid(0, "sha256");
    }
    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    private static LuxMapException Error(string code, HttpStatusCode status) => new(code, status, code.Replace('_', ' '));
}
