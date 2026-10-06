namespace LuxMap.Modules.Sync.Entities;

/// <summary>The kinds of operation an offline queue may push (BE-43 D-4). Stored and sent as snake_case text.</summary>
public enum SyncOpType
{
    FaultReport,
    LuxReading,
    PoleNote,
    WorkOrderStart,
    WorkOrderComplete,
}

/// <summary>
/// One queued operation that was APPLIED, remembered by its <c>client_op_id</c> so a resend returns the first
/// result instead of failing (BE-43 D-5).
/// </summary>
/// <remarks>
/// <para>
/// Only the operations with no idempotency key of their own land here — starting and completing a work order,
/// and a pole note. A second <c>start</c> would otherwise be a 409 and look like a conflict although the
/// engineer's own step went through. <c>fault_report</c> and <c>lux_reading</c> keep using the
/// <c>client_op_id</c> column on <c>fault</c> / <c>lux_reading</c>, so a report sent once by the endpoint and
/// again from the queue is still one fault.
/// </para>
/// <para>
/// Staged on the caller's context BEFORE the service runs, so it is written by the service's own
/// <c>SaveChanges</c>: an operation that fails or loses a race leaves no row. Conflicts and rejections are never
/// stored — a resend re-evaluates against the server as it is then.
/// </para>
/// <para>
/// Belongs to a person, not a commune (like <c>refresh_token</c>), so no <c>commune_id</c> and a CASCADE from
/// <c>app_user</c>: it is a technical replay memory, while the business event itself is in the audit trail.
/// </para>
/// </remarks>
public sealed class SyncOperation
{
    public required string UserId { get; init; }

    public Guid ClientOpId { get; init; }

    public SyncOpType OpType { get; init; }

    /// <summary>The work order or pole the operation acted on — what a replay answers as <c>id</c>.</summary>
    public required string EntityId { get; init; }

    public DateTime AppliedAt { get; init; }
}
