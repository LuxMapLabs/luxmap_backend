using System.Text.Json;
using LuxMap.Modules.Assets.Entities;
using LuxMap.Modules.Faults.Entities;
using LuxMap.Modules.WorkOrders;
using LuxMap.Modules.WorkOrders.Entities;
using LuxMap.Shared.Contracts.Enums;
using Microsoft.EntityFrameworkCore;

namespace LuxMap.Api.Tests;

public partial class WorkOrderTests
{
    private Task<JsonElement> Suggestions(string? kind = null, string extra = "", string who = "manager", int expected = 200)
        => Send(who, "GET", $"/suggestions{(kind is null ? "?" : "/items?kind=" + kind + "&")}commune_id={home}&data_source=simulated{extra}", null, expected);

    private static JsonElement SuggestionGroup(JsonElement summary, string kind)
        => summary.GetProperty("groups").EnumerateArray().Single(x => x.GetProperty("kind").GetString() == kind);

    private static JsonElement[] SuggestionItems(JsonElement page) => page.GetProperty("items").EnumerateArray().ToArray();

    [Fact]
    public async Task Scheduling_suggestions_are_manager_only_and_validate_wire_filters_and_scope()
    {
        foreach (var path in new[] { "/suggestions", "/suggestions/items?kind=missing_gps" })
        {
            var separator = path.Contains('?') ? "&" : "?";
            foreach (var who in new[] { "a", "superior", "admin" })
                await Send(who, "GET", path, null, 403, "ROLE_FORBIDDEN");
            using var anonymous = factory.CreateClient();
            Assert.Equal(401, (int)(await anonymous.GetAsync(Route + path)).StatusCode);
            await Send("manager", "GET", path + separator + $"commune_id={foreign}", null, 403, "COMMUNE_FORBIDDEN");
            await Send("manager", "GET", path + separator + "data_source=2", null, 400, "VALIDATION_FAILED");
            await Send("manager", "GET", path + separator + "data_source=", null, 400, "VALIDATION_FAILED");
        }
        foreach (var kind in new[] { "bogus", "2", "fault_inspection,fault_repair" })
            await Suggestions(kind, expected: 400);
        await Suggestions("missing_gps", "&state=2", expected: 400);
        await Suggestions("missing_gps", "&state=", expected: 400);
        await Send("manager", "GET", "/suggestions/items", null, 400, "VALIDATION_FAILED");
    }

    [Fact]
    public async Task Scheduling_counts_separate_poles_from_faults_and_keep_effective_type_and_severity_order()
    {
        var pole = await PlantPole(.3);
        var one = await PlantPoleFault(pole, FaultStatus.Detected);
        var two = await PlantPoleFault(pole, FaultStatus.Detected);
        var high = await PlantPoleFault(pole, FaultStatus.Detected);
        var critical = await PlantPoleFault(pole, FaultStatus.Detected);
        var repair = await PlantPoleFault(pole, FaultStatus.Confirmed);
        await PlantPoleFault(pole, FaultStatus.Resolved);
        await PlantPoleFault(pole, FaultStatus.Rejected);
        await PlantPoleFault(pole, FaultStatus.Verified);
        await Db(async db =>
        {
            using var system = db.EnterUnscopedSystemWriteBackdoor();
            var faults = await db.Set<Fault>().IgnoreQueryFilters().Where(f => new[] { one, two, high, critical }.Contains(f.FaultId)).ToListAsync();
            foreach (var fault in faults)
            {
                fault.CreatedAt = fault.DetectedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                fault.Severity = fault.FaultId == one ? Severity.Low : fault.FaultId == two ? Severity.Medium
                    : fault.FaultId == high ? Severity.High : Severity.Critical;
            }
            faults.Single(f => f.FaultId == two).OverrideFaultType = FaultType.LampDim;
            await db.SaveChangesAsync();
            return 0;
        });

        var summary = await Suggestions();
        Assert.Equal(5, summary.GetProperty("groups").GetArrayLength());
        var inspection = SuggestionGroup(summary, "fault_inspection");
        Assert.Equal(4, inspection.GetProperty("total").GetInt32());
        Assert.Equal(1, inspection.GetProperty("pole_count").GetInt32());
        Assert.Equal(1, inspection.GetProperty("segment_count").GetInt32());
        Assert.Equal(4, inspection.GetProperty("ready_count").GetInt32());
        Assert.Equal(1, SuggestionGroup(summary, "fault_repair").GetProperty("total").GetInt32());
        Assert.EndsWith("Z", summary.GetProperty("generated_at").GetString());

        var items = SuggestionItems(await Suggestions("fault_inspection"));
        Assert.Equal([critical, high, two, one], items.Select(x => x.GetProperty("fault_id").GetString()!).ToArray());
        Assert.Equal("lamp_dim", items.Single(x => x.GetProperty("fault_id").GetString() == two).GetProperty("fault_type").GetString());
        Assert.All(items, x => Assert.Equal("inspection", x.GetProperty("recommended_task_kind").GetString()));
        Assert.Empty(SuggestionItems(await Suggestions("fault_inspection", "&segment_id=SEG-absent")));
        Assert.Equal(4, (await Suggestions("fault_inspection", $"&segment_id={segment}")).GetProperty("total").GetInt32());
        Assert.Equal(repair, SuggestionItems(await Suggestions("fault_repair")).Single().GetProperty("fault_id").GetString());
    }

