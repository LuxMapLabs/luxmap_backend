using System.Net.Http.Json;
using System.Text.Json;
using LuxMap.Modules.Assets.Entities;
using LuxMap.Modules.Faults.Entities;
using LuxMap.Modules.WorkOrders.Entities;
using LuxMap.Persistence;
using LuxMap.Persistence.Audit;
using LuxMap.Shared.Contracts.Enums;
using Microsoft.EntityFrameworkCore;

namespace LuxMap.Api.Tests;

// Reuses WorkOrderTests' isolated accounts, AssetDatabaseCollection, evidence store and FK-ordered teardown.
public partial class WorkOrderTests
{
    private async Task<string> CompletedStatusRepair(params string[] faultIds)
    {
        var id = await Create("repair", faultIds, users["a"].UserId);
        await Send("a", "POST", $"/{id}/start", new { }, 200);
        Assert.Equal(201, (await Upload("a", id, "after")).Status);
        await Send("a", "POST", $"/{id}/complete", new { report_note = "Replaced and checked the lamp" }, 200);
        return id;
    }

    private Task<PoleCurrentStatus> RepairPoleStatus(string poleId) => Db(db => db.Set<PoleCurrentStatus>()
        .IgnoreQueryFilters().AsNoTracking().SingleAsync(x => x.PoleId == poleId));

    private async Task<JsonElement> RepairStatusAudit(string id)
    {
        var audit = await Db(db => db.Set<AuditEvent>().IgnoreQueryFilters()
            .SingleAsync(x => x.EntityId == id && x.Action == AuditAction.Verified));
        return JsonDocument.Parse(audit.AfterState!).RootElement.GetProperty("pole_status").Clone();
    }

    private static void RepairOutcome(JsonElement outcomes, string poleId, bool applied, string reason)
    {
        var outcome = Assert.Single(outcomes.EnumerateArray());
        Assert.Equal(poleId, outcome.GetProperty("pole_id").GetString());
        Assert.Equal(applied, outcome.GetProperty("applied").GetBoolean());
        Assert.Equal(reason, outcome.GetProperty("reason").GetString());
    }

