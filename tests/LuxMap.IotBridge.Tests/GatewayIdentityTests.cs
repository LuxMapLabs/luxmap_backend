using System.Net;
using System.Net.Sockets;
using LuxMap.IotWire;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using MQTTnet;
using MQTTnet.Client;

namespace LuxMap.IotBridge.Tests;

public sealed class GatewayIdentityTests
{
    [Fact]
    public async Task Canonical_registration_keeps_existing_gateway_routes_credentials_and_telemetry_after_restart()
    {
        var directory = Path.Combine(Path.GetTempPath(), "luxmap-identity-" + Guid.NewGuid().ToString("N"));
        var environment = new TestEnvironment(directory);
        var protection = DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(directory, "keys")));
        var credentials = new GatewayCredentials(environment, protection);
        var oldSecret = credentials.Issue("LUXMAP-GW001")!;
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["Mqtt:Port"] = FreePort().ToString(),
            ["Mqtt:Gateways:LUXMAP-GW001:0"] = "P001",
        }).Build();
        var store = new DeviceStore(environment);
        var broker = new MqttBroker(store, credentials, configuration, environment, NullLogger<MqttBroker>.Instance);
        using var old = new MqttFactory().CreateMqttClient();
        using var current = new MqttFactory().CreateMqttClient();
        try {
            await broker.StartAsync(default);
            var newSecret = (await broker.IssueCredentialAsync("iot-server-01"))!;
            var port = configuration.GetValue<int>("Mqtt:Port");
            await old.ConnectAsync(Options("LUXMAP-GW001", oldSecret, port));
            await Heartbeat(old, "LUXMAP-GW001");
            await Wait(() => broker.GatewayIdentityStatus("LUXMAP-GW001") == "online");
            Assert.Equal("offline", broker.GatewayIdentityStatus("iot-server-01"));
            await current.ConnectAsync(Options("iot-server-01", newSecret, port));
            await Heartbeat(current, "iot-server-01");
            await Wait(() => broker.GatewayIdentityStatus("iot-server-01") == "online");
            store.AcceptTelemetry("LUXMAP-GW001", new TelemetryRequest("P001", "MANUAL", "ON",
                10, 5, 100, 500, "2026-11-19T12:00:00", "retained-session", 1));
            Assert.True(await broker.RegisterGatewayAsync("iot-server-01", "P002"));
            Assert.True(old.IsConnected);
            Assert.True(credentials.IsIssued("LUXMAP-GW001"));
            Assert.True(credentials.Validate("iot-server-01", "iot-server-01", newSecret));
            Assert.Equal(new[] { "P002" }, broker.Gateways["iot-server-01"]);
            Assert.Equal(new[] { "P001" }, broker.Gateways["LUXMAP-GW001"]);
            Assert.Equal(100, store.Read("P001")!.LatestTelemetry!.Data.Current);
            await broker.StopAsync(default);
            broker.Dispose();
            broker = new(store, credentials, configuration, environment, NullLogger<MqttBroker>.Instance);
            await broker.StartAsync(default);
            Assert.Equal(new[] { "P001" }, broker.Gateways["LUXMAP-GW001"]); // Existing ACLs stay intact
            Assert.Equal(new[] { "P002" }, broker.Gateways["iot-server-01"]);
            Assert.Equal(100, store.Read("P001")!.LatestTelemetry!.Data.Current);
        }
        finally {
            await broker.StopAsync(default); broker.Dispose();
            SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Binding_compare_and_swap_rejects_wrong_owner_and_detach_does_not_revoke_other_nodes()
    {
        var directory = Path.Combine(Path.GetTempPath(), "luxmap-bind-" + Guid.NewGuid().ToString("N"));
        var environment = new TestEnvironment(directory);
        var credentials = new GatewayCredentials(environment,
            DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(directory, "keys"))));
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["Mqtt:Port"] = FreePort().ToString(),
        }).Build();
        using var broker = new MqttBroker(new DeviceStore(environment), credentials, config, environment, NullLogger<MqttBroker>.Instance);
        try {
            await broker.StartAsync(default);
            await broker.IssueCredentialAsync("iot-server-01");
            await broker.IssueCredentialAsync("iot-server-02");
            Assert.True(await broker.RegisterGatewayAsync("iot-server-01", "P001"));
            Assert.True(await broker.RegisterGatewayAsync("iot-server-01", "P002"));
            Assert.False(await broker.ChangeBindingAsync("iot-server-02", "P001", "unrelated", false));
            Assert.True(await broker.ChangeBindingAsync("iot-server-02", "P001", "iot-server-01", false));
            Assert.True(await broker.ChangeBindingAsync("iot-server-02", "P001", "iot-server-01", false));
            Assert.Equal(new[] { "P002" }, broker.Gateways["iot-server-01"]);
            Assert.False(await broker.ChangeBindingAsync("iot-server-01", "P001", "iot-server-01", true));
            Assert.True(await broker.ChangeBindingAsync("iot-server-02", "P001", "iot-server-02", true));
            Assert.True(await broker.ChangeBindingAsync("iot-server-02", "P001", "iot-server-02", true));
            Assert.Empty(broker.Gateways["iot-server-02"]);
            Assert.Equal(new[] { "P002" }, broker.Gateways["iot-server-01"]);
        }
        finally {
            await broker.StopAsync(default);
            SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }
    }

    private static MqttClientOptions Options(string id, string secret, int port) => new MqttClientOptionsBuilder()
        .WithClientId(id).WithCredentials(id, secret).WithTcpServer("127.0.0.1", port).Build();
    private static Task Heartbeat(IMqttClient client, string id) => client.PublishAsync(
        new MqttApplicationMessageBuilder().WithTopic($"luxmap/gateways/{id}/heartbeat").WithPayload("1").Build());
    private static async Task Wait(Func<bool> predicate) {
        for (var i = 0; i < 100 && !predicate(); i++) await Task.Delay(20);
        Assert.True(predicate());
    }
    private static int FreePort() {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
    private sealed class TestEnvironment(string directory) : IHostEnvironment {
        public string EnvironmentName { get; set; } = "Testing";
        public string ApplicationName { get; set; } = "LuxMap.IotBridge";
        public string ContentRootPath { get; set; } = directory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