    [Fact]
    public async Task Scheduling_sources_default_to_field_and_scope_before_counting_and_paging()
    {
        var field = await UnlocatedPole();
        var rig = await UnlocatedPole();
        var simulated = await UnlocatedPole();
        await Db(async db =>
        {
            using var system = db.EnterUnscopedSystemWriteBackdoor();
            (await db.Set<Pole>().IgnoreQueryFilters().SingleAsync(p => p.PoleId == field)).DataSource = DataSource.Field;
            (await db.Set<Pole>().IgnoreQueryFilters().SingleAsync(p => p.PoleId == rig)).DataSource = DataSource.CalibrationRig;
            db.Add(new Pole { CommuneId = foreign, SegmentId = foreignSegment, DataSource = DataSource.Field });
            await db.SaveChangesAsync();
            return 0;
        });

        var defaultPage = await Send("manager", "GET", "/suggestions/items?kind=missing_gps", null, 200);
        Assert.Equal([field], SuggestionItems(defaultPage).Select(x => x.GetProperty("pole_id").GetString()!).ToArray());
        var defaultSummary = await Send("manager", "GET", "/suggestions", null, 200);
        Assert.Equal(1, SuggestionGroup(defaultSummary, "missing_gps").GetProperty("total").GetInt32());
        var rigPage = await Send("manager", "GET", $"/suggestions/items?kind=missing_gps&data_source=calibration_rig&commune_id={home}", null, 200);
        Assert.Equal([rig], SuggestionItems(rigPage).Select(x => x.GetProperty("pole_id").GetString()!).ToArray());
        var both = await Send("manager", "GET", "/suggestions/items?kind=missing_gps&data_source=field,calibration_rig&page_size=1&page=2", null, 200);
        Assert.Equal(2, both.GetProperty("total").GetInt32());
        Assert.Equal(rig, SuggestionItems(both).Single().GetProperty("pole_id").GetString());
        Assert.Equal([simulated], SuggestionItems(await Suggestions("missing_gps")).Select(x => x.GetProperty("pole_id").GetString()!).ToArray());
        Assert.Empty(SuggestionItems(await Suggestions("missing_gps", "&segment_id=SEG-absent")));
    }

