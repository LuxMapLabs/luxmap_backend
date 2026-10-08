using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Text.Json;
using LuxMap.Modules.Telemetry.Entities;
using LuxMap.Shared.Contracts.Enums;
using LuxMap.Shared.Contracts.Errors;
using LuxMap.Shared.Http;

namespace LuxMap.Modules.Telemetry.Registry;

/// <summary>
/// One IoT device in the inventory (LIGHT-CTRL 2a, SELF-SIGNED). The device's relays are listed with it — a device carries a
/// handful, and the inventory screen shows them per row.
/// </summary>
/// <remarks>
/// 🔴 <b>Never the secret, never its hash</b> — only whether one is issued and when. <c>node_status</c> stays on
/// <c>GET /map/iot-nodes</c> (one answer per question, Contract 5.3.1); this row carries the raw <c>last_report_at</c>.
/// </remarks>
public sealed record IotNodeItem
{
    public required string NodeId { get; init; }

    public required string CabinetId { get; init; }

    public required string CommuneId { get; init; }

    public required NodeRole NodeRole { get; init; }

    /// <summary><c>calibration_rig</c> (testbed hardware) or <c>simulated</c> — never <c>field</c> (D-R10).</summary>
    public required DataSource DataSource { get; init; }

    /// <summary>Only a device with <c>true</c> accepts ON / OFF / AUTO commands (D-R7).</summary>
    public required bool SupportsRemoteControl { get; init; }

    /// <summary>Whether a secret is issued — the device cannot authenticate without one.</summary>
    public required bool HasCredential { get; init; }

    public DateTime? CredentialSetAt { get; init; }

    public DateTime? LastReportAt { get; init; }

    /// <summary>The relays wired to feeders, in relay order.</summary>
    public required IReadOnlyList<IotNodeRelay> Relays { get; init; }

    public required DateTime UpdatedAt { get; init; }
}

/// <summary>One relay of a device and the feeder it switches (I-12).</summary>
public sealed record IotNodeRelay
{
    public required int RelayNo { get; init; }

    public required string FeederId { get; init; }

    /// <summary>What the DEVICE last reported for this relay (I-6) — never what was last requested.</summary>
    public FeederControlMode? ControlMode { get; init; }

    public DateTime? ModeReportedAt { get; init; }
}

/// <summary><c>POST /assets/iot-nodes</c>. The commune is copied from the cabinet, never sent.</summary>
public sealed record CreateIotNodeRequest
{
    /// <summary>The cabinet the device is mounted in (CAB-3). At most one device per cabinet.</summary>
    [Required]
    [MaxLength(32)]
    public string? CabinetId { get; init; }

    /// <summary><c>calibration_rig</c> or <c>simulated</c>; anything else is 400 (D-R10).</summary>
    [Required]
    public DataSource? DataSource { get; init; }

    public bool SupportsRemoteControl { get; init; }
}

/// <summary>
/// <c>PUT /assets/iot-nodes/{id}</c> — full replacement of what is editable. The cabinet and the commune are not: moving a device
/// is removing it and registering it again, which also forces its relays to be wired again in the new cabinet.
/// </summary>
public sealed record UpdateIotNodeRequest
{
    [Required]
    public DataSource? DataSource { get; init; }

    public bool SupportsRemoteControl { get; init; }
}

/// <summary><c>PUT /assets/iot-nodes/{id}/relays/{relay_no}</c>: <c>{ "feeder_id": "FDR-001" }</c> wires, <c>null</c> unwires.</summary>
/// <remarks>The key is REQUIRED (a JSON element, like <c>SetPoleFeederRequest</c>): an empty body must not unwire a relay.</remarks>
public sealed record SetRelayRequest
{
    public JsonElement FeederId { get; init; }

    public string? ReadFeederId() => FeederId.ValueKind switch
    {
        JsonValueKind.String when FeederId.GetString() is { Length: > 0 and <= 32 } id => id,
        JsonValueKind.Null => null,
        JsonValueKind.Undefined => throw Invalid("feeder_id is required. Send null to unwire the relay."),
        _ => throw Invalid("feeder_id must be a feeder id, or null to unwire the relay."),
    };

    private static LuxMapException Invalid(string message)
        => new(ErrorCodes.ValidationFailed, HttpStatusCode.BadRequest, message,
            new Dictionary<string, object?> { ["field"] = "feeder_id" });
}

/// <summary>
/// <c>POST /assets/iot-nodes/{id}/credential</c> — the ONLY time the secret is ever shown. The previous secret stops working at
/// once. Put it in the device's firmware configuration; it cannot be read back.
/// </summary>
public sealed record IotNodeCredential(string NodeId, string Secret, DateTime CredentialSetAt);
