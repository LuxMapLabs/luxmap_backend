using System.Text;
using System.Text.Json;
using System.Collections.Concurrent;
using MQTTnet;
using MQTTnet.Protocol;
using MQTTnet.Server;

sealed class MqttBroker(DeviceStore store, GatewayCredentials credentials, IConfiguration configuration,
    IHostEnvironment environment, ILogger<MqttBroker> logger) : IHostedService, IDisposable
{
    private const string ServerId = "__luxmap-server";
    private readonly SemaphoreSlim commandGate = new(1, 1);
    private readonly JsonSerializerOptions json = new(JsonSerializerDefaults.Web);
    private MqttServer? broker;
    private volatile Dictionary<string, string[]> gateways = new(StringComparer.Ordinal);
    private readonly object registrationGate = new();
    private readonly string registrationPath = Path.Combine(environment.ContentRootPath, "data", "gateway-routes.json");
    private readonly ConcurrentDictionary<string, long> lastHeartbeats = new(StringComparer.Ordinal);
    public bool IsRunning => broker?.IsStarted == true;
    public IReadOnlyDictionary<string, string[]> Gateways => gateways;

    public bool RegisterGateway(string gatewayId, string nodeId)
    {
        lock (registrationGate)
        {
            if (gateways.TryGetValue(gatewayId, out var assigned)) return assigned.Contains(nodeId);
            if (gateways.Values.Any(nodes => nodes.Contains(nodeId))) return false;
            var saved = ReadRegistrations();
            saved.Add(gatewayId, [nodeId]);
            Directory.CreateDirectory(Path.GetDirectoryName(registrationPath)!);
            using (var file = File.Create(registrationPath + ".tmp"))
            {
                JsonSerializer.Serialize(file, saved);
                file.Flush(flushToDisk: true);
            }
            File.Move(registrationPath + ".tmp", registrationPath, overwrite: true);
            gateways = new(gateways, StringComparer.Ordinal) { [gatewayId] = [nodeId] };
            return true;
        }
    }

    private Dictionary<string, string[]> ReadRegistrations() => File.Exists(registrationPath)
        ? JsonSerializer.Deserialize<Dictionary<string, string[]>>(File.ReadAllText(registrationPath))
            ?? throw new InvalidDataException("Invalid gateway routes.")
        : new(StringComparer.Ordinal);

    public string GatewayStatus(string deviceId)
    {
        if (!IsRunning) return "broker_down";
        var matches = gateways.Where(pair => pair.Value.Contains(deviceId)).Take(2).ToArray();
        if (matches.Length != 1) return "unconfigured";
        return lastHeartbeats.TryGetValue(matches[0].Key, out var receivedAt) &&
            TimeProvider.System.GetElapsedTime(receivedAt) < TimeSpan.FromSeconds(30) ? "online" : "offline";
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        gateways = configuration.GetSection("Mqtt:Gateways").Get<Dictionary<string, string[]>>()
            ?? new() { ["LUXMAP-GW001"] = ["LUXMAP-P001"] };
        foreach (var (gateway, nodes) in ReadRegistrations())
            if (!gateways.TryAdd(gateway, nodes) && !gateways[gateway].SequenceEqual(nodes))
                throw new InvalidDataException("Conflicting MQTT gateway routes.");
        if (gateways.Any(pair => !DeviceRules.ValidId(pair.Key) || pair.Value.Length == 0 ||
            pair.Value.Any(id => !DeviceRules.ValidId(id))) ||
            gateways.Values.SelectMany(nodes => nodes).GroupBy(id => id).Any(group => group.Count() > 1))
            throw new InvalidDataException("Invalid or duplicate gateway/node configuration.");
        var port = configuration.GetValue("Mqtt:Port", 1883);
        broker = new MqttFactory().CreateMqttServer(new MqttServerOptionsBuilder()
            .WithDefaultEndpoint().WithDefaultEndpointPort(port).Build());
        broker.ValidatingConnectionAsync += async e =>
        {
            if (!gateways.TryGetValue(e.ClientId, out var nodes))
            {
                e.ReasonCode = MqttConnectReasonCode.ClientIdentifierNotValid;
                return;
            }
            if (!credentials.Validate(e.ClientId, e.UserName, e.Password))
            {
                e.ReasonCode = MqttConnectReasonCode.BadUserNameOrPassword;
                return;
            }
            await commandGate.WaitAsync(cancellationToken);
            try
            {
                foreach (var id in nodes)
                    await PublishCommandAsync(id, store.Read(id)?.DesiredCommand ?? new("AUTO", null, "initial-auto", 0));
            }
            finally { commandGate.Release(); }
        };
        broker.InterceptingSubscriptionAsync += e =>
        {
            if (!TryRoute(e.ClientId, e.TopicFilter.Topic, out _, out var kind) ||
                kind is not ("command" or "telemetryAck"))
            {
                e.ProcessSubscription = false;
                e.Response.ReasonCode = MqttSubscribeReasonCode.NotAuthorized;
            }
            return Task.CompletedTask;
        };
        broker.InterceptingPublishAsync += async e =>
        {
            if (e.ClientId == ServerId) return;
            e.ProcessPublish = false;
            if (e.ApplicationMessage.Topic == $"luxmap/gateways/{e.ClientId}/heartbeat")
            {
                if (gateways.ContainsKey(e.ClientId) && !e.ApplicationMessage.Retain &&
                    e.ApplicationMessage.PayloadSegment.AsSpan().SequenceEqual("1"u8))
                    lastHeartbeats[e.ClientId] = TimeProvider.System.GetTimestamp();
                return;
            }
            if (!TryRoute(e.ClientId, e.ApplicationMessage.Topic, out var id, out var kind) ||
                kind is not ("telemetry" or "commandAck") || e.ApplicationMessage.Retain ||
                e.ApplicationMessage.PayloadSegment.Count > 4096) return;
            try
            {
                var payload = Encoding.UTF8.GetString(e.ApplicationMessage.PayloadSegment);
                if (kind == "commandAck")
                {
                    var ack = JsonSerializer.Deserialize<CommandAcknowledgement>(payload, json);
                    if (ack is not null && ack.DeviceId == id) store.AcknowledgeCommand(ack);
                    return;
                }
                var data = JsonSerializer.Deserialize<TelemetryRequest>(payload, json);
                var error = DeviceRules.ValidateTelemetry(id, data);
                if (error is not null) { logger.LogWarning("Rejected {Id}: {Error}", id, error); return; }
                var inserted = store.AcceptTelemetry(e.ClientId, data!);
                // Only ACK after durable commit. Duplicates are ACKed too, including after restart.
                await PublishAsync(id, "telemetryAck", new
                {
                    kind = "telemetryAck", deviceId = id, sessionId = data!.SessionId,
                    sequenceNumber = data.SequenceNumber, duplicate = !inserted
                }, false);
            }
            catch (JsonException) { logger.LogWarning("Invalid MQTT JSON from {Id}", e.ClientId); }
            catch (Exception ex) { logger.LogError(ex, "Cannot commit/ACK data from {Id}; node must retry", e.ClientId); }
        };
        await broker.StartAsync();
        foreach (var device in store.ReadAll()) await PublishCommandAsync(device.DeviceId, device.DesiredCommand);
        logger.LogInformation("MQTT broker listening on TCP {Port}; gateways: {Count}", port, gateways.Count);
    }

    private bool TryRoute(string gatewayId, string topic, out string nodeId, out string kind)
    {
        var parts = topic.Split('/');
        nodeId = parts.Length == 3 ? parts[1] : "";
        kind = parts.Length == 3 ? parts[2] : "";
        return parts.Length == 3 && parts[0] == "luxmap" &&
            gateways.TryGetValue(gatewayId, out var nodes) && nodes.Contains(nodeId);
    }
    public async Task<LampCommand> SetCommandAsync(string id, LampCommand command, CancellationToken cancellationToken)
    {
        if (!gateways.Values.Any(nodes => nodes.Contains(id)))
            throw new ArgumentException("Node is not assigned to a gateway in Mqtt:Gateways.");
        await commandGate.WaitAsync(cancellationToken);
        try
        {
            if (!IsRunning) throw new InvalidOperationException("MQTT broker is unavailable.");
            var saved = store.SetCommand(id, command);
            await PublishCommandAsync(id, saved);
            return saved;
        }
        finally { commandGate.Release(); }
    }
    private Task PublishCommandAsync(string id, LampCommand command) => PublishAsync(id, "command", new
    {
        kind = "command", deviceId = id, mode = command.Mode, command = command.Command,
        commandId = command.CommandId ?? "initial-auto", version = command.Version,
        autoSchedule = command.AutoSchedule
    }, true);
    private Task PublishAsync(string id, string kind, object value, bool retained) => broker!.InjectApplicationMessage(
        new InjectedMqttApplicationMessage(new MqttApplicationMessageBuilder()
            .WithTopic($"luxmap/{id}/{kind}").WithPayload(JsonSerializer.Serialize(value, json))
            .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce).WithRetainFlag(retained).Build())
        { SenderClientId = ServerId });
    public async Task StopAsync(CancellationToken cancellationToken) { if (broker is not null) await broker.StopAsync(); }
    public void Dispose() { broker?.Dispose(); commandGate.Dispose(); }
}
