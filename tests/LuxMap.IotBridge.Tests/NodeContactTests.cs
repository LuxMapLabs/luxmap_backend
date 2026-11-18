using LuxMap.IotWire;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace LuxMap.IotBridge.Tests;

public sealed class NodeContactTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "luxmap-contact-" + Guid.NewGuid().ToString("N"));
    private readonly ManualClock clock = new();
    private readonly DeviceStore store;

    public NodeContactTests() => store = new(new TestEnvironment(directory), clock);

    [Fact]
    public void New_ack_updates_contact_but_duplicate_and_unmatched_ack_do_not_refresh_it()
    {
        var id = Guid.NewGuid().ToString("N");
        store.SetCommand("probe", new("MANUAL", "OFF", id));
        var ack = new CommandAcknowledgement("probe", id, "MANUAL", "OFF");
        store.AcknowledgeCommand(ack);
        var seen = clock.GetUtcNow();
        Assert.Equal(seen, store.Read("probe")!.LastSeenAt);
        Assert.Equal(seen, store.Read("probe")!.LastCommandAckAt);
        Assert.Null(store.Read("probe")!.LatestTelemetry);
        clock.Advance(61);
        store.AcknowledgeCommand(ack);
        store.AcknowledgeCommand(new("probe", Guid.NewGuid().ToString("N"), "MANUAL", "ON"));
        Assert.Equal(seen, store.Read("probe")!.LastSeenAt);
        Assert.Equal(seen, new DeviceStore(new TestEnvironment(directory), clock).Read("probe")!.LastCommandAckAt);
    }

    [Fact]
    public void A_new_late_ack_for_a_superseded_command_proves_contact_without_changing_current_power()
    {
        var old = Guid.NewGuid().ToString("N");
        var current = Guid.NewGuid().ToString("N");
        store.SetCommand("probe", new("MANUAL", "ON", old));
        store.SetCommand("probe", new("MANUAL", "OFF", current));
        store.AcknowledgeCommand(new("probe", old, "MANUAL", "ON"));
        var state = store.Read("probe")!;
        Assert.Equal(clock.GetUtcNow(), state.LastSeenAt);
        Assert.Equal(clock.GetUtcNow(), state.LastCommandAckAt);
        Assert.Null(state.LastCommandAck);
        Assert.False(state.CommandAcknowledged);
        Assert.Equal(current, state.DesiredCommand.CommandId);
        clock.Advance(61);
        store.AcknowledgeCommand(new("probe", old, "MANUAL", "ON"));
        Assert.True(store.Read("probe")!.LastSeenAt < clock.GetUtcNow().AddSeconds(-60));
    }

    [Fact]
    public void Older_replay_updates_contact_without_replacing_the_latest_electrical_sample()
    {
        var current = new TelemetryRequest("probe", "MANUAL", "ON", 10, 5, 100, 500,
            "2026-11-18T14:00:00", "current", 1);
        Assert.True(store.AcceptTelemetry("gateway", current));
        var latest = store.Read("probe")!.LatestTelemetry;
        clock.Advance(120);
        var replay = current with { SessionId = "old", Timestamp = "2026-11-17T14:00:00" };
        Assert.True(store.AcceptTelemetry("gateway", replay));
        Assert.Equal(latest, store.Read("probe")!.LatestTelemetry);
        var contact = clock.GetUtcNow();
        Assert.Equal(contact, store.Read("probe")!.LastSeenAt);
        clock.Advance(61);
        Assert.False(store.AcceptTelemetry("gateway", replay));
        Assert.Equal(contact, store.Read("probe")!.LastSeenAt);
    }

    [Fact]
    public void A_new_health_report_proves_contact_and_duplicate_report_does_not_extend_it()
    {
        var report = new SensorHealthReport("probe", "session", 1, "firmware-v1", ["ina226_unavailable"]);
        store.AcceptSensorHealth(report);
        var seen = clock.GetUtcNow();
        Assert.Equal(seen, store.Read("probe")!.LastSeenAt);
        Assert.Null(store.Read("probe")!.LatestTelemetry);
        clock.Advance(61);
        store.AcceptSensorHealth(report);
        Assert.Equal(seen, store.Read("probe")!.LastSeenAt);
    }

    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset now = DateTimeOffset.Parse("2026-11-18T07:00:00Z");
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(int seconds) => now = now.AddSeconds(seconds);
    }

    private sealed class TestEnvironment(string directory) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Testing";
        public string ApplicationName { get; set; } = "LuxMap.IotBridge";
        public string ContentRootPath { get; set; } = directory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(directory, recursive: true);
    }
}
