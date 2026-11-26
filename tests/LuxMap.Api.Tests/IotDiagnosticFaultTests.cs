using System.Net.Http.Json;
using LuxMap.Modules.Faults;
using LuxMap.Modules.Faults.Entities;
using LuxMap.Modules.IOT;
using LuxMap.Modules.IOT.Jobs;
using LuxMap.Persistence;
using LuxMap.Persistence.Audit;
using LuxMap.Persistence.Jobs;
using LuxMap.Shared.Contracts.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LuxMap.Api.Tests;

[Collection(nameof(AssetDatabaseCollection))]
public sealed class IotDiagnosticFaultTests(AssetImportFixture fixture)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Sustained_loss_creates_one_fault_and_recovery_respects_assignment(bool assigned)
    {
        var rig = await new LightingTestRig(fixture).RigAsync(issueSecret: false);
        var contexts = fixture.Services.GetRequiredService<JobContextFactory>();
        var profile = new NodeProfile
        {
            ServerId = "test", DeviceId = "P001", FeederId = rig.Feeders[0],
            NominalVoltage = 5, NominalCurrentMa = 100, NominalPowerMw = 500,
        };
        var origin = DateTime.UtcNow.AddMinutes(-10);
        var lookup = new Assignments(assigned);
        async Task Pass(int step, bool failed)
        {
            var now = origin.AddSeconds(step * 30);
            await using var db = contexts.Open(fixture.CommuneId);
            var result = new IotDiagnosticResult(rig.Node, profile.FeederId, failed ? "anomaly" : "normal",
                new(now, "ON", failed ? 0 : 5, failed ? 0 : 100, failed ? 0 : 500),
                failed ? [new("voltage_loss")] : [])
            {
                MeasuredAt = now, MeasurementKey = $"session/{step + 1}", MeasurementSequence = step + 1,
                ElectricalStatus = "fresh", ElectricalWindow = new("evaluated", 5, 5, failed ? 0 : 5, failed ? 0 : 100, failed ? 0 : 500),
            };
            await IotDiagnosticJob.SaveAsync(db, lookup, rig.Node, profile, result, result.Status, now, "job:iot-test", default);
        }
        for (var i = 0; i < 6; i++) await Pass(i, true);
        await Pass(6, true);
        await Pass(6, true); // Same report and job retry cannot create another fault.
        await Pass(7, true);
        string faultId;
        await using (var db = contexts.Open(fixture.CommuneId))
        {
            var fault = Assert.Single(await db.Set<Fault>().Where(f => f.OriginNodeId == rig.Node).ToListAsync());
            faultId = fault.FaultId;
            Assert.Equal(FaultStatus.Detected, fault.FaultStatus);
            Assert.Equal("voltage_loss", fault.IotAnomalyCode);
            Assert.Equal(rig.Cabinet, fault.CabinetId);
            Assert.Equal(profile.FeederId, fault.FeederId);
            Assert.Null(fault.PoleId);
        }
        for (var i = 8; i <= 11; i++) await Pass(i, false);
        await using (var db = contexts.Open(fixture.CommuneId))
        {
            var fault = await db.Set<Fault>().SingleAsync(f => f.FaultId == faultId);
            Assert.Equal(FaultStatus.Detected, fault.FaultStatus);
            Assert.NotNull(fault.RecoveredAt);
            Assert.Null(fault.ConfirmedBy);
            var audit = await db.Set<AuditEvent>().Where(a => a.EntityId == faultId).ToListAsync();
            Assert.Equal(2, audit.Count);
            Assert.Contains(audit, a => a.Action == AuditAction.Reported);
            Assert.Null(fault.ConfirmedAt);
            Assert.Null(fault.ReviewNote);
        }
        using (var manager = await fixture.ManagerClientAsync())
        {
            var response = await manager.PostAsJsonAsync("/api/v1/work-orders", new {
                task_kind = "inspection", title = "Inspect a recovered IoT fault", fault_ids = new[] { faultId }
            });
            Assert.Equal(System.Net.HttpStatusCode.Created, response.StatusCode);
            var page = await manager.GetFromJsonAsync<System.Text.Json.JsonElement>(
                $"/api/v1/iot/feeders/{profile.FeederId}/diagnostics/history?page_size=200");
            var history = page.GetProperty("items").EnumerateArray().Where(e => e.GetProperty("fault_id").GetString() == faultId).ToArray();
            Assert.NotEmpty(history);
            Assert.All(history, e => Assert.Equal("detected", e.GetProperty("fault_status").GetString()));
        }
        await Pass(12, true);
        await Pass(13, true);
        await using (var db = contexts.Open(fixture.CommuneId))
        {
            var fault = Assert.Single(await db.Set<Fault>().Where(f => f.OriginNodeId == rig.Node).ToListAsync());
            Assert.Equal(faultId, fault.FaultId);
            Assert.Equal(FaultStatus.Detected, fault.FaultStatus);
            Assert.Null(fault.RecoveredAt);
            Assert.Contains("Tái phát", fault.Note);
            Assert.Equal(3, await db.Set<AuditEvent>().CountAsync(a => a.EntityId == faultId));
        }
    }

    [Fact]
    public async Task Fresh_acks_do_not_raise_offline_fault_and_recover_an_existing_detected_fault_with_audit()
    {
        var rig = await new LightingTestRig(fixture).RigAsync(issueSecret: false);
        var contexts = fixture.Services.GetRequiredService<JobContextFactory>();
        var profile = new NodeProfile
        {
            ServerId = "test", DeviceId = "probe", FeederId = rig.Feeders[0],
            NominalVoltage = 5, NominalCurrentMa = 100, NominalPowerMw = 500,
        };
        var origin = DateTime.UtcNow.AddMinutes(-10);
        async Task Pass(int step, bool connected)
        {
            var now = origin.AddSeconds(step * 30);
            await using var db = contexts.Open(fixture.CommuneId);
            var result = new IotDiagnosticResult(rig.Node, profile.FeederId, "stale",
                new(origin.AddMinutes(-5), "ON", 5, 100, 500), [new("telemetry_stale")])
            {
                ConnectionStatus = connected ? "online" : "offline", ElectricalStatus = "stale",
                LastSeenAt = connected ? now : origin.AddMinutes(-5),
                LastCommandAckAt = connected ? now : null, MeasurementKey = "old/1",
            };
            await IotDiagnosticJob.SaveAsync(db, new Assignments(false), rig.Node, profile,
                result, result.Status, now, "job:iot-contact-test", default);
        }
        await Pass(0, true);
        await using (var db = contexts.Open(fixture.CommuneId))
            Assert.False(await db.Set<Fault>().AnyAsync(f => f.OriginNodeId == rig.Node));
        await Pass(1, false);
        string faultId;
        await using (var db = contexts.Open(fixture.CommuneId))
        {
            var fault = Assert.Single(await db.Set<Fault>().Where(f => f.OriginNodeId == rig.Node).ToListAsync());
            faultId = fault.FaultId;
            Assert.Equal(FaultType.NodeOffline, fault.FaultType);
        }
        for (var step = 2; step <= 5; step++) await Pass(step, true);
        await using (var db = contexts.Open(fixture.CommuneId))
        {
            var fault = await db.Set<Fault>().SingleAsync(f => f.FaultId == faultId);
            Assert.Equal(FaultStatus.Detected, fault.FaultStatus);
            Assert.NotNull(fault.RecoveredAt);
            Assert.Contains(await db.Set<AuditEvent>().Where(a => a.EntityId == faultId).ToListAsync(),
                audit => audit.Action == AuditAction.Reported);
        }
    }

    [Fact]
    public async Task Current_drop_creates_one_current_anomaly_fault_with_window_evidence()
    {
        var rig = await new LightingTestRig(fixture).RigAsync(issueSecret: false);
        var contexts = fixture.Services.GetRequiredService<JobContextFactory>();
        var profile = new NodeProfile {
            DeviceId = "P001", FeederId = rig.Feeders[0], NominalVoltage = 5,
            NominalCurrentMa = 100, NominalPowerMw = 500,
        };
        var now = DateTime.UtcNow;
        var result = new IotDiagnosticResult(rig.Node, profile.FeederId, "anomaly",
            new(now, "ON", 5, 85, 425), [new("current_drop", 85, 100, -15, 5)]) {
            MeasuredAt = now, MeasurementKey = "s/2", MeasurementSequence = 2,
            ElectricalStatus = "fresh", ConnectionStatus = "online", LastSeenAt = now,
            ElectricalWindow = new("evaluated", 5, 5, 5, 85, 425) { CurrentEvaluationAvailable = true },
        };
        for (var attempt = 0; attempt < 2; attempt++) {
            await using var db = contexts.Open(fixture.CommuneId);
            await IotDiagnosticJob.SaveAsync(db, new Assignments(false), rig.Node, profile,
                result, result.Status, now, "job:current-drop-test", default);
        }
        await using var check = contexts.Open(fixture.CommuneId);
        var fault = Assert.Single(await check.Set<Fault>().Where(f => f.OriginNodeId == rig.Node).ToListAsync());
        Assert.Equal(FaultType.CurrentAnomaly, fault.FaultType);
        Assert.Equal("current_drop", fault.IotAnomalyCode);
        Assert.Contains("electrical_window", fault.IotEvidence);
        Assert.DoesNotContain("lamp_count", fault.IotEvidence);
        var episode = await check.Set<LuxMap.Modules.IOT.Entities.IotDiagnosticEpisode>()
            .SingleAsync(e => e.NodeId == rig.Node);
        Assert.Equal(fault.FaultId, episode.FaultId);
        Assert.Single(await check.Set<AuditEvent>().Where(a => a.EntityId == fault.FaultId).ToListAsync());
    }

    private sealed class Assignments(bool assigned) : IActiveWorkOrderLookup
    {
        public Task<IReadOnlyDictionary<string, string>> ActiveWorkOrdersAsync(IReadOnlyCollection<string> ids, CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<string, string>>(assigned ? ids.ToDictionary(id => id, _ => "WO-TEST") : new Dictionary<string, string>());
        public Task<IReadOnlyDictionary<string, string>> ActiveRepairsAsync(IReadOnlyCollection<string> ids, CancellationToken ct) =>
            ActiveWorkOrdersAsync(ids, ct);
    }
}
