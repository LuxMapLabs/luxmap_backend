using System.Net;
using System.Text.Json;
using LuxMap.Modules.Assets.Entities;
using LuxMap.Modules.Telemetry.Entities;
using LuxMap.Persistence;
using LuxMap.Shared.Contracts.Enums;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;

namespace LuxMap.Api.Tests;

[Collection(nameof(AssetDatabaseCollection))]
public sealed class FeederStateTests(AssetImportFixture fixture)
{
    internal const string MapUrl = "/api/v1/map/cabinets?bbox=108.70,12.70,108.90,12.90&data_source=simulated";

    [Theory]
    [InlineData(null, false)]
    [InlineData(null, true)]
    [InlineData("on", true)]
    [InlineData("off", true)]
    [InlineData("auto", true)]
    public async Task Inventory_and_map_preserve_reports_but_never_infer_power(string? mode, bool connected)
    {
        var rig = await Plant(fixture, connected ? 2 : 1, mode);
        var client = await fixture.ManagerClientAsync();
        var list = await Json(client, "/api/v1/assets/feeders?page_size=200");
        var item = list.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("feeder_id").GetString() == rig.Feeders[0]);
        var detail = (await Json(client, $"/api/v1/assets/feeders/{rig.Feeders[0]}")).GetProperty("feeder");
        foreach (var row in new[] { item, detail })
        {
            Assert.Equal("unknown", row.GetProperty("power_state").GetString());
            var control = row.GetProperty("control");
            if (!connected) Assert.Equal(JsonValueKind.Null, control.ValueKind);
            else
            {
                Assert.Equal(rig.Node, control.GetProperty("node_id").GetString());
                Assert.Equal(1, control.GetProperty("relay_no").GetInt32());
                Assert.Equal(mode, control.GetProperty("control_mode").GetString());
                Assert.Equal(mode is null ? JsonValueKind.Null : JsonValueKind.String, control.GetProperty("mode_reported_at").ValueKind);
                if (mode is not null) Assert.EndsWith("Z", control.GetProperty("mode_reported_at").GetString());
            }
        }

