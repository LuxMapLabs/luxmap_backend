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

    [Fact]
    public void Auto_history_is_durable_deduplicated_and_ordered_by_measurement_not_replay_receipt()
    {
        var start = clock.GetUtcNow();
        TelemetryRequest Sample(int second, string mode, string lamp) => new(
            "probe", mode, lamp, 10, 5, 100, 500,
            start.AddSeconds(second).ToOffset(TimeSpan.FromHours(7)).ToString("yyyy-MM-ddTHH:mm:ss"),
            "session", second + 1);
        var bounds = new[] { new AutoHistoryWindow("probe", "feeder", start.AddSeconds(-1), null) };
        Assert.True(store.AcceptTelemetry("gateway", Sample(0, "AUTO", "ON")));
        Assert.True(store.AcceptTelemetry("gateway", Sample(1, "AUTO", "ON")));
        Assert.Empty(store.ReadAutoTransitions(bounds)); // Initial state and unchanged reports are not switches.
        Assert.True(store.AcceptTelemetry("gateway", Sample(3, "AUTO", "OFF")));
        Assert.True(store.AcceptTelemetry("gateway", Sample(2, "AUTO", "ON"))); // Late replay.
        Assert.False(store.AcceptTelemetry("gateway", Sample(3, "AUTO", "OFF")));
        Assert.True(store.AcceptTelemetry("gateway", Sample(4, "AUTO", "OFF")));
        Assert.True(store.AcceptTelemetry("gateway", Sample(5, "MANUAL", "ON")));
        Assert.True(store.AcceptTelemetry("gateway", Sample(6, "AUTO", "OFF"))); // Mode entry is a user command.
        Assert.True(store.AcceptTelemetry("gateway", Sample(7, "AUTO", "ON") with { AutoType = "full_time" }));
        var events = store.ReadAutoTransitions(bounds);
        Assert.Equal(2, events.Count);
        Assert.Equal("full_time", events[0].AutoType);
        Assert.Equal("scheduled", events[1].AutoType);
        Assert.Equal(new[] { "ON", "OFF" }, events.Select(e => e.Command));
        Assert.Equal(new[] { start.AddSeconds(7), start.AddSeconds(3) }, events.Select(e => e.OccurredAt));
        Assert.Equal(events.Count, events.Select(e => e.EventId).Distinct().Count());
        Assert.All(events, e => Assert.StartsWith("sensor-", e.EventId));
        Assert.Empty(store.ReadAutoTransitions([new("other", "feeder", start, null)]));
        Assert.Empty(store.ReadAutoTransitions([new("probe", "feeder", start.AddSeconds(7), null)]));
        var restored = new DeviceStore(new TestEnvironment(directory), clock);
        Assert.Equal(events, restored.ReadAutoTransitions(bounds));
    }

    [Fact]
    public void Full_time_is_persisted_and_requires_an_ack_that_confirms_the_same_auto_type()
    {
        var command = store.SetCommand("probe", new("AUTO", null, AutoType: "full_time"));
        Assert.Equal("full_time", command.AutoType);
        Assert.Null(command.AutoSchedule);
        store.AcknowledgeCommand(new("probe", command.CommandId, "AUTO", "ON"));
        Assert.False(store.Read("probe")!.CommandAcknowledged); // Legacy node only implements scheduled.
        store.AcknowledgeCommand(new("probe", command.CommandId, "AUTO", "ON", "scheduled"));
        Assert.False(store.Read("probe")!.CommandAcknowledged);
        store.AcknowledgeCommand(new("probe", command.CommandId, "AUTO", "ON", "full_time"));
        Assert.True(store.Read("probe")!.CommandAcknowledged);
        var restored = new DeviceStore(new TestEnvironment(directory), clock);
        Assert.Equal("full_time", restored.Read("probe")!.DesiredCommand.AutoType);
        Assert.True(restored.Read("probe")!.CommandAcknowledged);
        var legacy = restored.SetCommand("probe", new("AUTO", null));
        Assert.Equal("scheduled", legacy.AutoType);
        Assert.True(legacy.AutoSchedule!.IsValid());
        restored.AcknowledgeCommand(new("probe", legacy.CommandId, "AUTO", "OFF"));
        Assert.True(restored.Read("probe")!.CommandAcknowledged);
        Assert.Throws<ArgumentException>(() => restored.SetCommand("probe", new("AUTO", null, AutoType: "invalid")));
        Assert.Throws<ArgumentException>(() => restored.SetCommand("probe",
            new("AUTO", null, AutoSchedule: new(360, 1080), AutoType: "full_time")));
        Assert.Throws<ArgumentException>(() => restored.SetCommand("probe", new("MANUAL", "ON", AutoType: "full_time")));
    }

    [Fact]
    public void Existing_sqlite_receipts_are_upgraded_and_old_auto_commands_remain_scheduled()
    {
        var command = store.SetCommand("probe", new("AUTO", null));
        using (var db = new SqliteConnection(new SqliteConnectionStringBuilder {
            DataSource = Path.Combine(directory, "data", "telemetry.sqlite") }.ToString()))
        {
            db.Open();
            using var sql = db.CreateCommand();
            sql.CommandText = "ALTER TABLE command_receipts DROP COLUMN auto_type;";
            sql.ExecuteNonQuery();
        }
        var restored = new DeviceStore(new TestEnvironment(directory), clock);
        restored.AcknowledgeCommand(new("probe", command.CommandId, "AUTO", "ON"));
        Assert.True(restored.Read("probe")!.CommandAcknowledged);
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
