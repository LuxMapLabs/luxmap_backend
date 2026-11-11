using System.Net.Http.Json;
using System.Text.Json;
using LuxMap.Modules.Assets.Entities;
using LuxMap.Modules.WorkOrders.Entities;
using LuxMap.Shared.Contracts.Enums;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;

namespace LuxMap.Api.Tests;

/// <summary>
/// ROAD-SEGMENT-GEOM-NULL PR 2a — a road known only from the books has no line (<c>road_segment.geom IS NULL</c>). Rows 11, 12, 13
/// of the survey and the weekly-schedule path: none may answer 500, and creating / assigning / scheduling its orders must keep working.
/// PR 2a planted such roads straight into the database; PR 2b's writer makes them through the API.
/// </summary>
public partial class WorkOrderTests
{
    private Task<string> PlantUndrawnRoad() => Db(async db =>
    {
        using var system = db.EnterUnscopedSystemWriteBackdoor();
        var road = new RoadSegment { CommuneId = home, SegmentName = "WO undrawn road", RoadClass = RoadClass.InterVillage,
            DataSource = DataSource.Simulated, LengthM = 100, Geom = null };
        db.Add(road); await db.SaveChangesAsync(); return road.SegmentId;
    });

    private Task<string> PlantPoleOn(string road, bool located) => Db(async db =>
    {
        using var system = db.EnterUnscopedSystemWriteBackdoor();
        var pole = new Pole { CommuneId = home, SegmentId = road, DataSource = DataSource.Simulated,
            Geom = located ? new Point(108.003, 16.003) { SRID = 4326 } : null };
        db.Add(pole); await db.SaveChangesAsync(); return pole.PoleId;
    });

    private async Task<string> SurveyOrderOn(params string[] roads)
        => (await Send("manager", "POST", "", new { task_kind = "survey", title = "Survey with an undrawn road", commune_id = home,
            segment_ids = roads, assigned_to = users["a"].UserId }, 201)).GetProperty("work_order_id").GetString()!;

    [Fact]
    public async Task An_orders_poles_on_an_undrawn_road_have_no_position_and_come_after_the_drawn_road()
    {
        var undrawn = await PlantUndrawnRoad();
        var firstOnUndrawn = await PlantPoleOn(undrawn, located: true);
        var secondOnUndrawn = await PlantPoleOn(undrawn, located: false);
        var far = await PlantPole(.8);
        var near = await PlantPole(.2);
        var id = await SurveyOrderOn(segment, undrawn);

        var items = (await PolesOf(id, query: "?page_size=200")).GetProperty("items").EnumerateArray().ToArray();

        Assert.Equal([near, far, firstOnUndrawn, secondOnUndrawn], items.Select(x => x.GetProperty("pole_id").GetString()!));
        Assert.Equal([1, 2], items[..2].Select(x => x.GetProperty("position").GetInt32()));
        // "Along the road" means nothing without a road (RG-8): no number, however well placed the pole itself is.
        Assert.All(items[2..], x => Assert.Equal(JsonValueKind.Null, x.GetProperty("position").ValueKind));
        Assert.All(items[2..], x => Assert.Equal(undrawn, x.GetProperty("segment_id").GetString()));
        Assert.Equal(JsonValueKind.Object, items[2].GetProperty("location").ValueKind);
        Assert.Equal(JsonValueKind.Null, items[3].GetProperty("location").ValueKind);
    }

    [Fact]
    public async Task Auto_dispatch_previews_a_fault_on_a_pole_of_an_undrawn_road()
    {
        var undrawn = await PlantUndrawnRoad();
        var onUndrawn = await PlantPoleFault(await PlantPoleOn(undrawn, located: true), FaultStatus.Detected);
        var onDrawn = await PlantPoleFault(await PlantPole(.5), FaultStatus.Detected);

        await WithDispatchClient(async client =>
        {
            var preview = await Preview(client);

            Assert.Equal(2, preview.GetProperty("fault_count").GetInt32());
            var planned = preview.GetProperty("proposals").EnumerateArray()
                .SelectMany(p => p.GetProperty("fault_ids").EnumerateArray().Select(f => f.GetString())).ToArray();
            Assert.Equal(new[] { onDrawn, onUndrawn }.Order(), planned.Order());
            Assert.Contains(preview.GetProperty("proposals").EnumerateArray(),
                p => p.GetProperty("title").GetString()!.EndsWith("WO undrawn road", StringComparison.Ordinal));
        });
    }

