using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using LuxMap.Modules.Telemetry.Mqtt;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace LuxMap.Api.Tests;

/// <summary>A host running the MQTT channel's HTTP side (no broker): callback key set, channel = mqtt, transport removed.</summary>
public sealed class MqttHostFactory : WebApplicationFactory<Program>
{
    public const string CallbackKey = "test-callback-key-0123456789abcdef0123456789";
    public const string BackendPassword = "test-backend-password";

    public ConcurrentQueue<string> Disconnected { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
        => builder.UseEnvironment("Production").UseTestCorsOrigin()
            .UseSetting("Lighting:Channel", "mqtt")
            .UseSetting("Mqtt:Host", "broker.invalid")
            .UseSetting("Mqtt:BackendPassword", BackendPassword)
            .UseSetting("Mqtt:CallbackKey", CallbackKey)
            .ConfigureTestServices(services =>
            {
                // The transport would dial the broker; these tests are about the HTTP side.
                services.RemoveAll<IHostedService>();
                services.RemoveAll<IMqttBrokerAdmin>();
                services.AddSingleton<IMqttBrokerAdmin>(new Recorder(Disconnected));
            });

    private sealed class Recorder(ConcurrentQueue<string> calls) : IMqttBrokerAdmin
    {
        public Task DisconnectAsync(string nodeId, CancellationToken ct)
        {
            calls.Enqueue(nodeId);
            return Task.CompletedTask;
        }
    }
}

/// <summary>
/// LC-12 M-2/M-3/M-10/M-15 — the broker's callback (device secrets from the DB, ACL in the answer), the single channel switch,
/// and closing a session when a secret changes.
/// </summary>
[Collection(nameof(AssetDatabaseCollection))]
public sealed class BrokerAuthTests(AssetImportFixture fixture, MqttHostFactory mqtt) : IClassFixture<MqttHostFactory>
{
    private const string Callback = "/api/v1/internal/mqtt/auth";

    private LightingTestRig Rigs { get; } = new(fixture);

    [Fact]
    public async Task The_callback_answers_only_the_broker()
    {
        var body = new { clientid = "x", username = "x", password = "y" };
        Assert.Equal(HttpStatusCode.Unauthorized, (await mqtt.CreateClient().PostAsJsonAsync(Callback, body)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Broker("Broker wrong-key").PostAsJsonAsync(Callback, body)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Broker().PostAsJsonAsync(Callback, body)).StatusCode);

        // The default host has no callback key at all: nothing authenticates as the broker there.
        var plain = fixture.CreateClient();
        plain.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Broker", MqttHostFactory.CallbackKey);
        Assert.Equal(HttpStatusCode.Unauthorized, (await plain.PostAsJsonAsync(Callback, body)).StatusCode);
    }

    /// <summary>
    /// The device's secret is checked against the DATABASE; the answer carries exactly the device's own ACL. Literal topics:
    /// a rule widened by accident must show up here.
    /// </summary>
    [Fact]
    public async Task A_device_with_its_secret_is_allowed_with_exactly_its_own_topics()
    {
        var rig = await Rigs.RigAsync();
        var n = rig.Node;

        var answer = await AuthAsync(n, n, rig.Secret);

        Assert.Equal("allow", answer.GetProperty("result").GetString());
        Assert.False(answer.GetProperty("is_superuser").GetBoolean());
        Assert.Equal(
        [
            $"allow subscribe eq luxmap/v1/nodes/{n}/relays/+/command -",
            $"allow subscribe eq luxmap/v1/nodes/{n}/reply -",
            $"allow publish luxmap/v1/nodes/{n}/receipt False",
            $"allow publish luxmap/v1/nodes/{n}/ack False",
            $"allow publish luxmap/v1/nodes/{n}/heartbeat False",
            $"allow publish luxmap/v1/nodes/{n}/telemetry False",
            $"allow publish luxmap/v1/nodes/{n}/status -",
        ], Rules(answer));
    }