    [Fact]
    public async Task Scheduling_gps_warnings_show_prerequisites_existing_orders_and_release_after_cancellation()
    {
        var ready = await UnlocatedPole();
        var unassigned = await UnlocatedPole();
        var crossCommune = await UnlocatedPole();
        await Db(async db =>
        {
            using var system = db.EnterUnscopedSystemWriteBackdoor();
            (await db.Set<Pole>().IgnoreQueryFilters().SingleAsync(p => p.PoleId == ready)).ExternalRef = "scheduling-paper-list-1";
            (await db.Set<Pole>().IgnoreQueryFilters().SingleAsync(p => p.PoleId == unassigned)).SegmentId = null;
            (await db.Set<Pole>().IgnoreQueryFilters().SingleAsync(p => p.PoleId == crossCommune)).SegmentId = foreignSegment;
            await db.SaveChangesAsync();
            return 0;
        });
        var before = SuggestionItems(await Suggestions("missing_gps"));
        Assert.Equal("pole_gps_update", before.Single(x => x.GetProperty("id").GetString() == ready).GetProperty("recommended_task_kind").GetString());
        Assert.Equal("POLE_SEGMENT_REQUIRED", before.Single(x => x.GetProperty("id").GetString() == unassigned).GetProperty("blocking_reason").GetString());
        var hidden = before.Single(x => x.GetProperty("id").GetString() == crossCommune);
        Assert.Equal("SEGMENT_NOT_ACCESSIBLE", hidden.GetProperty("blocking_reason").GetString());
        Assert.Equal(JsonValueKind.Null, hidden.GetProperty("segment_name").ValueKind);
        var blocked = SuggestionItems(await Suggestions("missing_gps", "&state=blocked"));
        Assert.Equal(2, blocked.Length);
        Assert.All(blocked, x => Assert.Equal("blocked", x.GetProperty("state").GetString()));
        Assert.All(blocked, x => Assert.Equal(JsonValueKind.Null, x.GetProperty("recommended_task_kind").ValueKind));

        // Use the actual recommendation targets in the existing command.
        var candidate = before.Single(x => x.GetProperty("id").GetString() == ready);
        var created = await Send("manager", "POST", "", new
        {
            task_kind = candidate.GetProperty("recommended_task_kind").GetString(), title = "Thu GPS danh sách giấy",
            segment_id = candidate.GetProperty("segment_id").GetString(), pole_ids = new[] { candidate.GetProperty("pole_id").GetString() },
            assigned_to = users["a"].UserId, scheduled_date = "2030-01-01", due_date = "2030-01-02",
        }, 201);
        var order = created.GetProperty("work_order_id").GetString()!;
        var held = SuggestionItems(await Suggestions("missing_gps", "&state=in_work_order")).Single();
        Assert.Equal(order, held.GetProperty("work_order_id").GetString());
        Assert.Equal(users["a"].UserId, held.GetProperty("assigned_to").GetString());
        Assert.Equal("assigned", held.GetProperty("wo_status").GetString());
        Assert.Equal("Thu GPS danh sách giấy", held.GetProperty("work_order_title").GetString());
        Assert.Equal("2030-01-01", held.GetProperty("scheduled_date").GetString());
        Assert.Equal("2030-01-02", held.GetProperty("due_date").GetString());
        Assert.Equal("scheduling-paper-list-1", held.GetProperty("external_ref").GetString());
        Assert.Equal(JsonValueKind.Null, held.GetProperty("recommended_task_kind").ValueKind);
        Assert.Empty(SuggestionItems(await Suggestions("missing_gps", "&state=ready")));
        var counts = SuggestionGroup(await Suggestions(), "missing_gps");
        Assert.Equal(3, counts.GetProperty("total").GetInt32());
        Assert.Equal(1, counts.GetProperty("in_work_order_count").GetInt32());
        Assert.Equal(2, counts.GetProperty("blocked_count").GetInt32());
        await Send("manager", "POST", $"/{order}/cancel", new { note = "Dời lịch" }, 200);
        Assert.Equal(ready, SuggestionItems(await Suggestions("missing_gps", "&state=ready")).Single().GetProperty("pole_id").GetString());
    }