    [Fact]
    public async Task The_agenda_keeps_an_undrawn_roads_group_by_name_with_no_place_and_no_distance_after_the_measured_ones()
    {
        var undrawn = await PlantUndrawnRoad();
        var survey = await SurveyOrderOn(undrawn);
        var inspection = await Plant(WorkOrderStatus.Assigned);

        // Without `near`: no distance to take, but the road's start point does not exist (was a 500 on StartPoint).
        var plain = Groups(await AgendaOf("a", "?night_of=2026-10-06"));
        Assert.Equal([segment, undrawn], plain.Select(x => x.GetProperty("segment_id").GetString()!));
        Assert.Equal(JsonValueKind.Null, plain[1].GetProperty("location").ValueKind);
        Assert.Equal("WO undrawn road", plain[1].GetProperty("segment_name").GetString());

        // With `near`: the measured road first, the undrawn one after it (was a 500 on ST_Distance(NULL, …)).
        var groups = Groups(await AgendaOf("a", "?night_of=2026-10-06&near=16.2,108.2"));
        Assert.Equal([segment, undrawn], groups.Select(x => x.GetProperty("segment_id").GetString()!));
        Assert.Equal([inspection], OrderIds(groups[0]));
        Assert.True(groups[0].GetProperty("distance_m").GetDouble() > 0);
        Assert.Equal([survey], OrderIds(groups[1]));
        Assert.Equal("WO undrawn road", groups[1].GetProperty("segment_name").GetString());
        Assert.Equal(JsonValueKind.Null, groups[1].GetProperty("location").ValueKind);
        Assert.Equal(JsonValueKind.Null, groups[1].GetProperty("distance_m").ValueKind);
    }

    [Fact]
    public async Task A_weekly_schedule_for_an_undrawn_road_generates_its_survey_order()
    {
        var undrawn = await PlantUndrawnRoad();
        await WithRecurringHost(async (client, clock, services) =>
        {
            var created = await NewRecurring(client, road: undrawn, assigned: users["a"].UserId);
            Assert.Equal("WO undrawn road", created.GetProperty("segment_name").GetString());

            clock.SetUtcNow(DateTimeOffset.Parse("2025-11-06T10:00:00Z", System.Globalization.CultureInfo.InvariantCulture));
            await RunRecurring(services, clock);

            var order = Assert.Single(await RecurringOrders());
            Assert.Equal("Khảo sát toàn tuyến - WO undrawn road", order.Title);
            Assert.Equal(WorkOrderStatus.Assigned, order.WoStatus);
            var target = await Db(db => db.Set<WorkOrderSegment>().IgnoreQueryFilters().SingleAsync(x => x.WorkOrderId == order.WorkOrderId));
            Assert.Equal(undrawn, target.SegmentId);
            var group = Assert.Single(Groups(await AgendaOf("a", "?night_of=2025-11-12")));
            Assert.Equal(undrawn, group.GetProperty("segment_id").GetString());
        });
    }

    [Fact]
    public async Task A_road_created_through_the_api_without_a_line_takes_poles_a_weekly_schedule_and_a_survey_order_and_is_drawn_later()
    {
        await WithRecurringHost(async (client, clock, services) =>
        {
            var poleA = await PlantFreePole(located: false);
            var poleB = await PlantFreePole(located: false);

            // The form of a commune that only has its books: name, class, declared length, the poles — no line yet (RG-2, RG-13).
            var created = await client.PostAsJsonAsync("/api/v1/assets/segments", new Dictionary<string, object?>
            {
                ["segment_name"] = "Books-only road", ["road_class"] = "inter_village", ["length_m"] = 900, ["commune_id"] = home,
                ["data_source"] = "simulated", ["pole_ids"] = new[] { poleA, poleB },
            });
            Assert.Equal(System.Net.HttpStatusCode.Created, created.StatusCode);
            var road = created.Headers.Location!.ToString().Split('/')[^1];

            // A weekly schedule and a survey order for it both work, and the order lists the poles with no position or place along the road.
            var schedule = await NewRecurring(client, road: road, assigned: users["a"].UserId);
            Assert.Equal("Books-only road", schedule.GetProperty("segment_name").GetString());
            var order = await SurveyOrderOn(road);
            var listed = (await PolesOf(order, query: "?page_size=200")).GetProperty("items").EnumerateArray().ToArray();
            Assert.Equal(new[] { poleA, poleB }.Order(), listed.Select(x => x.GetProperty("pole_id").GetString()!).Order());
            Assert.All(listed, x => Assert.Equal(JsonValueKind.Null, x.GetProperty("position").ValueKind));

            // Sweeping it stays refused until somebody draws it (RG-7, pinned in SurveyIngestTests); drawing it later is an ordinary PUT:
            // no pole has a position yet, so there is no corridor to break.
            var drawn = await client.PutAsJsonAsync($"/api/v1/assets/segments/{road}", new Dictionary<string, object?>
            {
                ["segment_name"] = "Books-only road", ["road_class"] = "inter_village", ["length_m"] = 900,
                ["data_source"] = "simulated", ["geom_wkt"] = "LINESTRING(108.1 16.1, 108.11 16.1)",
            });
            Assert.Equal(System.Net.HttpStatusCode.NoContent, drawn.StatusCode);
            Assert.True(await Db(db => db.Set<RoadSegment>().IgnoreQueryFilters().AnyAsync(x => x.SegmentId == road && x.Geom != null)));
        });
    }

    private Task<string> PlantFreePole(bool located) => Db(async db =>
    {
        using var system = db.EnterUnscopedSystemWriteBackdoor();
        var pole = new Pole { CommuneId = home, DataSource = DataSource.Simulated,
            Geom = located ? new Point(108.1, 16.1) { SRID = 4326 } : null };
        db.Add(pole); await db.SaveChangesAsync(); return pole.PoleId;
    });
}
