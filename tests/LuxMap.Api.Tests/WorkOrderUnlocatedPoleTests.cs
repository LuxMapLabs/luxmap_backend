using System.Text.Json;
using LuxMap.Modules.Assets.Entities;
using LuxMap.Modules.WorkOrders.Entities;
using LuxMap.Shared.Contracts.Enums;
using Microsoft.EntityFrameworkCore;

namespace LuxMap.Api.Tests;

/// <summary>POLE-GEOM-NULL PR 2a — a pole with no position inside a work order (GN-3, GN-6).</summary>
public partial class WorkOrderTests
{
    private Task<string> UnlocatedPole() => Db(async db =>
    {
        using var system = db.EnterUnscopedSystemWriteBackdoor();
        var pole = new Pole { CommuneId = home, SegmentId = segment, DataSource = DataSource.Simulated };
        db.Add(pole);
        await db.SaveChangesAsync();
        return pole.PoleId;
    });

    [Fact]
    public async Task Work_order_poles_list_an_unlocated_pole_last_on_its_road_without_a_position()
    {
        var unlocated = await UnlocatedPole();   // planted FIRST: neither insert order nor id order puts it last
        var far = await PlantPole(.8);
        var near = await PlantPole(.2);
        var id = await Plant(WorkOrderStatus.InProgress);

        var items = (await PolesOf(id, query: "?page_size=200")).GetProperty("items").EnumerateArray().ToArray();
        var ids = items.Select(p => p.GetProperty("pole_id").GetString()).ToArray();
        Assert.Equal(unlocated, ids[^1]);
        Assert.True(Array.IndexOf(ids, near) < Array.IndexOf(ids, far));
        var last = items[^1];
        Assert.Equal(JsonValueKind.Null, last.GetProperty("location").ValueKind);
        Assert.Equal(JsonValueKind.Null, last.GetProperty("position").ValueKind);
        Assert.Equal(segment, last.GetProperty("segment_id").GetString());
        // The placed poles keep an unbroken 1..n: the unlocated one takes no number.
        Assert.Equal(Enumerable.Range(1, items.Length - 1), items[..^1].Select(p => p.GetProperty("position").GetInt32()));
        Assert.All(items[..^1], p => Assert.Equal(JsonValueKind.Object, p.GetProperty("location").ValueKind));
    }

    [Fact]
    public async Task Pole_photo_on_an_unlocated_pole_is_refused_and_stores_nothing()
    {
        var unlocated = await UnlocatedPole();
        var id = await Plant(WorkOrderStatus.InProgress);

        var (status, body) = await PoleUpload(id, unlocated);

        Assert.Equal(409, status);
        Assert.Equal("POLE_LOCATION_REQUIRED", Code(body));
        var details = body.GetProperty("error").GetProperty("details");
        Assert.Equal(unlocated, details.GetProperty("pole_id").GetString());
        Assert.Equal("pole_photo", details.GetProperty("operation").GetString());
        Assert.Equal(0, await Db(db => db.Set<WorkOrderPolePhoto>().IgnoreQueryFilters().CountAsync(p => p.PoleId == unlocated)));
    }
}
