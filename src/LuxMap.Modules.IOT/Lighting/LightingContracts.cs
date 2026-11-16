using System.ComponentModel.DataAnnotations;
using LuxMap.Modules.IOT.Entities;
using LuxMap.Shared.Contracts.Enums;

namespace LuxMap.Modules.IOT.Lighting;

/// <summary>A feeder of the target that is left out, and why (D-8).</summary>
public sealed record LightingExclusion
{
    public required string FeederId { get; init; }

    public string? NodeId { get; init; }

    public int? RelayNo { get; init; }

    public required LightingExclusionReason Reason { get; init; }
}

/// <summary>Internal command creation: exactly one of <c>feeder_id</c> / <c>segment_id</c>.</summary>
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

/// <summary>The request and its commands, also returned when replaying the same <c>client_op_id</c>.</summary>
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

    /// <summary>Unassigned poles reached by the switched feeders; null when read from a stored request (not recorded).</summary>
    public required int? UnassignedSegmentPoleCount { get; init; }
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
