using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LuxMap.Modules.Faults.Entities;
using LuxMap.Modules.IOT;
using LuxMap.Modules.IOT.Jobs;
using LuxMap.Modules.WorkOrders;
using LuxMap.Modules.WorkOrders.Entities;
using LuxMap.Persistence.Jobs;
using LuxMap.Persistence.Audit;
using LuxMap.Shared.Contracts.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LuxMap.Api.Tests;

[Collection(nameof(AssetDatabaseCollection))]
public sealed class IotInspectionLifecycleTests(AssetImportFixture fixture)
{
    private JobContextFactory Contexts => fixture.Services.GetRequiredService<JobContextFactory>();

    private async Task Pass(LightingTestRig.Rig rig, int step, params string[] codes)
    {
        var now = DateTime.UtcNow.AddSeconds(step);
        var profile = new NodeProfile { ServerId = "test", DeviceId = "P001", FeederId = rig.Feeders[0],
            NominalVoltage = 5, NominalCurrentMa = 100, NominalPowerMw = 500 };
        var result = new IotDiagnosticResult(rig.Node, profile.FeederId, "anomaly", new(now, "ON", 5, 85, 425),
            codes.Select(c => new IotAnomaly(c, 85, 100, -15, 5)).ToArray()) {
            MeasuredAt = now, MeasurementKey = $"s/{step}", MeasurementSequence = step,
            ConnectionStatus = "online", ElectricalStatus = "fresh", LastSeenAt = now,
            ElectricalWindow = new("evaluated", 4, 4, 5, 85, 425) { CurrentEvaluationAvailable = true },
        };
        await using var db = Contexts.Open(fixture.CommuneId);
        await IotDiagnosticJob.SaveAsync(db, new ActiveWorkOrderLookup(db), rig.Node, profile, result, result.Status,
            now, "job:inspection-lifecycle", default);
    }

    private async Task<string> FaultId(string node, string code)
    {
        await using var db = Contexts.Open(fixture.CommuneId);
        return await db.Set<Fault>().Where(f => f.OriginNodeId == node && f.IotAnomalyCode == code)
            .Select(f => f.FaultId).SingleAsync();
    }

    private static async Task<JsonElement> Ok(HttpResponseMessage response, HttpStatusCode status = HttpStatusCode.OK)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == status, $"Expected {status}, got {response.StatusCode}: {body}");
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    [Theory]
    [InlineData("open", true)]
    [InlineData("assigned", true)]
    [InlineData("in_progress", false)]
    [InlineData("done", false)]
    [InlineData("verified", false)]
    [InlineData("cancelled", false)]
    public async Task New_code_joins_only_a_waiting_inspection_once(string state, bool attached)
    {
        var rig = await new LightingTestRig(fixture).RigAsync(issueSecret: false);
        await Pass(rig, 1, "current_drop");
        var first = await FaultId(rig.Node, "current_drop");
        using var manager = await fixture.ManagerClientAsync();
        using var engineer = await fixture.FieldEngineerClientAsync();
        var me = await engineer.GetFromJsonAsync<JsonElement>("/api/v1/auth/me");
        var order = await Ok(await manager.PostAsJsonAsync("/api/v1/work-orders", new {
            task_kind = "inspection", title = "Check IoT wiring", fault_ids = new[] { first },
            assigned_to = state == "open" || state == "cancelled" ? null : me.GetProperty("user_id").GetString()
        }), HttpStatusCode.Created);
        var id = order.GetProperty("work_order_id").GetString();
        if (state is "in_progress" or "done" or "verified")
            await Ok(await engineer.PostAsJsonAsync($"/api/v1/work-orders/{id}/start", new { }));
        if (state is "done" or "verified")
            await Ok(await engineer.PostAsJsonAsync($"/api/v1/work-orders/{id}/complete", new {
                report_note = "Inspection finished", fault_outcomes = new[] { new { fault_id = first, outcome = "fault_present" } }
            }));
        if (state == "verified") await Ok(await manager.PostAsJsonAsync($"/api/v1/work-orders/{id}/verify", new { }));
        if (state == "cancelled") await Ok(await manager.PostAsJsonAsync($"/api/v1/work-orders/{id}/cancel", new { note = "Reschedule inspection" }));
        await Pass(rig, 2, "current_drop", "voltage_deviation");
        await Pass(rig, 2, "current_drop", "voltage_deviation");
        var second = await FaultId(rig.Node, "voltage_deviation");
        await using var check = Contexts.Open(fixture.CommuneId);
        Assert.Equal(attached ? 1 : 0, await check.Set<WorkOrderFault>().CountAsync(l => l.FaultId == second && l.WorkOrderId == id));
        Assert.Equal(FaultStatus.Detected, (await check.Set<Fault>().SingleAsync(f => f.FaultId == second)).FaultStatus);
        Assert.Equal(attached ? 1 : 0, await check.Set<AuditEvent>().CountAsync(a => a.EntityId == id &&
            a.ActorKind == AuditActorKind.Iot && a.Action == AuditAction.DetailsChanged));
    }

    [Theory]
    [InlineData("fault_present", FaultStatus.Confirmed)]
    [InlineData("fault_absent", FaultStatus.Rejected)]
    [InlineData("inconclusive", FaultStatus.Detected)]
    public async Task Only_manager_verification_decides_an_Iot_fault(string outcome, FaultStatus expected)
    {
        var rig = await new LightingTestRig(fixture).RigAsync(issueSecret: false);
        await Pass(rig, 1, "current_drop");
        var fault = await FaultId(rig.Node, "current_drop");
        using var manager = await fixture.ManagerClientAsync();
        using var engineer = await fixture.FieldEngineerClientAsync();
        var me = await engineer.GetFromJsonAsync<JsonElement>("/api/v1/auth/me");
        var order = await Ok(await manager.PostAsJsonAsync("/api/v1/work-orders", new {
            task_kind = "inspection", title = "Inspect IoT", fault_ids = new[] { fault },
            assigned_to = me.GetProperty("user_id").GetString()
        }), HttpStatusCode.Created);
        var id = order.GetProperty("work_order_id").GetString();
        await Ok(await engineer.PostAsJsonAsync($"/api/v1/work-orders/{id}/start", new { }));
        await Ok(await engineer.PostAsJsonAsync($"/api/v1/work-orders/{id}/complete", new {
            report_note = "Inspection finished", fault_outcomes = new[] { new { fault_id = fault, outcome } }
        }));
        var pending = await manager.GetFromJsonAsync<JsonElement>($"/api/v1/faults/{fault}");
        Assert.Equal("detected", pending.GetProperty("fault_status").GetString());
        await Ok(await manager.PostAsJsonAsync($"/api/v1/work-orders/{id}/verify", new { }));
        await using var check = Contexts.Open(fixture.CommuneId);
        var row = await check.Set<Fault>().SingleAsync(f => f.FaultId == fault);
        Assert.Equal(expected, row.FaultStatus);
        Assert.Equal(expected == FaultStatus.Detected ? null : fixture.ManagerUserId, row.ConfirmedBy);
    }
}
