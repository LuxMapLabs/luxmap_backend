using System.Globalization;
using System.Net;
using LuxMap.Modules.Faults;
using LuxMap.Modules.Faults.Entities;
using LuxMap.Modules.WorkOrders.Entities;
using LuxMap.Persistence;
using LuxMap.Shared.Authorization;
using LuxMap.Shared.Contracts;
using LuxMap.Shared.Contracts.Enums;
using LuxMap.Shared.Contracts.Paging;
using LuxMap.Shared.Http;
using LuxMap.Shared.Serialization;
using LuxMap.Shared.Storage;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LuxMap.Modules.WorkOrders;

/// <summary>One photo of a work order as the clients see it (BE-24). Bytes only through the API.</summary>
public sealed record EvidenceItem
{
    public required string EvidenceId { get; init; }
    /// <summary>The work order the photo belongs to; null for a photo of a reported fault.</summary>
    public string? WorkOrderId { get; init; }

    /// <summary>The reported fault the photo belongs to (BE-41); null for a work-order photo.</summary>
    public string? FaultId { get; init; }

    public EvidenceKind Kind { get; init; }
    public DateTime CapturedAt { get; init; }
    public double Lat { get; init; }
    public double Lng { get; init; }
    public required string UploadedBy { get; init; }
    public DateTime UploadedAt { get; init; }

    /// <summary>Relative path through the API, never presigned (BE-11 rule 1).</summary>
    public required string ThumbnailUrl { get; init; }

    /// <summary>The original, byte for byte as the phone sent it.</summary>
    public required string OriginalUrl { get; init; }
}

/// <summary>
/// Repair and inspection photos (BE-24). <c>before</c>/<c>after</c> only on a repair, <c>observation</c>
/// only on an inspection (drift EV-1); only the assigned engineer, only while the order is in progress.
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>Every read goes through the parent work order.</b> <c>repair_evidence</c> is commune-scoped, but
/// who may see an order also depends on the ASSIGNEE scope that only <c>work_order</c> carries — an
/// engineer must not open another engineer's photos in the same commune by guessing an id.
/// </para>
/// <para>
/// Object first, row second (BE-11 rule 3): a failure after the write leaves an orphaned object for BE-35
/// to reconcile, never a row whose image 404s. The id is drawn from its sequence BEFORE the write because
/// the object key is derived from it; a rejected image leaves a gap in the numbering, which is accepted.
/// </para>
/// </remarks>
public sealed partial class WorkOrderEvidenceService(LuxMapDbContext db, ICurrentActorAccessor actor, IObjectStore store)
{
    /// <summary>A phone photo is a few MB; this leaves room without letting one request hold the server.</summary>
    public const long MaxUploadBytes = 16 * 1024 * 1024;

    private static LuxMapException Error(string code, HttpStatusCode status, params (string Key, object? Value)[] details)
        => new(code, status, code.Replace('_', ' '), details.ToDictionary(x => x.Key, x => x.Value));

    private string ActorId => actor.UserId ?? throw Error("UNAUTHENTICATED", HttpStatusCode.Unauthorized);

    /// <summary>The order through its own query filter (commune + assignee), or 404 — same as GET /work-orders/{id}.</summary>
    private async Task<WorkOrder> VisibleOrder(string id, CancellationToken ct)
        => await db.Set<WorkOrder>().AsNoTracking().FirstOrDefaultAsync(x => x.WorkOrderId == id, ct)
            ?? throw Error("WORK_ORDER_NOT_FOUND", HttpStatusCode.NotFound);

    public static EvidenceKind[] AllowedKinds(TaskKind kind) => kind switch
    {
        TaskKind.Repair => [EvidenceKind.Before, EvidenceKind.After],
        TaskKind.Inspection => [EvidenceKind.Observation],
        _ => [],
    };