        var props = Cabinet(await Json(client, MapUrl), rig.Cabinet);
        Assert.Equal(rig.Feeders, props.GetProperty("feeder_ids").EnumerateArray().Select(i => i.GetString()));
        var feeders = props.GetProperty("feeders").EnumerateArray().ToArray();
        Assert.Equal(rig.Feeders, feeders.Select(i => i.GetProperty("feeder_id").GetString()));
        Assert.All(feeders, feeder => Assert.Equal("unknown", feeder.GetProperty("power_state").GetString()));
        Assert.Equal(connected ? mode : null, feeders[0].GetProperty("control_mode").GetString());
        Assert.Equal(connected ? JsonValueKind.Number : JsonValueKind.Null, feeders[0].GetProperty("relay_no").ValueKind);
        Assert.Equal(connected && mode is not null ? JsonValueKind.String : JsonValueKind.Null, feeders[0].GetProperty("mode_reported_at").ValueKind);
        if (connected)
        {
            Assert.Equal(JsonValueKind.Null, feeders[1].GetProperty("relay_no").ValueKind);
            Assert.Equal(JsonValueKind.Null, feeders[1].GetProperty("control_mode").ValueKind);
            Assert.Equal(JsonValueKind.Null, feeders[1].GetProperty("mode_reported_at").ValueKind);
        }
    }

    [Fact]
    public async Task Offline_preserves_the_last_mode_and_empty_cabinets_have_both_arrays()
    {
        var rig = await Plant(fixture, 2, "on");
        await fixture.QueryAsync(async db =>
        {
            using var system = db.EnterUnscopedSystemWriteBackdoor();
            var node = await db.Set<IotNode>().IgnoreQueryFilters().SingleAsync(n => n.NodeId == rig.Node);
            node.LastReportAt = DateTime.UtcNow.AddDays(-2);
            return await db.SaveChangesAsync();
        });
        var empty = await Plant(fixture, 0, null);
        var client = await fixture.ManagerClientAsync();
        var detail = (await Json(client, $"/api/v1/assets/feeders/{rig.Feeders[0]}")).GetProperty("feeder");
        Assert.Equal("on", detail.GetProperty("control").GetProperty("control_mode").GetString());
        Assert.Equal("unknown", detail.GetProperty("power_state").GetString());
        var map = await Json(client, MapUrl);
        Assert.Equal("on", Cabinet(map, rig.Cabinet).GetProperty("feeders")[0].GetProperty("control_mode").GetString());
        Assert.Empty(Cabinet(map, empty.Cabinet).GetProperty("feeders").EnumerateArray());
        Assert.Empty(Cabinet(map, empty.Cabinet).GetProperty("feeder_ids").EnumerateArray());
    }

    [Fact]
    public async Task Commune_scope_hides_foreign_feeders_controls_and_cabinets()
    {
        var foreign = await Plant(fixture, 2, "on", fixture.ForeignCommuneId);
        var client = await fixture.ManagerClientAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/assets/feeders/{foreign.Feeders[0]}")).StatusCode);
        Assert.DoesNotContain((await Json(client, MapUrl)).GetProperty("features").EnumerateArray(),
            f => f.GetProperty("properties").GetProperty("cabinet_id").GetString() == foreign.Cabinet);
        var list = await Json(client, "/api/v1/assets/feeders?page_size=200");
        Assert.DoesNotContain(list.GetProperty("items").EnumerateArray(), i => foreign.Feeders.Contains(i.GetProperty("feeder_id").GetString()));
    }

    internal sealed record Rig(string Cabinet, string? Node, string[] Feeders);

    // All IDs are assigned by the database. Fixture teardown removes control -> node -> feeder -> cabinet.
    internal static Task<Rig> Plant(AssetImportFixture fixture, int count, string? mode, string? commune = null)
        => fixture.QueryAsync(async db =>
        {
            using var system = db.EnterUnscopedSystemWriteBackdoor();
            commune ??= fixture.CommuneId;
            var cabinet = new ElectricalCabinet
            {
                CabinetName = "feeder state probe", CommuneId = commune,
                DataSource = DataSource.Simulated, Geom = new Point(108.8, 12.8) { SRID = 4326 },
            };
            db.Add(cabinet); await db.SaveChangesAsync();
            var feeders = Enumerable.Range(0, count).Select(_ => new Feeder
            {
                CommuneId = commune, FeederName = "feeder state probe", CabinetId = cabinet.CabinetId,
                CabinetSource = TopologySource.Inferred,
            }).ToArray();
            db.AddRange(feeders); await db.SaveChangesAsync();
            // One batch shares created_at, so the API orders by length(id), id; EF does not draw sequence values in array order.
            feeders = [.. feeders.OrderBy(f => f.FeederId.Length).ThenBy(f => f.FeederId, StringComparer.Ordinal)];
            IotNode? node = null;
            if (count > 1)
            {
                node = new IotNode
                {
                    CabinetId = cabinet.CabinetId, CommuneId = commune, NodeRole = NodeRole.SegmentController,
                    DataSource = DataSource.Simulated, LastReportAt = DateTime.UtcNow,
                };
                db.Add(node); await db.SaveChangesAsync();
                db.Add(new FeederControl
                {
                    FeederId = feeders[0].FeederId, NodeId = node.NodeId, CommuneId = commune,
                    CabinetId = cabinet.CabinetId, RelayNo = 1,
                    ControlMode = mode switch { "on" => FeederControlMode.On, "off" => FeederControlMode.Off, "auto" => FeederControlMode.Auto, _ => null },
                    ModeReportedAt = mode is null ? null : DateTime.UtcNow,
                });
                await db.SaveChangesAsync();
            }
            return new Rig(cabinet.CabinetId, node?.NodeId, feeders.Select(f => f.FeederId).ToArray());
        });

    internal static JsonElement Cabinet(JsonElement map, string id) => map.GetProperty("features").EnumerateArray()
        .Single(f => f.GetProperty("properties").GetProperty("cabinet_id").GetString() == id).GetProperty("properties");

    internal static async Task<JsonElement> Json(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
    }
}