    [Fact]
    public async Task Scheduling_unknown_means_survey_and_missing_gps_is_not_a_fault_or_survey_candidate()
    {
        var absent = await PlantPole(.2);
        var unknown = await PlantPole(.3, status: FixtureStatus.Unknown);
        await PlantPole(.4, status: FixtureStatus.Normal, confidence: .9);
        await PlantPole(.5, status: FixtureStatus.Out, confidence: .9);
        await PlantPole(.6, status: FixtureStatus.Dim, confidence: .9);
        await UnlocatedPole();
        var candidates = SuggestionItems(await Suggestions("survey_needed"));
        Assert.Equal([absent, unknown], candidates.Select(x => x.GetProperty("pole_id").GetString()!).ToArray());
        Assert.All(candidates, x => Assert.Equal("survey", x.GetProperty("recommended_task_kind").GetString()));
        Assert.All(candidates, x => Assert.Equal(JsonValueKind.Null, x.GetProperty("observed_at").ValueKind));
        Assert.Empty(SuggestionItems(await Suggestions("fault_inspection")));
        Assert.Empty(SuggestionItems(await Suggestions("fault_repair")));
        var created = await Send("manager", "POST", "", new
        {
            task_kind = "survey", title = "Khảo sát đêm", commune_id = home, segment_ids = new[] { segment },
            // The warning is visible even when the order is scheduled outside the month on the calendar.
            scheduled_date = "2030-01-01",
        }, 201);
        var order = created.GetProperty("work_order_id").GetString()!;
        var held = SuggestionItems(await Suggestions("survey_needed", "&state=in_work_order"));
        Assert.Equal(2, held.Length);
        Assert.All(held, x => Assert.Equal(order, x.GetProperty("work_order_id").GetString()));
        Assert.Empty(SuggestionItems(await Suggestions("survey_needed", "&state=ready")));
        await Send("manager", "POST", $"/{order}/cancel", new { note = "Khảo sát lại sau" }, 200);
        Assert.Equal(2, (await Suggestions("survey_needed", "&state=ready")).GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Scheduling_faults_already_in_orders_remain_visible_without_suggesting_duplicate_creation()
    {
        var fault = await Fault(FaultStatus.Confirmed);
        var candidate = SuggestionItems(await Suggestions("fault_repair")).Single();
        var created = await Send("manager", "POST", "", new
        {
            task_kind = candidate.GetProperty("recommended_task_kind").GetString(), title = "Sửa đèn",
            fault_ids = new[] { candidate.GetProperty("fault_id").GetString() }, assigned_to = users["a"].UserId,
        }, 201);
        var order = created.GetProperty("work_order_id").GetString();
        var held = SuggestionItems(await Suggestions("fault_repair")).Single();
        Assert.Equal(fault, held.GetProperty("fault_id").GetString());
        Assert.Equal(order, held.GetProperty("work_order_id").GetString());
        Assert.Equal("in_work_order", held.GetProperty("state").GetString());
        Assert.Equal(JsonValueKind.Null, held.GetProperty("recommended_task_kind").ValueKind);
        Assert.Empty(SuggestionItems(await Suggestions("fault_repair", "&state=ready")));
        await Send("manager", "POST", "", new { task_kind = "repair", title = "Duplicate", fault_ids = new[] { fault } }, 409, "FAULT_ALREADY_IN_WORK_ORDER");
    }

    [Fact]
    public async Task Scheduling_overdue_uses_existing_night_and_state_rules_and_never_creates_another_order()
    {
        var night = new WorkOrderAgendaOptions().NightOf(DateTimeOffset.UtcNow);
        var late = await Plant(WorkOrderStatus.Assigned);
        var done = await Plant(WorkOrderStatus.Done);
        var dueTonight = await Plant(WorkOrderStatus.Open);
        await Db(async db =>
        {
            using var system = db.EnterUnscopedSystemWriteBackdoor();
            foreach (var id in new[] { late, done, dueTonight })
            {
                var order = await db.Set<WorkOrder>().IgnoreQueryFilters().SingleAsync(w => w.WorkOrderId == id);
                order.ScheduledDate = night.AddDays(-10);
                order.DueDate = id == dueTonight ? night : night.AddDays(-2);
            }
            await db.SaveChangesAsync();
            return 0;
        });
        var items = SuggestionItems(await Suggestions("overdue"));
        Assert.Equal(late, items.Single().GetProperty("work_order_id").GetString());
        Assert.Equal(JsonValueKind.Null, items.Single().GetProperty("recommended_task_kind").ValueKind);
        Assert.Equal(1, SuggestionGroup(await Suggestions(), "overdue").GetProperty("total").GetInt32());
        // Its segment is simulated: the default field view must not include testbed/simulated work.
        Assert.Empty(SuggestionItems(await Send("manager", "GET", "/suggestions/items?kind=overdue", null, 200)));
    }

    [Fact]
    public async Task Scheduling_gps_verify_removes_missing_gps_and_makes_the_pole_available_for_survey()
    {
        var pole = await UnlocatedPole();
        var order = await CreateGps([pole]);
        await Send("a", "POST", $"/{order}/start", null, 200);
        await Send("a", "POST", $"/{order}/complete", new
        {
            report_note = "Đã đo vị trí cột trên danh sách giấy.",
            pole_locations = new[] { Location(pole, 16.003, 108.003) },
        }, 200);
        // Done is still waiting on the manager: do not offer the same GPS job again.
        Assert.Equal(order, SuggestionItems(await Suggestions("missing_gps", "&state=in_work_order")).Single()
            .GetProperty("work_order_id").GetString());
        await Send("manager", "POST", $"/{order}/verify", new { note = "Đã đối chiếu." }, 200, auditExpected: 2);
        Assert.Empty(SuggestionItems(await Suggestions("missing_gps")));
        Assert.Equal(pole, SuggestionItems(await Suggestions("survey_needed", "&state=ready")).Single().GetProperty("pole_id").GetString());
    }

    [Fact]
    public async Task Scheduling_survey_can_be_scheduled_before_the_road_is_drawn_but_needs_a_road()
    {
        var undrawn = await PlantPole(.2);
        var noRoad = await PlantPole(.3);
        await Db(async db =>
        {
            using var system = db.EnterUnscopedSystemWriteBackdoor();
            (await db.Set<RoadSegment>().IgnoreQueryFilters().SingleAsync(r => r.SegmentId == segment)).Geom = null;
            (await db.Set<Pole>().IgnoreQueryFilters().SingleAsync(p => p.PoleId == noRoad)).SegmentId = null;
            await db.SaveChangesAsync();
            return 0;
        });
        var items = SuggestionItems(await Suggestions("survey_needed"));
        Assert.Equal(2, items.Length);
        var drawable = items.Single(x => x.GetProperty("id").GetString() == undrawn);
        Assert.Equal("ready", drawable.GetProperty("state").GetString());
        Assert.Equal("survey", drawable.GetProperty("recommended_task_kind").GetString());
        Assert.False(drawable.GetProperty("segment_has_geometry").GetBoolean());
        Assert.Equal("POLE_SEGMENT_REQUIRED", items.Single(x => x.GetProperty("id").GetString() == noRoad).GetProperty("blocking_reason").GetString());
        Assert.Equal(1, SuggestionGroup(await Suggestions(), "survey_needed").GetProperty("blocked_count").GetInt32());
        // A GPS order does not require a drawn road. Reuse PG rather than blocking both workflows.
        var unlocated = await UnlocatedPole();
        Assert.Equal(unlocated, SuggestionItems(await Suggestions("missing_gps", "&state=ready")).Single().GetProperty("id").GetString());
    }

    [Fact]
    public async Task Scheduling_overdue_survey_sources_do_not_match_unrelated_unassigned_poles()
    {
        var night = new WorkOrderAgendaOptions().NightOf(DateTimeOffset.UtcNow);
        var otherRoad = await SecondHomeRoad();
        var created = await Send("manager", "POST", "", new
        {
            task_kind = "survey", title = "Hai tuyến chưa khảo sát", commune_id = home,
            segment_ids = new[] { segment, otherRoad }, due_date = night.AddDays(-1),
        }, 201);
        var order = created.GetProperty("work_order_id").GetString();
        await Db(async db =>
        {
            using var system = db.EnterUnscopedSystemWriteBackdoor();
            // This field pole has nothing to do with either simulated road on the order.
            db.Add(new Pole { CommuneId = home, DataSource = DataSource.Field });
            await db.SaveChangesAsync();
            return 0;
        });
        Assert.Empty(SuggestionItems(await Send("manager", "GET", "/suggestions/items?kind=overdue", null, 200)));
        var rows = SuggestionItems(await Suggestions("overdue", $"&segment_id={otherRoad}"));
        Assert.Equal(order, rows.Single().GetProperty("work_order_id").GetString());
        Assert.Equal(otherRoad, rows.Single().GetProperty("segment_id").GetString());
        Assert.Equal(1, SuggestionGroup(await Suggestions(extra: $"&segment_id={otherRoad}"), "overdue").GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Scheduling_paging_keeps_ids_in_numeric_order_across_the_width_boundary()
    {
        string[] ids = ["POLE-999999", "POLE-1000000"];
        await Db(async db =>
        {
            using var system = db.EnterUnscopedSystemWriteBackdoor();
            Assert.False(await db.Set<Pole>().IgnoreQueryFilters().AnyAsync(p => ids.Contains(p.PoleId)));
            foreach (var id in ids.Reverse())
                db.Add(new Pole { PoleId = id, CommuneId = home, SegmentId = segment, DataSource = DataSource.Simulated,
                    CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) });
            await db.SaveChangesAsync();
            return 0;
        });
        var first = await Suggestions("missing_gps", "&page_size=1");
        var second = await Suggestions("missing_gps", "&page_size=1&page=2");
        Assert.Equal(2, first.GetProperty("total").GetInt32());
        Assert.Equal(ids[0], SuggestionItems(first).Single().GetProperty("pole_id").GetString());
        Assert.Equal(ids[1], SuggestionItems(second).Single().GetProperty("pole_id").GetString());
        Assert.Empty(SuggestionItems(await Suggestions("missing_gps", "&page_size=1&page=3")));
    }

    [Fact]
    public async Task Scheduling_overdue_uses_fault_source_for_fault_orders_and_pole_source_for_gps()
    {
        var pole = await PlantPole(.3);
        var gps = await CreateGps([pole]);
        var detected = await Fault();
        var confirmed = await Fault(FaultStatus.Confirmed);
        var inspection = await Create(faults: [detected]);
        var repair = await Create("repair", [confirmed]);
        var night = new WorkOrderAgendaOptions().NightOf(DateTimeOffset.UtcNow);
        await Db(async db =>
        {
            using var system = db.EnterUnscopedSystemWriteBackdoor();
            (await db.Set<Pole>().IgnoreQueryFilters().SingleAsync(p => p.PoleId == pole)).DataSource = DataSource.Field;
            foreach (var fault in await db.Set<Fault>().IgnoreQueryFilters().Where(f => f.FaultId == detected || f.FaultId == confirmed).ToListAsync())
                fault.DataSource = DataSource.CalibrationRig;
            foreach (var order in await db.Set<WorkOrder>().IgnoreQueryFilters().Where(w => w.WorkOrderId == gps
                         || w.WorkOrderId == inspection || w.WorkOrderId == repair).ToListAsync())
            {
                order.ScheduledDate = night.AddDays(-3);
                order.DueDate = night.AddDays(-2);
            }
            await db.SaveChangesAsync();
            return 0;
        });
        var field = SuggestionItems(await Send("manager", "GET", "/suggestions/items?kind=overdue", null, 200));
        Assert.Equal(gps, field.Single().GetProperty("work_order_id").GetString());
        var rig = SuggestionItems(await Send("manager", "GET", "/suggestions/items?kind=overdue&data_source=calibration_rig", null, 200));
        Assert.Equal(2, rig.Length);
        Assert.Contains(rig, x => x.GetProperty("work_order_id").GetString() == inspection);
        Assert.Contains(rig, x => x.GetProperty("work_order_id").GetString() == repair);
        Assert.Empty(SuggestionItems(await Suggestions("overdue")));
    }
}