    /// <summary>What a photo is attached to, and the checks that decide whether this caller may attach it now.</summary>
    /// <param name="Own">Visibility and ownership; 404 when not the caller's. Returns the parent's commune.</param>
    /// <param name="Writable">Label and state rules (400 / 409) — checked AFTER a replay, which answers whatever the state.</param>
    /// <param name="Lock">Row lock on the parent, taken after the image write and before the row is inserted.</param>
    private sealed record Target(string? WorkOrderId, string? FaultId, Func<CancellationToken, Task<string>> Own,
        Func<CancellationToken, Task> Writable, FormattableString Lock);

    private sealed record Upload(IFormFile File, EvidenceKind Kind, DateTime CapturedAt, double Lat, double Lng, Guid? Operation);

    /// <summary>A photo on a work order: the assignee, while it is in progress, labelled for the kind of order.</summary>
    public Task<(EvidenceItem Item, bool Created)> UploadAsync(string id, IFormFile? file, string? kind,
        string? capturedAt, string? lat, string? lng, string? clientOpId, CancellationToken ct)
    {
        var upload = Parse(file, kind, capturedAt, lat, lng, clientOpId);
        async Task<string> Own(CancellationToken token)
        {
            var order = await VisibleOrder(id, token);
            if (order.AssignedTo != ActorId) throw Error("WORK_ORDER_NOT_FOUND", HttpStatusCode.NotFound);
            return order.CommuneId;
        }
        async Task Writable(CancellationToken token)
        {
            var order = await VisibleOrder(id, token);
            if (!AllowedKinds(order.TaskKind).Contains(upload.Kind))
                throw Error("EVIDENCE_KIND_NOT_ALLOWED", HttpStatusCode.BadRequest,
                    ("kind", WireEnum.Name(upload.Kind)), ("task_kind", WireEnum.Name(order.TaskKind)));
            if (order.WoStatus != WorkOrderStatus.InProgress)
                throw Error("WORK_ORDER_NOT_IN_PROGRESS", HttpStatusCode.Conflict, ("wo_status", WireEnum.Name(order.WoStatus)));
        }
        return StoreAsync(new Target(id, null, Own, Writable,
            $"SELECT 1 FROM work_order WHERE work_order_id = {id} FOR UPDATE"), upload, ct);
    }

    /// <summary>
    /// A photo on a fault the caller REPORTED (BE-41, drift EV-2), while it is still open. Always an
    /// <c>observation</c>: a report fixes nothing, so it has no before or after.
    /// </summary>
    public Task<(EvidenceItem Item, bool Created)> UploadToFaultAsync(string id, IFormFile? file,
        string? capturedAt, string? lat, string? lng, string? clientOpId, CancellationToken ct)
    {
        var upload = Parse(file, "observation", capturedAt, lat, lng, clientOpId);
        async Task<string> Own(CancellationToken token)
        {
            var fault = await VisibleFault(id, token);
            if (fault.ReportedBy != ActorId) throw Error("FAULT_NOT_FOUND", HttpStatusCode.NotFound);
            return fault.CommuneId;
        }
        async Task Writable(CancellationToken token)
        {
            var fault = await VisibleFault(id, token);
            if (!FaultStatusSets.IsOpen(fault.FaultStatus))
                throw Error("FAULT_NOT_OPEN", HttpStatusCode.Conflict, ("fault_status", WireEnum.Name(fault.FaultStatus)));
        }
        return StoreAsync(new Target(null, id, Own, Writable,
            $"SELECT 1 FROM fault WHERE fault_id = {id} FOR UPDATE"), upload, ct);
    }

