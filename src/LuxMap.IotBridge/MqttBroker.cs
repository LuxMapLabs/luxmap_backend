using System.Text;
using System.Text.Json;
using LuxMap.IotWire;
using System.Collections.Concurrent;
using MQTTnet;
using MQTTnet.Protocol;
using MQTTnet.Server;

sealed class MqttBroker(DeviceStore store, GatewayCredentials credentials, IConfiguration configuration,
    IHostEnvironment environment, ILogger<MqttBroker> logger) : IHostedService, IDisposable
{
    private const int MaxNodesPerGateway = 32;
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


    public async Task<string?> IssueCredentialAsync(string gatewayId)
    {
        await commandGate.WaitAsync();
        try
        {
            EnsureGateway(gatewayId);
            return credentials.Issue(gatewayId);
        }
        finally { commandGate.Release(); }
    }

    public async Task RevokeCredentialAsync(string gatewayId)
    {
        await commandGate.WaitAsync();
        try
        {
            credentials.Revoke(gatewayId);
            lastHeartbeats.TryRemove(gatewayId, out _);
            if (IsRunning && (await broker!.GetClientsAsync()).Any(client => client.Id == gatewayId))
            {
                try
                {
                    await broker.DisconnectClientAsync(gatewayId,
                        new MQTTnet.Server.Disconnecting.MqttServerClientDisconnectOptions
                        { ReasonCode = MqttDisconnectReasonCode.NotAuthorized });
                }
                catch
                {
                    // A client can close itself between the snapshot and disconnect.
                    if ((await broker.GetClientsAsync()).Any(client => client.Id == gatewayId)) throw;
                }
            }
        }
        finally { commandGate.Release(); }
    }

    public void EnsureGateway(string gatewayId) => SaveGatewayRoute(gatewayId, null);

    public async Task<bool> RegisterGatewayAsync(string gatewayId, string nodeId)
    {
        if (!SaveGatewayRoute(gatewayId, nodeId)) return false;
        await commandGate.WaitAsync();
        try
        {
            if (IsRunning)
                await PublishCommandAsync(nodeId, store.Read(nodeId)?.DesiredCommand ?? new("AUTO", null, "initial-auto", 0));
        }
        finally { commandGate.Release(); }
        return true;
    }

    private bool SaveGatewayRoute(string gatewayId, string? nodeId)
    {
        lock (registrationGate)
        {
            var exists = gateways.TryGetValue(gatewayId, out var assigned);
            assigned ??= [];
            if (nodeId is null && exists || nodeId is not null && assigned.Contains(nodeId)) return true;
            if (nodeId is not null &&
                (assigned.Length >= MaxNodesPerGateway || gateways.Values.Any(nodes => nodes.Contains(nodeId))))
                return false;
            var saved = ReadRegistrations();
            saved[gatewayId] = nodeId is null ? [] : [.. saved.GetValueOrDefault(gatewayId) ?? [], nodeId];
            Directory.CreateDirectory(Path.GetDirectoryName(registrationPath)!);
            using (var file = File.Create(registrationPath + ".tmp"))
            {
                JsonSerializer.Serialize(file, saved);
                file.Flush(flushToDisk: true);
            }
            File.Move(registrationPath + ".tmp", registrationPath, overwrite: true);
            gateways = new(gateways, StringComparer.Ordinal) { [gatewayId] = nodeId is null ? [] : [.. assigned, nodeId] };
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
            gateways[gateway] = (gateways.GetValueOrDefault(gateway) ?? [])
                .Union(nodes, StringComparer.Ordinal).ToArray();
        if (gateways.Any(pair => !DeviceRules.ValidId(pair.Key) || pair.Value.Length > MaxNodesPerGateway ||
            pair.Value.Any(id => !DeviceRules.ValidId(id))) ||
            gateways.Values.SelectMany(nodes => nodes).GroupBy(id => id).Any(group => group.Count() > 1))
            throw new InvalidDataException("Invalid or duplicate gateway/node configuration.");
        var port = configuration.GetValue("Mqtt:Port", 1883);
        var optionsBuilder = new MqttServerOptionsBuilder().WithDefaultEndpoint().WithDefaultEndpointPort(port);
        // The TLS listener is for gateways reaching the bridge over the internet; it opens only when a certificate is set.
        var certificatePath = configuration["Mqtt:Tls:CertificatePath"];
        var tlsPort = configuration.GetValue("Mqtt:Tls:Port", 8883);
        PemCertificateProvider? certificates = null;
        if (!string.IsNullOrWhiteSpace(certificatePath))
        {
            certificates = new PemCertificateProvider(certificatePath,
                configuration["Mqtt:Tls:KeyPath"] ?? throw new InvalidDataException("Mqtt:Tls:KeyPath is not set."), logger);
            certificates.GetCertificate(); // a wrong path or a mismatched pair stops startup instead of every handshake
            optionsBuilder.WithEncryptedEndpoint().WithEncryptedEndpointPort(tlsPort);
        }
        var options = optionsBuilder.Build();
        if (certificates is not null) options.TlsEndpointOptions.CertificateProvider = certificates;
        broker = new MqttFactory().CreateMqttServer(options);
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
                if (!credentials.Validate(e.ClientId, e.UserName, e.Password))
                {
                    e.ReasonCode = MqttConnectReasonCode.BadUserNameOrPassword;
                    return;
                }
                foreach (var id in nodes)
                    await PublishCommandAsync(id, store.Read(id)?.DesiredCommand ?? new("AUTO", null, "initial-auto", 0));
            }
            finally { commandGate.Release(); }
        };
        broker.InterceptingSubscriptionAsync += e =>
        {
            if (!credentials.IsIssued(e.ClientId))
            {
                e.ProcessSubscription = false;
                e.Response.ReasonCode = MqttSubscribeReasonCode.NotAuthorized;
                return Task.CompletedTask;
            }
            var nodeList = gateways.ContainsKey(e.ClientId) &&
                e.TopicFilter.Topic == $"luxmap/gateways/{e.ClientId}/nodes";
            if (!nodeList && (!TryRoute(e.ClientId, e.TopicFilter.Topic, out _, out var kind) ||
                kind is not ("command" or "telemetryAck" or "sensorHealthAck")))
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
            if (!credentials.IsIssued(e.ClientId)) return;
            if (e.ApplicationMessage.Topic == $"luxmap/gateways/{e.ClientId}/heartbeat")
            {
                if (gateways.ContainsKey(e.ClientId) && !e.ApplicationMessage.Retain &&
                    e.ApplicationMessage.PayloadSegment.AsSpan().SequenceEqual("1"u8))
                    lastHeartbeats[e.ClientId] = TimeProvider.System.GetTimestamp();
                return;
            }
            if (e.ApplicationMessage.Topic == $"luxmap/gateways/{e.ClientId}/nodes/request")
            {
                if (gateways.TryGetValue(e.ClientId, out var nodes) && !e.ApplicationMessage.Retain &&
                    e.ApplicationMessage.PayloadSegment.AsSpan().SequenceEqual("1"u8))
                    await PublishTopicAsync($"luxmap/gateways/{e.ClientId}/nodes", new { deviceIds = nodes }, false);
                return;
            }
            if (!TryRoute(e.ClientId, e.ApplicationMessage.Topic, out var id, out var kind) ||
                kind is not ("telemetry" or "sensorHealth" or "commandAck") || e.ApplicationMessage.Retain ||
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
                if (kind == "sensorHealth")
                {
                    var report = JsonSerializer.Deserialize<SensorHealthReport>(payload, json);
                    if (report is null || !report.IsValidFor(id))
                    {
                        logger.LogWarning("Rejected {Id}: invalid sensor health report", id);
                        return;
                    }
                    store.AcceptSensorHealth(report);
                    await PublishAsync(id, "sensorHealthAck", new
                    {
                        kind = "sensorHealthAck", deviceId = id, sessionId = report.SessionId,
                        sequenceNumber = report.SequenceNumber
                    }, false);
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
        logger.LogInformation("MQTT broker listening on TCP {Port}{Tls}; gateways: {Count}", port,
            certificates is null ? "" : $" and TLS {tlsPort}", gateways.Count);
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
    private Task PublishCommandAsync(string id, LampCommand command)
    {
        if (command.ExpiresAtUnixSeconds is { } expiry && expiry <= DateTimeOffset.UtcNow.ToUnixTimeSeconds())
            return Task.CompletedTask;
        return PublishAsync(id, "command", new
        {
        kind = "command", deviceId = id, mode = command.Mode, command = command.Command,
        commandId = command.CommandId ?? "initial-auto", version = command.Version,
        autoSchedule = command.AutoSchedule, expiresAtUnixSeconds = command.ExpiresAtUnixSeconds
        }, true);
    }
    private Task PublishAsync(string id, string kind, object value, bool retained)
        => PublishTopicAsync($"luxmap/{id}/{kind}", value, retained);

    private Task PublishTopicAsync(string topic, object value, bool retained) => broker!.InjectApplicationMessage(
        new InjectedMqttApplicationMessage(new MqttApplicationMessageBuilder()
            .WithTopic(topic).WithPayload(JsonSerializer.Serialize(value, json))
            .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce).WithRetainFlag(retained).Build())
        { SenderClientId = ServerId });
    public async Task StopAsync(CancellationToken cancellationToken) { if (broker is not null) await broker.StopAsync(); }
    public void Dispose() { broker?.Dispose(); commandGate.Dispose(); }
}
