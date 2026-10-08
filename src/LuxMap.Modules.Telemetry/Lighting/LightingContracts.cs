using System.ComponentModel.DataAnnotations;
using LuxMap.Modules.Telemetry.Entities;
using LuxMap.Shared.Contracts.Enums;

namespace LuxMap.Modules.Telemetry.Lighting;

/// <summary>One relay that a request switches (or would switch, in a preview).</summary>
public sealed record LightingTarget
{
    public required string NodeId { get; init; }

    public required int RelayNo { get; init; }

    public required string FeederId { get; init; }

    public required string CabinetId { get; init; }
}

/// <summary>A feeder of the target that is left out, and why (D-8).</summary>
public sealed record LightingExclusion
{
    public required string FeederId { get; init; }

    public string? NodeId { get; init; }

    public int? RelayNo { get; init; }

    public required LightingExclusionReason Reason { get; init; }
}

/// <summary>
/// <c>GET /lighting/preview</c> — what a press WOULD do, without writing anything. The server checks it all again at the press.
/// </summary>
public sealed record LightingPreview
{
    public required LightingTargetKind TargetKind { get; init; }

    public required string TargetId { get; init; }

    public required IReadOnlyList<LightingTarget> Targets { get; init; }

    public required IReadOnlyList<LightingExclusion> Excluded { get; init; }

    /// <summary>
    /// Every segment the switched feeders light — a feeder can run along several, so switching one segment's feeder also
    /// switches poles of other segments (I-14). The UI must say so before the press.
    /// </summary>
    public required IReadOnlyList<string> AffectedSegmentIds { get; init; }

    /// <summary>Poles of the target that no command reaches: no feeder, or a feeder that is excluded.</summary>
    public required int UncontrollablePoleCount { get; init; }
}

/// <summary><c>POST /lighting/commands</c>: exactly one of <c>feeder_id</c> / <c>segment_id</c>.</summary>
public sealed record CreateLightingRequest
{
    [MaxLength(32)]
    public string? FeederId { get; init; }

    [MaxLength(32)]
    public string? SegmentId { get; init; }

    [Required]
    public FeederControlMode? Mode { get; init; }

    /// <summary>Idempotency key: send the same press again with the same key and content to get the same request back.</summary>
    [Required]
    public Guid? ClientOpId { get; init; }
}

/// <summary>The press and the commands it created (202, or 200 for a replay of the same <c>client_op_id</c>).</summary>
public sealed record LightingRequestResult
{
    public required Guid RequestId { get; init; }

    public required Guid ClientOpId { get; init; }

    public required LightingTargetKind TargetKind { get; init; }

    public required string TargetId { get; init; }

    public required FeederControlMode Mode { get; init; }

    public required string RequestedBy { get; init; }

    public required DateTime RequestedAt { get; init; }

    public required IReadOnlyList<LightingCommandItem> Commands { get; init; }

    public required IReadOnlyList<LightingExclusion> Excluded { get; init; }

    public required IReadOnlyList<string> AffectedSegmentIds { get; init; }

    public required int UncontrollablePoleCount { get; init; }
}

/// <summary>One command and where it stands.</summary>
/// <remarks>
/// <c>status</c> is computed at read time: an open command past <c>expires_at</c> reads <c>expired</c> before any write has
/// stored it (3.4). <c>reported_mode</c> is what the DEVICE said it set — never what was requested.
/// </remarks>
public sealed record LightingCommandItem
{
    public required string CommandId { get; init; }

    public required Guid RequestId { get; init; }

    public required long Seq { get; init; }

    public required string NodeId { get; init; }

    public required int RelayNo { get; init; }

    public required string FeederId { get; init; }

    public required string CabinetId { get; init; }

    public required DataSource DataSource { get; init; }

    public required FeederControlMode RequestedMode { get; init; }

    public required LightingCommandStatus Status { get; init; }

    public required DateTime CreatedAt { get; init; }

    public required DateTime ExpiresAt { get; init; }

    public DateTime? DeliveredAt { get; init; }

    public DateTime? CompletedAt { get; init; }

    public FeederControlMode? ReportedMode { get; init; }

    public string? Error { get; init; }
}

/// <summary>
/// <c>GET /device/commands</c> — the device's open commands, newest per relay. <c>server_time</c> lets a device without a
/// synchronised clock judge <c>expires_at</c>.
/// </summary>
public sealed record DeviceCommandBatch(DateTime ServerTime, IReadOnlyList<DeviceCommand> Commands);

/// <summary>One command as the firmware sees it (the firmware contract, Phase 1 §6).</summary>
public sealed record DeviceCommand(string CommandId, long Seq, int RelayNo, FeederControlMode Mode, DateTime ExpiresAt);

/// <summary>
/// <c>POST /device/commands/{command_id}/ack</c>, sent AFTER executing. <c>applied</c> needs <c>reported_mode</c> equal to the
/// requested mode; <c>failed</c> needs <c>error</c>.
/// </summary>
public sealed record DeviceAckRequest
{
    /// <summary>Must be the command's own <c>seq</c>.</summary>
    [Required]
    public long? Seq { get; init; }

    [Required]
    public LightingAckResult? Result { get; init; }

    public FeederControlMode? ReportedMode { get; init; }

    [MaxLength(500)]
    public string? Error { get; init; }
}

public sealed record DeviceAckResult(string CommandId, LightingCommandStatus Status);
