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
            };
            await IotDiagnosticJob.SaveAsync(db, lookup, rig.Node, profile, result, result.Status, now, "job:iot-test", default);
        }
        for (var i = 0; i < 6; i++) await Pass(i, true);
        await using (var db = contexts.Open(fixture.CommuneId))
            Assert.False(await db.Set<Fault>().AnyAsync(f => f.OriginNodeId == rig.Node));
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
            Assert.Equal(assigned ? FaultStatus.Detected : FaultStatus.Rejected, fault.FaultStatus);
            Assert.NotNull(fault.RecoveredAt);
            Assert.Null(fault.ConfirmedBy);
            var audit = await db.Set<AuditEvent>().Where(a => a.EntityId == faultId).ToListAsync();
            Assert.Equal(2, audit.Count);
            Assert.Contains(audit, a => a.Action == (assigned ? AuditAction.Reported : AuditAction.Rejected));
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
            Assert.Equal(FaultStatus.Rejected, fault.FaultStatus);
            Assert.NotNull(fault.RecoveredAt);
            Assert.Contains(await db.Set<AuditEvent>().Where(a => a.EntityId == faultId).ToListAsync(),
                audit => audit.Action == AuditAction.Rejected);
        }
    }

    private sealed class Assignments(bool assigned) : IActiveWorkOrderLookup
    {
        public Task<IReadOnlyDictionary<string, string>> ActiveWorkOrdersAsync(IReadOnlyCollection<string> ids, CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<string, string>>(assigned ? ids.ToDictionary(id => id, _ => "WO-TEST") : new Dictionary<string, string>());
        public Task<IReadOnlyDictionary<string, string>> ActiveRepairsAsync(IReadOnlyCollection<string> ids, CancellationToken ct) =>
            ActiveWorkOrdersAsync(ids, ct);
    }
}
