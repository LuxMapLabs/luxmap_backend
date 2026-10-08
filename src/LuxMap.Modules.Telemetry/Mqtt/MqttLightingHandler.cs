using System.Buffers;
using System.Text.Json;
using LuxMap.Modules.Telemetry.Lighting;
using LuxMap.Shared.Contracts.Errors;
using LuxMap.Shared.Http;
using LuxMap.Shared.Serialization;
using Microsoft.Extensions.Logging;

namespace LuxMap.Modules.Telemetry.Mqtt;

/// <summary>
/// The MQTT adapter's logic, free of any MQTT library: messages in → <see cref="LightingCommandService"/> → messages out
/// (LC-12). The transport (<see cref="MqttLightingChannel"/>) only moves bytes; everything here is tested without a broker.
/// </summary>
/// <remarks>
/// The device id comes from the TOPIC — the broker ACL (M-3) lets a device publish only under its own id — and its commune from a
/// lookup made only to open the right scope (M-14). A message that is not understood is logged and dropped; it never throws into
/// the transport. A report always gets a <c>reply</c>, so the device knows when to stop resending (M-7).
/// </remarks>
public sealed class MqttLightingHandler(LightingScopeFactory scopes, ILogger<MqttLightingHandler> log)
{
    /// <summary>One inbound message; returns the reply to publish, if any.</summary>
    public async Task<MqttOutbound?> HandleAsync(string topic, ReadOnlySequence<byte> payload, CancellationToken ct)
    {
        if (MqttTopics.Parse(topic) is not { } target || payload.Length > MqttTopics.MaxPayloadBytes)
        {
            log.LogWarning("MQTT message dropped: unexpected topic or oversized payload on {Topic} ({Bytes} B).", topic, payload.Length);
            return null;
        }

        var commune = await scopes.CommuneOfAsync(target.NodeId, ct);
        if (commune is null)
        {
            log.LogWarning("MQTT message dropped: unknown device {NodeId}.", target.NodeId);
            return null;
        }

        await using var scope = scopes.Open(commune, $"mqtt-{Guid.NewGuid():N}");
        try
        {
            switch (target.Kind)
            {
                case MqttTopics.Receipt when Read<MqttReceiptMessage>(payload) is { CommandId: { } id, Seq: { } seq } && MqttTopics.IsCommandId(id):
                    await scope.Service.ReceiptAsync(target.NodeId, id, seq, ct);
                    return null;

                case MqttTopics.Ack when Read<MqttAckMessage>(payload) is { CommandId: { } id } ack && MqttTopics.IsCommandId(id):
                    return await AckAsync(scope.Service, target.NodeId, id, ack, ct);

                case MqttTopics.Heartbeat when Read<MqttHeartbeatMessage>(payload) is not null:
                    await scope.Service.TouchAsync(target.NodeId, ct);
                    return null;

                default:
                    log.LogWarning("MQTT message dropped: {Kind} from {NodeId} is not a valid v{Version} message.",
                        target.Kind, target.NodeId, MqttTopics.SchemaVersion);
                    return null;
            }
        }
        catch (LuxMapException refused)
        {
            log.LogWarning("MQTT {Kind} from {NodeId} refused: {Code}.", target.Kind, target.NodeId, refused.Code);
            return null;
        }
    }

    /// <summary>
    /// The open commands of every device that has any, as command messages — published again on every round until they close
    /// (M-4). Expiry and supersession are stored on the way, exactly as the poll does.
    /// </summary>
    public async Task<IReadOnlyList<MqttOutbound>> DispatchAsync(CancellationToken ct)
        => await DispatchAsync(await scopes.NodesWithOpenCommandsAsync(ct), ct);

    /// <summary>
    /// The same round for the given devices only. Production dispatches every device; a test on a shared database dispatches its
    /// own, so that storing expiry and supersession never reaches another test's rows (Codex review).
    /// </summary>
    public async Task<IReadOnlyList<MqttOutbound>> DispatchAsync(IEnumerable<(string NodeId, string CommuneId)> nodes, CancellationToken ct)
    {
        var messages = new List<MqttOutbound>();
        foreach (var (nodeId, communeId) in nodes)
        {
            try
            {
                await using var scope = scopes.Open(communeId, $"mqtt-dispatch-{Guid.NewGuid():N}");
                foreach (var command in await scope.Service.DispatchAsync(nodeId, ct))
                {
                    messages.Add(Message(MqttTopics.Command(nodeId, command.RelayNo), new MqttCommandMessage(
                        MqttTopics.SchemaVersion, command.CommandId, command.Seq, command.RelayNo, command.Mode, command.ExpiresAt)));
                }
            }
            catch (Exception failure) when (failure is not OperationCanceledException)
            {
                // One device's trouble must not stop the others' commands.
                log.LogError(failure, "MQTT dispatch failed for {NodeId}.", nodeId);
            }
        }

        return messages;
    }

    private static async Task<MqttOutbound> AckAsync(
        LightingCommandService service, string nodeId, string commandId, MqttAckMessage ack, CancellationToken ct)
    {
        var reply = Reply(nodeId);
        try
        {
            var stored = await service.AckAsync(nodeId, commandId,
                new DeviceAckRequest { Seq = ack.Seq, Result = ack.Result, ReportedMode = ack.ReportedMode, Error = ack.Error },
                ct, deliverIfPending: true);
            return Message(reply, new MqttReplyMessage(MqttTopics.SchemaVersion, commandId, stored.Status, null, null));
        }
        catch (LuxMapException refused)
        {
            // COMMAND_CLOSED still stored the report; the device must stop resending all the same.
            var status = refused.Details.TryGetValue("status", out var value) && value is string name
                ? WireEnum.Parse<LightingCommandStatus>(name, "status") : (LightingCommandStatus?)null;
            var recorded = refused.Details.TryGetValue("mode_recorded", out var flag) && flag is bool b ? b : (bool?)null;
            return Message(reply, new MqttReplyMessage(MqttTopics.SchemaVersion, commandId, status, refused.Code, recorded));
        }
    }

    private static string Reply(string nodeId) => MqttTopics.Of(nodeId, MqttTopics.Reply);

    private static MqttOutbound Message<T>(string topic, T body)
        => new(topic, JsonSerializer.SerializeToUtf8Bytes(body, LuxMapJsonOptions.Default));

    /// <summary>JSON of schema v1, or null — an unknown field type, an int where an enum belongs, another version: all null.</summary>
    private static T? Read<T>(ReadOnlySequence<byte> payload) where T : class
    {
        try
        {
            var reader = new Utf8JsonReader(payload);
            var message = JsonSerializer.Deserialize<T>(ref reader, LuxMapJsonOptions.Default);
            using var document = JsonDocument.Parse(payload);
            return document.RootElement.TryGetProperty("schema_version", out var version)
                && version.ValueKind == JsonValueKind.Number && version.GetInt32() == MqttTopics.SchemaVersion
                ? message : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
