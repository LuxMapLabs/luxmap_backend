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
    private readonly ConcurrentDictionary<string, string> authenticatedSessions = new(StringComparer.Ordinal);
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
            authenticatedSessions.TryRemove(gatewayId, out _);
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
        await commandGate.WaitAsync();
        try
        {
            if (!credentials.IsIssued(gatewayId) || !SaveGatewayRoute(gatewayId, nodeId)) return false;
            if (IsRunning) {
                await PublishNodeListAsync(gatewayId);
                await PublishCommandAsync(nodeId, store.Read(nodeId)?.DesiredCommand ?? new("AUTO", null, "initial-auto", 0));
            }
            return true;
        }
        finally { commandGate.Release(); }
    }

    private bool SaveGatewayRoute(string gatewayId, string? nodeId)
    {
        lock (registrationGate)
        {
            var assigned = gateways.GetValueOrDefault(gatewayId) ?? [];
            if (nodeId is null && gateways.ContainsKey(gatewayId) || nodeId is not null && assigned.Contains(nodeId)) return true;
            if (nodeId is not null && (assigned.Length >= MaxNodesPerGateway ||
                gateways.Values.Any(nodes => nodes.Contains(nodeId)))) return false;
            var next = new Dictionary<string, string[]>(gateways, StringComparer.Ordinal) {
                [gatewayId] = nodeId is null ? [] : [.. assigned, nodeId],
            };
            SaveRoutes(next);
            return true;
        }
    }

    // Versioned full snapshots preserve removals even when startup appsettings had legacy routes.
    private void SaveRoutes(Dictionary<string, string[]> next)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(registrationPath)!);
        using (var file = File.Create(registrationPath + ".tmp")) {
            JsonSerializer.Serialize(file, new { version = 2, gateways = next });
            file.Flush(flushToDisk: true);
        }
        File.Move(registrationPath + ".tmp", registrationPath, overwrite: true);
        gateways = next;
    }

    private Dictionary<string, string[]> LoadRoutes(Dictionary<string, string[]> configured)
    {
        if (!File.Exists(registrationPath)) return configured;
        using var file = JsonDocument.Parse(File.ReadAllText(registrationPath));
        if (file.RootElement.TryGetProperty("version", out var version)) {
            if (version.GetInt32() != 2) throw new InvalidDataException("Unsupported gateway routes version.");
            return file.RootElement.GetProperty("gateways").Deserialize<Dictionary<string, string[]>>()
                ?? throw new InvalidDataException("Invalid gateway routes snapshot.");
        }
        var legacy = file.RootElement.Deserialize<Dictionary<string, string[]>>()
            ?? throw new InvalidDataException("Invalid legacy gateway routes.");
        foreach (var (id, devices) in legacy)
            configured[id] = (configured.GetValueOrDefault(id) ?? []).Union(devices, StringComparer.Ordinal).ToArray();
        return configured;
    }

    public string GatewayIdentityStatus(string gatewayId)
    {
        if (!gateways.ContainsKey(gatewayId)) return "unconfigured";
        if (!IsRunning) return "broker_down";
        return credentials.IsIssued(gatewayId) && lastHeartbeats.TryGetValue(gatewayId, out var received) &&
            TimeProvider.System.GetElapsedTime(received) < TimeSpan.FromSeconds(30) ? "online" : "offline";
    }

    private Task PublishNodeListAsync(string id) => PublishTopicAsync($"luxmap/gateways/{id}/nodes",
        new { serverId = id, deviceIds = gateways.GetValueOrDefault(id) ?? [] }, false);

    private async Task DisconnectAsync(string id)
    {
        if (IsRunning && (await broker!.GetClientsAsync()).Any(c => c.Id == id)) {
            try { await broker.DisconnectClientAsync(id, new MQTTnet.Server.Disconnecting.MqttServerClientDisconnectOptions
                { ReasonCode = MqttDisconnectReasonCode.NotAuthorized }); }
            catch { if ((await broker.GetClientsAsync()).Any(c => c.Id == id)) throw; }
        }
    }

    // Compare-and-swap prevents a delayed retry from stealing a device from another server.
    public async Task<bool> ChangeBindingAsync(string serverId, string deviceId, string? expectedOwner, bool detach)
    {
        await commandGate.WaitAsync();
        try
        {
            var owner = gateways.SingleOrDefault(p => p.Value.Contains(deviceId)).Key;
            if (detach && owner is null) return true;
            if (!detach && owner == serverId) return credentials.IsIssued(serverId);
            if (owner != expectedOwner || (detach && owner != serverId) ||
                (!detach && !credentials.IsIssued(serverId))) return false;
            var target = gateways.GetValueOrDefault(serverId) ?? [];
            if (!detach && target.Length >= MaxNodesPerGateway) return false;
            var next = new Dictionary<string, string[]>(gateways, StringComparer.Ordinal);
            if (owner is not null) next[owner] = next[owner].Where(id => id != deviceId).ToArray();
            if (!detach) next[serverId] = [.. target, deviceId];
            lock (registrationGate) SaveRoutes(next);
            // Existing MQTT subscriptions must not keep receiving commands for removed devices.
            if (owner is not null && owner != serverId || detach) await DisconnectAsync(owner!);
            if (IsRunning) {
                if (owner is not null) await PublishNodeListAsync(owner);
                if (!detach) await PublishNodeListAsync(serverId);
            }
            return true;
        }
        finally { commandGate.Release(); }
    }

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
        gateways = LoadRoutes(configuration.GetSection("Mqtt:Gateways").Get<Dictionary<string, string[]>>()
            ?? new(StringComparer.Ordinal));
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
                authenticatedSessions[e.ClientId] = credentials.Fingerprint(e.ClientId)!;
                foreach (var id in gateways.GetValueOrDefault(e.ClientId) ?? [])
                    await PublishCommandAsync(id, store.Read(id)?.DesiredCommand ?? new("AUTO", null, "initial-auto", 0));
            }
            finally { commandGate.Release(); }
        };
        broker.InterceptingSubscriptionAsync += e =>
        {
            if (!SessionAuthorized(e.ClientId))
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
            if (!SessionAuthorized(e.ClientId)) return;
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
                    await PublishNodeListAsync(e.ClientId);
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

    private bool SessionAuthorized(string id) =>
        authenticatedSessions.TryGetValue(id, out var fingerprint) && fingerprint == credentials.Fingerprint(id);

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
        autoSchedule = command.AutoSchedule, expiresAtUnixSeconds = command.ExpiresAtUnixSeconds,
        autoType = command.Mode == "AUTO" ? command.AutoType ?? "scheduled" : null
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