    [Theory]
    [InlineData(FixtureStatus.Out, FaultType.LampOut, false)]
    [InlineData(FixtureStatus.Dim, FaultType.LampDim, false)]
    [InlineData(FixtureStatus.Out, FaultType.SegmentOutage, true)]
    public async Task Repair_verify_publishes_normal_for_the_effective_lamp_type(
        FixtureStatus initial, FaultType type, bool overridden)
    {
        var pole = await PlantPole(.2, status: initial, confidence: .91, lamp: true);
        var fault = await PlantPoleFault(pole, FaultStatus.Confirmed);
        await Db(async db =>
        {
            using var system = db.EnterUnscopedSystemWriteBackdoor();
            var row = await db.Set<Fault>().IgnoreQueryFilters().SingleAsync(x => x.FaultId == fault);
            row.FaultType = type; row.OverrideFaultType = overridden ? FaultType.LampOut : null;
            await db.SaveChangesAsync(); return 0;
        });
        var id = await CompletedStatusRepair(fault);
        Assert.Equal(initial, (await RepairPoleStatus(pole)).FixtureStatus);
        var completedAt = await Db(db => db.Set<WorkOrder>().IgnoreQueryFilters()
            .Where(x => x.WorkOrderId == id).Select(x => x.CompletedAt).SingleAsync());

        await Send("manager", "POST", $"/{id}/verify", new { }, 200);

        var map = await clients["manager"].GetFromJsonAsync<JsonElement>(
            "/api/v1/map/poles?bbox=108.00199,16.00199,108.00201,16.00201");
        var properties = Assert.Single(map.GetProperty("features").EnumerateArray(),
            x => x.GetProperty("properties").GetProperty("pole_id").GetString() == pole).GetProperty("properties");
        Assert.Equal("normal", properties.GetProperty("fixture_status").GetString());
        Assert.Equal(JsonValueKind.Null, properties.GetProperty("status_confidence").ValueKind);
        Assert.Equal(JsonValueKind.Null, properties.GetProperty("last_sweep_id").ValueKind);
        var current = await RepairPoleStatus(pole);
        Assert.Equal(completedAt, current.LastEvaluatedAt);
        Assert.Equal(completedAt, current.LastSeenAt);
        Assert.Null(current.LastRunId);
        Assert.False(typeof(IAudited).IsAssignableFrom(typeof(PoleCurrentStatus)));
        Assert.Equal(FaultStatus.Verified, await Db(db => db.Set<Fault>().IgnoreQueryFilters()
            .Where(x => x.FaultId == fault).Select(x => x.FaultStatus).SingleAsync()));
        RepairOutcome(await RepairStatusAudit(id), pole, true, "applied");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Repair_verify_preserves_status_when_another_lamp_fault_is_open(bool skippedInSameOrder)
    {
        var pole = await PlantPole(.2, status: FixtureStatus.Out, confidence: .91);
        var repaired = await PlantPoleFault(pole, FaultStatus.Confirmed);
        var other = await PlantPoleFault(pole, FaultStatus.Confirmed);
        var id = await CompletedStatusRepair(skippedInSameOrder ? [repaired, other] : [repaired]);
        // Even an open fault skipped by this verify counts. Use an override to exercise effective type.
        await Db(async db =>
        {
            using var system = db.EnterUnscopedSystemWriteBackdoor();
            var row = await db.Set<Fault>().IgnoreQueryFilters().SingleAsync(x => x.FaultId == other);
            row.FaultStatus = FaultStatus.Confirmed; row.FaultType = FaultType.SegmentOutage; row.OverrideFaultType = FaultType.LampDim;
            await db.SaveChangesAsync(); return 0;
        });
        var before = await RepairPoleStatus(pole);
        await Send("manager", "POST", $"/{id}/verify", new { }, 200);
        var after = await RepairPoleStatus(pole);
        Assert.Equal(FixtureStatus.Out, after.FixtureStatus);
        Assert.Equal(before.StatusConfidence, after.StatusConfidence);
        Assert.Equal(before.LastSeenAt, after.LastSeenAt);
        Assert.Equal(before.UpdatedAt, after.UpdatedAt);
        RepairOutcome(await RepairStatusAudit(id), pole, false, "other_open_fault");
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(0, false)]
    [InlineData(1, true)]
    public async Task Repair_verify_does_not_replace_an_observation_at_or_after_completion(int secondsLater, bool legacyClock)
    {
        var pole = await PlantPole(.2, status: FixtureStatus.Dim, confidence: .91);
        var id = await CompletedStatusRepair(await PlantPoleFault(pole, FaultStatus.Confirmed));
        await Db(async db =>
        {
            using var system = db.EnterUnscopedSystemWriteBackdoor();
            var wo = await db.Set<WorkOrder>().IgnoreQueryFilters().SingleAsync(x => x.WorkOrderId == id);
            // A later observation can still predate verification: using the manager's clock would wrongly overwrite it.
            wo.CompletedAt = wo.CompletedAt!.Value.AddMinutes(-2);
            wo.StartedAt = wo.CompletedAt.Value.AddMinutes(-1); wo.AssignedAt = wo.StartedAt;
            var status = await db.Set<PoleCurrentStatus>().IgnoreQueryFilters().SingleAsync(x => x.PoleId == pole);
            status.LastSeenAt = wo.CompletedAt.Value.AddSeconds(legacyClock ? secondsLater : -1);
            status.LastEvaluatedAt = legacyClock ? null : wo.CompletedAt.Value.AddSeconds(secondsLater);
            await db.SaveChangesAsync(); return 0;
        });
        var before = await RepairPoleStatus(pole);
        await Send("manager", "POST", $"/{id}/verify", new { }, 200);
        var after = await RepairPoleStatus(pole);
        Assert.Equal(FixtureStatus.Dim, after.FixtureStatus);
        Assert.Equal(.91, after.StatusConfidence);
        Assert.Equal(before.LastSeenAt, after.LastSeenAt);
        Assert.Equal(before.LastEvaluatedAt, after.LastEvaluatedAt);
        Assert.Equal(before.UpdatedAt, after.UpdatedAt);
        RepairOutcome(await RepairStatusAudit(id), pole, false, "not_newer");
    }

    [Fact]
    public async Task Repair_verify_deduplicates_poles_and_excludes_all_faults_just_verified()
    {
        var pole = await PlantPole(.2, status: FixtureStatus.Out, confidence: .91);
        var first = await PlantPoleFault(pole, FaultStatus.Confirmed);
        var second = await PlantPoleFault(pole, FaultStatus.Confirmed);
        var id = await CompletedStatusRepair(first, second);
        await Send("manager", "POST", $"/{id}/verify", new { }, 200);
        Assert.Equal(FixtureStatus.Normal, (await RepairPoleStatus(pole)).FixtureStatus);
        RepairOutcome(await RepairStatusAudit(id), pole, true, "applied");
    }

    [Fact]
    public async Task Repair_verify_of_a_poleless_segment_fault_publishes_no_status()
    {
        var pole = await PlantPole(.2, status: FixtureStatus.Out, confidence: .91);
        var fault = await Fault(FaultStatus.Confirmed);
        await Db(async db =>
        {
            using var system = db.EnterUnscopedSystemWriteBackdoor();
            var row = await db.Set<Fault>().IgnoreQueryFilters().SingleAsync(x => x.FaultId == fault);
            row.FaultType = FaultType.SegmentOutage;
            await db.SaveChangesAsync(); return 0;
        });
        var id = await CompletedStatusRepair(fault);
        await Send("manager", "POST", $"/{id}/verify", new { }, 200);
        Assert.Empty((await RepairStatusAudit(id)).EnumerateArray());
        Assert.Equal(FixtureStatus.Out, (await RepairPoleStatus(pole)).FixtureStatus);
    }

    [Fact]
    public async Task Repair_complete_and_return_leave_the_pole_status_unchanged()
    {
        var pole = await PlantPole(.2, status: FixtureStatus.Out, confidence: .91);
        var before = await RepairPoleStatus(pole);
        var id = await CompletedStatusRepair(await PlantPoleFault(pole, FaultStatus.Confirmed));
        var completed = await RepairPoleStatus(pole);
        Assert.Equal(FixtureStatus.Out, completed.FixtureStatus);
        Assert.Equal(before.UpdatedAt, completed.UpdatedAt);
        await Send("manager", "POST", $"/{id}/return", new { note = "Please check the lamp again" }, 200);
        var returned = await RepairPoleStatus(pole);
        Assert.Equal(FixtureStatus.Out, returned.FixtureStatus);
        Assert.Equal(before.UpdatedAt, returned.UpdatedAt);
        Assert.Equal(before.StatusConfidence, returned.StatusConfidence);
    }

    [Fact]
    public async Task Repair_verify_does_not_write_a_pole_outside_the_callers_commune_scope()
    {
        var pole = await PlantPole(.2, commune: foreign, road: foreignSegment, status: FixtureStatus.Out, confidence: .91);
        var fault = await Fault(FaultStatus.Confirmed);
        // The fault → pole FK permits this legacy mismatch, although normal API writers derive the commune from the pole.
        await Db(db => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE fault SET pole_id = {pole} WHERE fault_id = {fault}"));
        var id = await CompletedStatusRepair(fault);
        var before = await RepairPoleStatus(pole);
        await Send("manager", "POST", $"/{id}/verify", new { }, 200);
        Assert.Empty((await RepairStatusAudit(id)).EnumerateArray());
        var after = await RepairPoleStatus(pole);
        Assert.Equal(FixtureStatus.Out, after.FixtureStatus);
        Assert.Equal(before.UpdatedAt, after.UpdatedAt);
    }
}