    [Fact]
    public async Task Every_wrong_identity_is_denied()
    {
        var rig = await Rigs.RigAsync();
        var other = await Rigs.RigAsync();

        foreach (var (clientId, username, password) in new[]
                 {
                     (rig.Node, rig.Node, "not-the-secret"),
                     (other.Node, rig.Node, rig.Secret),            // client id borrowed from another device
                     (rig.Node, other.Node, rig.Secret),            // another device's name, this one's secret
                     ("NODE-999999", "NODE-999999", rig.Secret),
                     ("NODE-#", "NODE-#", rig.Secret),
                     ("luxmap-backend", "luxmap-backend", "wrong"),
                 })
        {
            var answer = await AuthAsync(clientId, username, password);
            Assert.Equal("deny", answer.GetProperty("result").GetString());
            Assert.False(answer.TryGetProperty("acl", out _));
        }
    }

    [Fact]
    public async Task The_backend_client_is_allowed_to_command_and_listen_but_is_no_superuser()
    {
        var answer = await AuthAsync("luxmap-backend", "luxmap-backend", MqttHostFactory.BackendPassword);

        Assert.Equal("allow", answer.GetProperty("result").GetString());
        Assert.False(answer.GetProperty("is_superuser").GetBoolean());
        Assert.Equal(
        [
            "allow publish luxmap/v1/nodes/+/relays/+/command False",
            "allow publish luxmap/v1/nodes/+/reply False",
            "allow subscribe eq luxmap/v1/nodes/+/receipt -",
            "allow subscribe eq luxmap/v1/nodes/+/ack -",
            "allow subscribe eq luxmap/v1/nodes/+/heartbeat -",
            "allow subscribe eq luxmap/v1/nodes/+/status -",
            "allow subscribe eq luxmap/v1/nodes/+/telemetry -",
        ], Rules(answer));
    }

    /// <summary>M-10: with the MQTT channel, the HTTP poll / ack endpoints do not exist for a device.</summary>
    [Fact]
    public async Task With_the_mqtt_channel_the_http_device_endpoints_are_gone()
    {
        var rig = await Rigs.RigAsync();
        var device = mqtt.CreateClient();
        device.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Device", $"{rig.Node}.{rig.Secret}");

        Assert.Equal(HttpStatusCode.NotFound, (await device.GetAsync(LightingTestRig.Device)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await device.PostAsJsonAsync($"{LightingTestRig.Device}/CMD-000001/ack",
            new { seq = 1, result = "applied", reported_mode = "on" })).StatusCode);
    }

    /// <summary>M-15: a rotated secret or a deleted device closes the open MQTT session.</summary>
    [Fact]
    public async Task Rotating_a_secret_or_deleting_a_device_closes_its_session()
    {
        var manager = mqtt.CreateClient();
        manager.DefaultRequestHeaders.Authorization = (await fixture.ManagerClientAsync()).DefaultRequestHeaders.Authorization;
        var rig = await Rigs.RigAsync();

        Assert.Equal(HttpStatusCode.OK, (await manager.PostAsync($"/api/v1/assets/iot-nodes/{rig.Node}/credential", null)).StatusCode);
        Assert.Contains(rig.Node, mqtt.Disconnected);

        foreach (var relay in new[] { 1, 2 })
        {
            await manager.PutAsJsonAsync($"/api/v1/assets/iot-nodes/{rig.Node}/relays/{relay}", new { feeder_id = (string?)null });
        }

        var before = mqtt.Disconnected.Count(id => id == rig.Node);
        Assert.Equal(HttpStatusCode.NoContent, (await manager.DeleteAsync($"/api/v1/assets/iot-nodes/{rig.Node}")).StatusCode);
        Assert.Equal(before + 1, mqtt.Disconnected.Count(id => id == rig.Node));
    }

    private HttpClient Broker(string header = "Broker " + MqttHostFactory.CallbackKey)
    {
        var client = mqtt.CreateClient();
        client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", header);
        return client;
    }

    private async Task<JsonElement> AuthAsync(string clientId, string username, string password)
    {
        var response = await Broker().PostAsJsonAsync(Callback, new { clientid = clientId, username, password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
    }

    private static string[] Rules(JsonElement answer)
        => [.. answer.GetProperty("acl").EnumerateArray().Select(rule =>
            $"{rule.GetProperty("permission").GetString()} {rule.GetProperty("action").GetString()} {rule.GetProperty("topic").GetString()} "
            + (rule.TryGetProperty("retain", out var retain) ? retain.GetBoolean().ToString() : "-"))];
}
