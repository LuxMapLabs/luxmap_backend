using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using LuxMap.Modules.Telemetry.Entities;
using LuxMap.Modules.Telemetry.Lighting;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using MQTTnet;
using MQTTnet.Formatter;
using MQTTnet.Protocol;

namespace LuxMap.Api.Tests;

/// <summary>
/// Runs only with <c>LUXMAP_MQTT_E2E=1</c> and an EMQX started by <c>docker compose --profile mqtt up -d emqx</c> whose callback
/// port is <c>MQTT_CALLBACK_PORT</c>. Everything else is checked without a broker; these tests check what only the real broker
/// decides: the callback is honoured, the ACL in its answer is enforced, and messages flow both ways.
/// </summary>
public sealed class BrokerFactAttribute : FactAttribute
{
    public BrokerFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("LUXMAP_MQTT_E2E") != "1")
        {
            Skip = "Needs EMQX: docker compose --profile mqtt up -d emqx, then LUXMAP_MQTT_E2E=1.";
        }
    }
}

/// <summary>The real API on a real port, with the MQTT channel running — the broker calls it back to authenticate.</summary>
public sealed class MqttEndToEndHost : WebApplicationFactory<Program>
{
    public static int CallbackPort => int.Parse(Environment.GetEnvironmentVariable("MQTT_CALLBACK_PORT") ?? "5141");

    public static int BrokerPort => int.Parse(Environment.GetEnvironmentVariable("MQTT_PORT") ?? "1883");

    public MqttEndToEndHost()
    {
        // Any address, not loopback only: on Linux the broker container reaches the host through host-gateway.
        UseKestrel(kestrel => kestrel.ListenAnyIP(CallbackPort));
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
        => builder.UseEnvironment("Production").UseTestCorsOrigin()
            .UseSetting("Lighting:Channel", "mqtt")
            .UseSetting("Mqtt:Host", "127.0.0.1")
            .UseSetting("Mqtt:Port", BrokerPort.ToString())
            .UseSetting("Mqtt:ResendInterval", "00:00:01");
}

/// <summary>LC-12 over a real EMQX: authentication by callback, ACL from the answer, command → receipt → ack → reply.</summary>
[Collection(nameof(AssetDatabaseCollection))]
public sealed class MqttEndToEndTests(AssetImportFixture fixture)
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(15);

    private LightingTestRig Rigs { get; } = new(fixture);

    [BrokerFact]
    public async Task A_device_receives_executes_and_acknowledges_over_the_real_broker()
    {
        var rig = await Rigs.RigAsync();
        using var host = new MqttEndToEndHost();
        host.StartServer();

        await using var device = await ReadyDeviceAsync(rig.Node, rig.Secret);
        var commands = await device.SubscribeAsync($"luxmap/v1/nodes/{rig.Node}/relays/+/command");
        var replies = await device.SubscribeAsync($"luxmap/v1/nodes/{rig.Node}/reply");
        Assert.Equal(MqttClientSubscribeResultCode.GrantedQoS1, commands);
        Assert.Equal(MqttClientSubscribeResultCode.GrantedQoS1, replies);

        var commandId = await Rigs.PressAsync(await fixture.ManagerClientAsync(), rig.Feeders[0], "off");
        var command = await device.NextAsync($"luxmap/v1/nodes/{rig.Node}/relays/1/command", Patience);
        Assert.Equal(commandId, command.GetProperty("command_id").GetString());
        var seq = command.GetProperty("seq").GetInt64();

        await device.PublishAsync($"luxmap/v1/nodes/{rig.Node}/receipt", new { schema_version = 1, command_id = commandId, seq });
        await Eventually(async () => await Rigs.StatusAsync(commandId) == "delivered");

        await device.PublishAsync($"luxmap/v1/nodes/{rig.Node}/ack",
            new { schema_version = 1, command_id = commandId, seq, result = "applied", reported_mode = "off" });
        var reply = await device.NextAsync($"luxmap/v1/nodes/{rig.Node}/reply", Patience);

        Assert.Equal("applied", reply.GetProperty("status").GetString());
        Assert.Equal("off", await Rigs.ModeAsync(rig.Feeders[0]));
    }

    /// <summary>
    /// The broker enforces what the callback answered: a wrong secret or a borrowed client id cannot connect; subscribing to another
    /// device's topics, or publishing into them, gets the client DISCONNECTED (<c>deny_action = disconnect</c>) — and the other
    /// device's command is untouched.
    /// </summary>
    [BrokerFact]
    public async Task The_broker_enforces_identity_and_each_devices_own_topics()
    {
        var mine = await Rigs.RigAsync();
        var theirs = await Rigs.RigAsync();
        using var host = new MqttEndToEndHost();
        host.StartServer();
        await (await ReadyDeviceAsync(mine.Node, mine.Secret)).DisposeAsync();

        Assert.NotEqual(MqttClientConnectResultCode.Success, await ConnectCodeAsync(mine.Node, mine.Node, "not-the-secret"));
        Assert.NotEqual(MqttClientConnectResultCode.Success, await ConnectCodeAsync(theirs.Node, mine.Node, mine.Secret)); // borrowed client id
        Assert.NotEqual(MqttClientConnectResultCode.Success, await ConnectCodeAsync(theirs.Node, theirs.Node, mine.Secret));

        await RefusedAsync(mine, device => device.SubscribeAsync($"luxmap/v1/nodes/{theirs.Node}/relays/+/command"));
        await RefusedAsync(mine, device => device.SubscribeAsync("luxmap/v1/nodes/#"));

        var commandId = await Rigs.PressAsync(await fixture.ManagerClientAsync(), theirs.Feeders[0], "off");
        var seq = await fixture.QueryAsync(db => db.Set<LightingCommand>().IgnoreQueryFilters().Where(c => c.CommandId == commandId).Select(c => c.Seq).SingleAsync());
        await RefusedAsync(mine, device => device.PublishAsync($"luxmap/v1/nodes/{theirs.Node}/ack",
            new { schema_version = 1, command_id = commandId, seq, result = "applied", reported_mode = "off" }, expectDelivery: false));

        await Task.Delay(TimeSpan.FromSeconds(2));
        Assert.Equal("pending", await Rigs.StatusAsync(commandId));
    }