    private static Upload Parse(IFormFile? file, string? kind, string? capturedAt, string? lat, string? lng, string? clientOpId)
    {
        // Shape first, so a malformed request costs no database round trip and no storage write.
        if (file is null || file.Length == 0) throw OptionalJson.Invalid("file");
        var parsedKind = WireEnum.ParseCsv<EvidenceKind>(kind, "kind") is [var single] ? single : throw OptionalJson.Invalid("kind");
        // A COMPLETE ISO 8601 date-time: TryParse alone accepts "13:30" and quietly supplies today's date.
        // Z or an offset is converted to UTC; no designator is read as UTC (Contract §0, as for query strings).
        if (capturedAt is null || !CompleteTimestamp().IsMatch(capturedAt)
            || !DateTime.TryParse(capturedAt, CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var captured))
            throw OptionalJson.Invalid("captured_at");
        // timestamptz keeps microseconds; trim here so the response equals what is stored.
        captured = new DateTime(captured.Ticks / 10 * 10, DateTimeKind.Utc);
        Guid? operation = null;
        if (!string.IsNullOrWhiteSpace(clientOpId))
            operation = Guid.TryParse(clientOpId, out var parsed) && parsed != Guid.Empty ? parsed : throw OptionalJson.Invalid("client_op_id");
        return new Upload(file, parsedKind, captured, Coordinate(lat, "lat", 90), Coordinate(lng, "lng", 180), operation);
    }

