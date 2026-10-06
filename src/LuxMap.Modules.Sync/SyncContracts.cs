using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using LuxMap.Modules.Faults;
using LuxMap.Modules.Map.Features;
using LuxMap.Modules.WorkOrders;
using LuxMap.Shared.Contracts.GeoJson;

namespace LuxMap.Modules.Sync;

/// <summary>
/// <c>GET /sync/bundle</c> — everything a field engineer needs to work the given roads offline (BE-43 D-1, D-3).
/// </summary>
/// <remarks>
/// A full snapshot every time, never a delta: the client REPLACES its cache for these segments. A pole deleted,
/// a work order handed to someone else, a commune taken away — each simply stops being in the next bundle.
/// Read in one REPEATABLE READ transaction so the five parts describe the same moment.
/// </remarks>
public sealed record SyncBundle
{
    /// <summary>When the snapshot was taken — what a phone shows as "data as of".</summary>
    public required DateTime GeneratedAt { get; init; }

    /// <summary>The segments actually packed: the ones asked for, or those of the caller's open work orders.</summary>
    public required IReadOnlyList<string> SegmentIds { get; init; }

    /// <summary>Same properties as <c>GET /map/segments</c>, in the order of <see cref="SegmentIds"/>.</summary>
    public required FeatureCollection<SegmentProperties> Segments { get; init; }

    /// <summary>Same properties as <c>GET /map/poles</c> plus the pole's <c>note</c>, in id order.</summary>
    public required FeatureCollection<SyncPoleProperties> Poles { get; init; }

    /// <summary>Open faults on these segments or their poles, each an item of <c>GET /faults</c>, default order.</summary>
    public required IReadOnlyList<FaultItem> OpenFaults { get; init; }

    /// <summary>The caller's open work orders (<c>assigned</c>, <c>in_progress</c>) touching these segments, as details.</summary>
    public required IReadOnlyList<WorkOrderDetail> WorkOrders { get; init; }
}

/// <summary>A pole of the bundle: the map layer's fifteen properties plus <c>note</c>, still flat (BE-43 D-3).</summary>
public sealed record SyncPoleProperties : PoleProperties
{
    [SetsRequiredMembers]
    public SyncPoleProperties(PoleProperties map, string? note) : base(map) => Note = note;

    /// <summary>The free-text note on the pole (POLE-NOTE); <c>null</c> when none. What a <c>pole_note</c> push sends as <c>base_note</c>.</summary>
    public string? Note { get; init; }
}

/// <summary><c>POST /sync/push</c> — the offline queue, applied in order (BE-43 D-7).</summary>
public sealed record SyncPushRequest
{
    public IReadOnlyList<SyncOperationRequest>? Operations { get; init; }
}

/// <summary>One queued operation.</summary>
public sealed record SyncOperationRequest
{
    /// <summary>A UUID the phone generated for this operation; the same key on a resend.</summary>
    public Guid? ClientOpId { get; init; }

    /// <summary>
    /// <c>fault_report</c>, <c>lux_reading</c>, <c>pole_note</c>, <c>work_order_start</c> or <c>work_order_complete</c>.
    /// Text, not an enum, so one unknown type rejects that operation rather than the whole batch.
    /// </summary>
    public string? OpType { get; init; }

    /// <summary>The body the operation's own endpoint takes — see the Contract, section 5.8.</summary>
    public JsonElement Payload { get; init; }
}

/// <summary>
/// The outcome of every operation, in three lists. HTTP is 200 whatever they hold: the applied ones were
/// really written. A resend of the same batch is safe.
/// </summary>
public sealed record SyncPushResult
{
    public required IReadOnlyList<SyncApplied> Applied { get; init; }

    public required IReadOnlyList<SyncConflict> Conflicts { get; init; }

    public required IReadOnlyList<SyncRejected> Rejected { get; init; }
}

/// <param name="Id">The fault, lux reading, pole or work order the operation produced or acted on.</param>
/// <param name="Replayed"><c>true</c> when this <c>client_op_id</c> had already been applied — nothing new was written.</param>
public sealed record SyncApplied(Guid ClientOpId, string OpType, string Id, bool Replayed);

/// <summary>
/// The server's state disagrees with the operation; nothing was written and the server wins.
/// </summary>
/// <param name="Reason">The error code the endpoint would have answered, e.g. <c>INVALID_STATE_TRANSITION</c>,
/// <c>NOTE_CHANGED</c>, <c>WORK_ORDER_NOT_FOUND</c>.</param>
/// <param name="ServerState">The thing as it stands now — work order detail or pole row — or <c>null</c> when the
/// caller may no longer see it.</param>
public sealed record SyncConflict(Guid ClientOpId, string OpType, string Reason, string Message, object? ServerState);

/// <summary>The operation itself is wrong (validation, permission, an earlier failure on the same thing): a person must look at it.</summary>
public sealed record SyncRejected(Guid ClientOpId, string? OpType, SyncError Error);

public sealed record SyncError(string Code, string Message, IReadOnlyDictionary<string, object?>? Details);
