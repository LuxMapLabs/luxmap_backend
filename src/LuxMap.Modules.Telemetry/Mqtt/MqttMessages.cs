using System.Text.Json.Serialization;
using LuxMap.Modules.Telemetry.Entities;
using LuxMap.Modules.Telemetry.Lighting;

namespace LuxMap.Modules.Telemetry.Mqtt;

/// <summary>
/// Topics of LC-12 under <c>luxmap/v1/nodes/{node_id}</c>. A node id never contains <c>/</c>, <c>+</c> or <c>#</c> — checked
/// wherever one comes from outside, so it can never widen a topic or an ACL rule.
/// </summary>
public static class MqttTopics
{
    public const string Root = "luxmap/v1/nodes";

    /// <summary>Largest payload accepted or sent (LC-12).</summary>
    public const int MaxPayloadBytes = 4096;

    public const int SchemaVersion = 1;

    public static string Command(string nodeId, int relayNo) => $"{Root}/{nodeId}/relays/{relayNo}/command";

    public static string CommandFilter(string nodeId) => $"{Root}/{nodeId}/relays/+/command";

    public static string Of(string nodeId, string kind) => $"{Root}/{nodeId}/{kind}";

    public const string Receipt = "receipt";
    public const string Ack = "ack";
    public const string Reply = "reply";
    public const string Heartbeat = "heartbeat";
    public const string Status = "status";
    public const string Telemetry = "telemetry";

    /// <summary>
    /// A command id as the server issues it (<c>CMD-</c> + 6 to 20 digits). Anything else is dropped before it is looked up or echoed
    /// in a reply — an arbitrary id escaped into JSON could outgrow the broker's packet limit (Codex review).
    /// </summary>
    public static bool IsCommandId(string? id)
        => id is { Length: >= 10 and <= 24 } && id.StartsWith("CMD-", StringComparison.Ordinal) && id.AsSpan(4).IndexOfAnyExceptInRange('0', '9') < 0;

    public static bool IsSafeNodeId(string? nodeId)
        => nodeId is { Length: > 0 and <= 64 } && !nodeId.AsSpan().ContainsAny("/+#\0 ") && !nodeId.StartsWith('$');

    /// <summary><c>luxmap/v1/nodes/{node_id}/{kind}</c> → (node, kind), or null for anything else.</summary>
    public static (string NodeId, string Kind)? Parse(string topic)
    {
        var parts = topic.Split('/');
        return parts is ["luxmap", "v1", "nodes", var node, var kind] && IsSafeNodeId(node) ? (node, kind) : null;
    }
}

/// <summary>server → device: <c>…/relays/{relay_no}/command</c>. A SET, never a toggle.</summary>
public sealed record MqttCommandMessage(int SchemaVersion, string CommandId, long Seq, int RelayNo, FeederControlMode Mode, DateTime ExpiresAt);

/// <summary>device → server: <c>…/receipt</c> — received and checked, not yet executed (M-5).</summary>
public sealed record MqttReceiptMessage(int SchemaVersion, string? CommandId, long? Seq);

/// <summary>device → server: <c>…/ack</c> — after executing (M-7).</summary>
public sealed record MqttAckMessage(
    int SchemaVersion, string? CommandId, long? Seq, LightingAckResult? Result, FeederControlMode? ReportedMode, string? Error);

/// <summary>server → device: <c>…/reply</c> — the report is stored (or why not). The device stops resending its ack.</summary>
public sealed record MqttReplyMessage(
    int SchemaVersion,
    string CommandId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] LightingCommandStatus? Status,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Code,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? ModeRecorded);

/// <summary>device → server: <c>…/heartbeat</c> every 30 s (M-8).</summary>
public sealed record MqttHeartbeatMessage(int SchemaVersion, long? UptimeS, string? FirmwareVersion);

/// <summary>One message to publish: QoS 1, never retained.</summary>
public sealed record MqttOutbound(string Topic, byte[] Payload);