    private async Task<(EvidenceItem Item, bool Created)> StoreAsync(Target target, Upload upload, CancellationToken ct)
    {
        await target.Own(ct);
        if (upload.Operation is { } op && await Replay(target, op, upload.Kind, ct) is { } replayed) return (replayed, false);
        await target.Writable(ct);

        // A fixed expression from the id registry, no user input: the same draw the column DEFAULT makes.
        var nextId = $"SELECT {PrefixedIds.RepairEvidence.DefaultValueSql} AS \"Value\"";
        var evidenceId = await db.Database.SqlQueryRaw<string>(nextId).SingleAsync(ct);
        StoredImage stored;
        await using (var content = upload.File.OpenReadStream())
            stored = await store.StoreImageAsync(StorageBucket.Evidence, evidenceId, content, ct);

        // 🔴 The write above can take seconds, and no lock was held across it (BE-15 P2a rule): the parent may
        // have been reassigned, cancelled or closed meanwhile. Lock it, read it again through its filter, check
        // again. Inserting a child row does not touch the parent's xmin, so nothing else would notice.
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlAsync(target.Lock, ct);
        var communeId = await target.Own(ct);
        // An overlapping retry with the same key may have committed while this one was writing — and the parent
        // may even have moved on since. The photo exists: answer the replay, not a state error.
        if (upload.Operation is { } overlap && await Replay(target, overlap, upload.Kind, ct) is { } committed) return (committed, false);
        await target.Writable(ct);

        var row = new RepairEvidence
        {
            EvidenceId = evidenceId, WorkOrderId = target.WorkOrderId, FaultId = target.FaultId, CommuneId = communeId,
            Kind = upload.Kind, CapturedAt = upload.CapturedAt, Lat = upload.Lat, Lng = upload.Lng,
            ObjectKey = stored.OriginalKey, ThumbnailKey = stored.ThumbnailKey, ByteCount = stored.OriginalBytes,
            ThumbnailBytes = stored.ThumbnailBytes, UploadedBy = ActorId, UploadedAt = UtcMicrosecondClock.UtcNow(),
            ClientOpId = upload.Operation,
        };
        db.Add(row);
        try
        {
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch (DbUpdateException error) when (upload.Operation is { } retry
            && error.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Two retries raced past the replay check. A failed statement aborts the transaction, so roll back
            // BEFORE asking again. The loser's object stays orphaned (BE-35 reconciles).
            await transaction.RollbackAsync(ct);
            db.Entry(row).State = EntityState.Detached;
            return (await Replay(target, retry, upload.Kind, ct) ?? throw Error("IDEMPOTENCY_CONFLICT", HttpStatusCode.Conflict), false);
        }
        return (Item(row), true);
    }

    /// <summary>The photo this user already sent with this key, if it was for the same parent and label.</summary>
    private async Task<EvidenceItem?> Replay(Target target, Guid operation, EvidenceKind kind, CancellationToken ct)
    {
        var actorId = ActorId;
        var earlier = await db.Set<RepairEvidence>().AsNoTracking()
            .FirstOrDefaultAsync(x => x.UploadedBy == actorId && x.ClientOpId == operation, ct);
        if (earlier is null) return null;
        if (earlier.WorkOrderId != target.WorkOrderId || earlier.FaultId != target.FaultId || earlier.Kind != kind)
            throw Error("IDEMPOTENCY_CONFLICT", HttpStatusCode.Conflict);
        return Item(earlier);
    }

    public async Task<PagedResult<EvidenceItem>> ListAsync(string id, PageRequest page, CancellationToken ct)
    {
        await VisibleOrder(id, ct);
        return await Page(db.Set<RepairEvidence>().AsNoTracking().Where(x => x.WorkOrderId == id), page, ct);
    }

    /// <summary>Photos of a fault, for everyone who may read the fault (commune scope).</summary>
    public async Task<PagedResult<EvidenceItem>> ListForFaultAsync(string id, PageRequest page, CancellationToken ct)
    {
        await VisibleFault(id, ct);
        return await Page(db.Set<RepairEvidence>().AsNoTracking().Where(x => x.FaultId == id), page, ct);
    }

    private static async Task<PagedResult<EvidenceItem>> Page(IQueryable<RepairEvidence> query, PageRequest page, CancellationToken ct)
    {
        var total = await query.CountAsync(ct);
        var rows = await query.OrderBy(x => x.CapturedAt).ThenBy(x => x.UploadedAt)
            .ThenBy(x => x.EvidenceId.Length).ThenBy(x => x.EvidenceId)
            .Skip(page.Skip).Take(page.PageSize).ToListAsync(ct);
        return PagedResult<EvidenceItem>.From(page, total, rows.Select(Item).ToList());
    }

    /// <summary>The image bytes, after the same visibility check as the list — through the parent. 404 otherwise.</summary>
    public async Task<Stream> OpenAsync(string evidenceId, bool thumbnail, CancellationToken ct)
    {
        var row = await db.Set<RepairEvidence>().AsNoTracking().FirstOrDefaultAsync(x => x.EvidenceId == evidenceId, ct);
        var visible = row switch
        {
            null => false,
            { WorkOrderId: { } order } => await db.Set<WorkOrder>().AnyAsync(x => x.WorkOrderId == order, ct),
            { FaultId: { } fault } => await db.Set<Fault>().AnyAsync(x => x.FaultId == fault, ct),
            _ => false,
        };
        if (!visible) throw Error("EVIDENCE_NOT_FOUND", HttpStatusCode.NotFound);
        return await store.OpenAsync(StorageBucket.Evidence, thumbnail ? row!.ThumbnailKey : row!.ObjectKey, ct);
    }

    /// <summary>The fault through its commune filter, or 404.</summary>
    private async Task<Fault> VisibleFault(string id, CancellationToken ct)
        => await db.Set<Fault>().AsNoTracking().FirstOrDefaultAsync(x => x.FaultId == id, ct)
            ?? throw Error("FAULT_NOT_FOUND", HttpStatusCode.NotFound);

    [System.Text.RegularExpressions.GeneratedRegex(@"^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}(\.[0-9]{1,7})?(Z|[+-][0-9]{2}:[0-9]{2})?$")]
    private static partial System.Text.RegularExpressions.Regex CompleteTimestamp();

    private static double Coordinate(string? value, string field, double limit)
        => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
            && double.IsFinite(number) && Math.Abs(number) <= limit ? number : throw OptionalJson.Invalid(field);

    private static EvidenceItem Item(RepairEvidence x) => new()
    {
        EvidenceId = x.EvidenceId, WorkOrderId = x.WorkOrderId, FaultId = x.FaultId, Kind = x.Kind, CapturedAt = x.CapturedAt,
        Lat = x.Lat, Lng = x.Lng, UploadedBy = x.UploadedBy, UploadedAt = x.UploadedAt,
        ThumbnailUrl = $"/api/v1/evidence/{x.EvidenceId}/thumbnail",
        OriginalUrl = $"/api/v1/evidence/{x.EvidenceId}/original",
    };
}
