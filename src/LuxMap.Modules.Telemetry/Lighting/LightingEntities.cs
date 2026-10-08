using LuxMap.Modules.Telemetry.Entities;
using LuxMap.Persistence.Audit;
using LuxMap.Shared.Authorization;
using LuxMap.Shared.Contracts.Enums;

namespace LuxMap.Modules.Telemetry.Lighting;

/// <summary>
/// One press of ON / OFF / AUTO by a Manager (LIGHT-CTRL 2b, LC-4…LC-8): a feeder or a segment, split into one
/// <see cref="LightingCommand"/> per relay that can be switched. Testbed devices only (D-R7).
/// </summary>
/// <remarks>
/// Everything that decided the outcome is a SNAPSHOT taken at the press — which segments the switched feeders also light
/// (I-14), what was left out and why (D-8), how many poles could not be switched — so the history stays true after the
/// network is rewired. <c>target_id</c> carries no foreign key for the same reason.
/// </remarks>
public sealed class LightingRequest : ICommuneScoped, IAudited
{
    public Guid RequestId { get; set; }

    /// <summary>Idempotency key from the client; the same key with the same content replays, other content is 409.</summary>
    public Guid ClientOpId { get; set; }

    public required string RequestHash { get; set; }

    /// <summary>The commune of the target (feeder or segment). Commands carry their own.</summary>
    public required string CommuneId { get; set; }

    public LightingTargetKind TargetKind { get; set; }

    public required string TargetId { get; set; }

    public FeederControlMode RequestedMode { get; set; }

    public required string RequestedBy { get; set; }

    public DateTime RequestedAt { get; set; }

    public required string[] AffectedSegmentIds { get; set; }

    /// <summary>The <c>excluded[]</c> answered at the press, as JSON.</summary>
    public required string Excluded { get; set; }

    public int UncontrollablePoleCount { get; set; }
}

/// <summary>
/// One relay to switch (LIGHT-CTRL 2b). The device fetches it, executes it, then acknowledges it (3.4).
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>No foreign key to <c>feeder_control</c></b> (Codex P1 on Phase 1): that table is the CURRENT wiring, and a finished
/// command must never block unwiring a relay. <c>relay_no</c> and <c>cabinet_id</c> are snapshots; the wiring is checked
/// again at delivery and at acknowledgement.
/// </para>
/// <para>
/// <c>seq</c> is the execution order (D-10): the device drops any command whose <c>seq</c> is not above the last one it
/// executed on that relay, and the server writes a reported mode only when its <c>seq</c> is above
/// <c>feeder_control.mode_seq</c>.
/// </para>
/// </remarks>
public sealed class LightingCommand : ICommuneScoped, IAudited
{
    public string CommandId { get; set; } = null!;

    public Guid RequestId { get; set; }

    public required string NodeId { get; set; }

    public required string FeederId { get; set; }

    public required string CommuneId { get; set; }

    public short RelayNo { get; set; }

    public required string CabinetId { get; set; }

    /// <summary>Copied from the device: <c>calibration_rig</c> or <c>simulated</c>, never <c>field</c>.</summary>
    public DataSource DataSource { get; set; }

    public FeederControlMode RequestedMode { get; set; }

    /// <summary>Server-assigned, strictly increasing (identity column).</summary>
    public long Seq { get; set; }

    public LightingCommandStatus Status { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime ExpiresAt { get; set; }

    public DateTime? DeliveredAt { get; set; }

    /// <summary>When the command CLOSED — applied, failed, expired or superseded.</summary>
    public DateTime? CompletedAt { get; set; }

    /// <summary>What the device said it set, from its acknowledgement.</summary>
    public FeederControlMode? ReportedMode { get; set; }

    public string? Error { get; set; }

    public bool IsOpen => Status is LightingCommandStatus.Pending or LightingCommandStatus.Delivered;
}

public enum LightingTargetKind
{
    Feeder,
    Segment,
}

public enum LightingCommandStatus
{
    Pending,
    Delivered,
    Applied,
    Failed,
    Expired,
    Superseded,
}

/// <summary>Why a feeder of the target is left out of the request (D-8).</summary>
public enum LightingExclusionReason
{
    /// <summary>No relay of any device switches this feeder.</summary>
    NotWired,

    /// <summary>The device is not a remote-controlled testbed device (D-R7).</summary>
    RemoteControlUnsupported,

    /// <summary>The device has no secret, so it cannot fetch a command.</summary>
    NoCredential,
}

/// <summary>The device's verdict in an acknowledgement.</summary>
public enum LightingAckResult
{
    Applied,
    Failed,
}