    /// <summary>A fresh connection tries <paramref name="forbidden"/>: refused outright, or the broker disconnects it.</summary>
    private static async Task RefusedAsync(LightingTestRig.Rig rig, Func<Device, Task<MqttClientSubscribeResultCode>> forbidden)
    {
        await using var device = await ReadyDeviceAsync(rig.Node, rig.Secret);
        try
        {
            Assert.NotEqual(MqttClientSubscribeResultCode.GrantedQoS1, await forbidden(device));
        }
        catch (MQTTnet.Exceptions.MqttClientDisconnectedException)
        {
            // deny_action = disconnect: the broker closed the connection instead of answering.
        }

        await Eventually(() => Task.FromResult(!device.IsConnected));
    }

    private static Task RefusedAsync(LightingTestRig.Rig rig, Func<Device, Task> forbidden)
        => RefusedAsync(rig, async device =>
        {
            await forbidden(device);
            return MqttClientSubscribeResultCode.UnspecifiedError;
        });

    /// <summary>
    /// Connects with the RIGHT credentials, retrying: EMQX probes the callback when it starts and, finding no API there, waits
    /// up to its health-check interval (15 s) before trying again — every login is refused meanwhile.
    /// </summary>
    private static async Task<Device> ReadyDeviceAsync(string nodeId, string secret)
    {
        var until = DateTime.UtcNow + TimeSpan.FromSeconds(40);
        while (true)
        {
            var client = new MqttClientFactory().CreateMqttClient();
            var device = new Device(client);
            var result = await client.ConnectAsync(Options(nodeId, secret));
            if (result.ResultCode == MqttClientConnectResultCode.Success)
            {
                return device;
            }

            await device.DisposeAsync();
            Assert.True(DateTime.UtcNow < until, $"the broker kept refusing a valid device: {result.ResultCode}");
            await Task.Delay(1000);
        }
    }

    /// <summary>MQTTnet 5 does not throw on a refused CONNECT — it returns the broker's code.</summary>
    private static async Task<MqttClientConnectResultCode> ConnectCodeAsync(string clientId, string username, string secret)
    {
        using var client = new MqttClientFactory().CreateMqttClient();
        try
        {
            return (await client.ConnectAsync(Options(clientId, secret, username))).ResultCode;
        }
        catch (MQTTnet.Exceptions.MqttCommunicationException)
        {
            return MqttClientConnectResultCode.NotAuthorized;
        }
    }

    private static MqttClientOptions Options(string clientId, string secret, string? username = null) => new MqttClientOptionsBuilder()
        .WithTcpServer("127.0.0.1", MqttEndToEndHost.BrokerPort)
        .WithClientId(clientId)
        .WithCredentials(username ?? clientId, secret)
        .WithCleanSession(true)
        .WithProtocolVersion(MqttProtocolVersion.V311)
        .WithTimeout(TimeSpan.FromSeconds(10))
        .Build();

    private static async Task Eventually(Func<Task<bool>> condition)
    {
        var until = DateTime.UtcNow + Patience;
        while (!await condition())
        {
            Assert.True(DateTime.UtcNow < until, "condition not met in time");
            await Task.Delay(200);
        }
    }

    private sealed class Device : IAsyncDisposable
    {
        private readonly ConcurrentQueue<(string Topic, byte[] Payload)> received = new();

        public Device(IMqttClient client)
        {
            Client = client;
            client.ApplicationMessageReceivedAsync += message =>
            {
                received.Enqueue((message.ApplicationMessage.Topic, System.Buffers.BuffersExtensions.ToArray(message.ApplicationMessage.Payload)));
                return Task.CompletedTask;
            };
        }

        public IMqttClient Client { get; }

        public bool IsConnected => Client.IsConnected;

        public async Task<MqttClientSubscribeResultCode> SubscribeAsync(string filter)
        {
            var result = await Client.SubscribeAsync(new MqttClientSubscribeOptionsBuilder()
                .WithTopicFilter(filter, MqttQualityOfServiceLevel.AtLeastOnce).Build());
            return result.Items.Single().ResultCode;
        }

        public Task PublishAsync(string topic, object body, bool expectDelivery = true)
            => Client.PublishAsync(new MqttApplicationMessageBuilder()
                .WithTopic(topic)
                .WithPayload(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(body)))
                .WithQualityOfServiceLevel(expectDelivery ? MqttQualityOfServiceLevel.AtLeastOnce : MqttQualityOfServiceLevel.AtMostOnce)
                .Build());

        public async Task<JsonElement> NextAsync(string topic, TimeSpan patience)
        {
            var until = DateTime.UtcNow + patience;
            while (DateTime.UtcNow < until)
            {
                if (received.TryDequeue(out var message) && message.Topic == topic)
                {
                    return JsonDocument.Parse(message.Payload).RootElement.Clone();
                }

                await Task.Delay(100);
            }

            throw new TimeoutException($"Nothing on {topic} in {patience}.");
        }

        public async ValueTask DisposeAsync()
        {
            if (Client.IsConnected)
            {
                await Client.DisconnectAsync();
            }

            Client.Dispose();
        }
    }
}
