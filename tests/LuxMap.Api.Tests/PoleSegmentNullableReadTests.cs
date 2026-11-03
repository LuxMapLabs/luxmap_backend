using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LuxMap.Modules.Assets.Entities;
using LuxMap.Shared.Contracts.Enums;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;

namespace LuxMap.Api.Tests;

[Collection(nameof(AssetDatabaseCollection))]
public sealed class PoleSegmentNullableReadTests(AssetImportFixture fixture)
{
    private LightingTestRig Rigs { get; } = new(fixture);

    [Fact]
    public async Task Unassigned_pole_remains_in_inventory_bbox_detail_and_feeder_lists()
    {
        var client = await fixture.ManagerClientAsync();
        var road = await Rigs.NewSegmentAsync();
        var feeder = await Rigs.NewFeederAsync(await Rigs.NewCabinetAsync());
        var before = await Get(client, "/api/v1/assets/poles?page_size=1");
        var assigned = await Pole(road, feeder);
        var unassigned = await Pole(null, feeder);
        var noFeeder = await Pole(null, null);
        var foreign = await Pole(null, null, foreign: true);
        var after = await Get(client, "/api/v1/assets/poles?page_size=1");
        Assert.Equal(before.GetProperty("total").GetInt32() + 3, after.GetProperty("total").GetInt32());
        Assert.Single(after.GetProperty("items").EnumerateArray());
        var inventory = await All(client, "/api/v1/assets/poles");
        var item = inventory.Single(p => Id(p) == unassigned);
        Null(item, "segment_id");
        Assert.Equal(Keys(inventory.Single(p => Id(p) == assigned)), Keys(item));
        Assert.DoesNotContain(inventory, p => Id(p) == foreign);
        var detail = await Get(client, $"/api/v1/assets/poles/{unassigned}");
        Null(detail, "segment_name");
        Null(detail.GetProperty("pole"), "segment_id");
        var wired = await All(client, $"/api/v1/assets/feeders/{feeder}/poles");
        Assert.Equal(new[] { assigned, unassigned }, wired.Select(Id));
        Null(wired[1], "segment_id");
        Assert.Equal(Keys(wired[0]), Keys(wired[1]));
        Assert.Equal(109.5, wired[1].GetProperty("lng").GetDouble());
        Assert.Equal(13.5, wired[1].GetProperty("lat").GetDouble());
        var noCircuit = await All(client, "/api/v1/assets/feeders/poles?unassigned=true");
        Null(noCircuit.Single(p => Id(p) == noFeeder), "segment_id");
        Assert.DoesNotContain(noCircuit, p => Id(p) == unassigned);
        var onRoad = await All(client, $"/api/v1/assets/segments/{road}/poles");
        Assert.Equal(assigned, Id(Assert.Single(onRoad)));

        var map = await Get(client, "/api/v1/map/poles?bbox=109.499,13.499,109.501,13.501&data_source=simulated");
        var feature = map.GetProperty("features").EnumerateArray().Single(f => Id(f.GetProperty("properties")) == unassigned);
        Assert.False(feature.TryGetProperty("id", out _));
        var properties = feature.GetProperty("properties");
        Null(properties, "segment_id");
        Assert.Equal("unknown", properties.GetProperty("fixture_status").GetString());
        Assert.Equal(new[] { 109.5, 13.5 }, feature.GetProperty("geometry").GetProperty("coordinates").EnumerateArray().Select(c => c.GetDouble()));
        Assert.DoesNotContain(map.GetProperty("features").EnumerateArray(), f => Id(f.GetProperty("properties")) == foreign);
        var mapDetail = await Get(client, $"/api/v1/map/poles/{unassigned}");
        Null(mapDetail, "segment_id");
        Null(mapDetail, "segment_name");
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/map/poles/{foreign}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/assets/poles/{foreign}")).StatusCode);
    }

    [Fact]
    public async Task Cabinet_topology_refuses_with_unassigned_poles_and_lists_them()
    {
        var client = await fixture.ManagerClientAsync();
        var cabinet = await Rigs.NewCabinetAsync();
        var feeder = await Rigs.NewFeederAsync(cabinet);
        var road = await Rigs.NewSegmentAsync();
        await Pole(road, feeder);
        Assert.Single((await Get(client, $"/api/v1/map/cabinets/{cabinet}/topology")).GetProperty("features").EnumerateArray());
        var ids = await Rigs.AsSystemAsync(async db =>
        {
            await using var tx = await db.Database.BeginTransactionAsync();
            var existing = await db.Set<Pole>().IgnoreQueryFilters().Select(p => p.PoleId).ToArrayAsync();
            // Choose a free width boundary above all live IDs; no sequence is rewound or changed.
            var digits = existing.Max(id => id.Length - "POLE-".Length) + 1;
            var boundary = (long)Math.Pow(10, digits);
            var now = DateTime.UtcNow.AddDays(-1);
            var poles = new[] { NewPole(null, feeder), NewPole(null, feeder) };
            poles[0].PoleId = $"POLE-{boundary - 1}";
            poles[1].PoleId = $"POLE-{boundary}";
            foreach (var pole in poles) pole.CreatedAt = now;
            db.AddRange(poles.Reverse()); // insertion/text order must not decide the response
            await db.SaveChangesAsync();
            await tx.CommitAsync();
            return poles.Select(p => p.PoleId).ToArray();
        });
        for (var i = 0; i < 100; i++) await Pole(null, feeder);
        var response = await client.GetAsync($"/api/v1/map/cabinets/{cabinet}/topology");
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var error = (await Json(response)).GetProperty("error");
        Assert.Equal("POLE_SEGMENT_REQUIRED", error.GetProperty("code").GetString());
        var details = error.GetProperty("details");
        Assert.Equal("cabinet_topology", details.GetProperty("operation").GetString());
        Assert.Equal(102, details.GetProperty("pole_count").GetInt32());
        var listed = details.GetProperty("pole_ids").EnumerateArray().Select(p => p.GetString()).ToArray();
        Assert.Equal(100, listed.Length);
        Assert.Equal(ids, listed.Take(2));
        var expected = await fixture.QueryAsync(db => db.Set<Pole>().IgnoreQueryFilters()
            .Where(p => p.FeederId == feeder && p.SegmentId == null).OrderBy(p => p.CreatedAt)
            .ThenBy(p => p.PoleId.Length).ThenBy(p => p.PoleId).Take(100).Select(p => p.PoleId).ToArrayAsync());
        Assert.Equal(expected, listed);
        var foreignClient = await fixture.ForeignCommuneClientAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await foreignClient.GetAsync($"/api/v1/map/cabinets/{cabinet}/topology")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/v1/map/cabinets/CAB-missing/topology")).StatusCode);
    }

    [Fact]
    public async Task Statistics_count_unassigned_separately_without_losing_unknown()
    {
        var client = await fixture.ManagerClientAsync();
        const string url = "/api/v1/statistics/fixture-status?data_source=public_imagery";
        var before = Assert.Single((await Get(client, url)).GetProperty("rows").EnumerateArray().DefaultIfEmpty());
        var baseline = before.ValueKind == JsonValueKind.Undefined ? 0 : before.GetProperty("pole_count").GetInt32();
        var baselineUnknown = before.ValueKind == JsonValueKind.Undefined ? 0 : before.GetProperty("unknown").GetInt32();
        var baselineUnassigned = before.ValueKind == JsonValueKind.Undefined ? 0 : before.GetProperty("unassigned_segment_pole_count").GetInt32();
        var baselineNever = before.ValueKind == JsonValueKind.Undefined ? 0 : before.GetProperty("never_surveyed").GetInt32();
        var road = await Rigs.NewSegmentAsync();
        var assigned = await Pole(road, null, source: DataSource.PublicImagery);
        var missing = await Pole(null, null, source: DataSource.PublicImagery);
        var unknown = await Pole(null, null, source: DataSource.PublicImagery);
        await Rigs.AsSystemAsync(async db =>
        {
            db.Add(new PoleCurrentStatus { PoleId = unknown, CommuneId = fixture.CommuneId, FixtureStatus = FixtureStatus.Unknown });
            return await db.SaveChangesAsync();
        });
        var total = Assert.Single((await Get(client, url)).GetProperty("rows").EnumerateArray());
        Assert.Equal(baseline + 3, total.GetProperty("pole_count").GetInt32());
        Assert.Equal(baselineUnknown + 3, total.GetProperty("unknown").GetInt32());
        Assert.Equal(baselineNever + 2, total.GetProperty("never_surveyed").GetInt32());
        Assert.Equal(baselineUnassigned + 2, total.GetProperty("unassigned_segment_pole_count").GetInt32());
        var rows = (await Get(client, url + "&group_by=commune,segment")).GetProperty("rows").EnumerateArray().ToArray();
        Null(rows[^1], "segment_id");
        var assignedRow = rows.Single(r => r.GetProperty("segment_id").GetString() == road);
        Assert.Equal(0, assignedRow.GetProperty("unassigned_segment_pole_count").GetInt32());
        Assert.Equal(1, assignedRow.GetProperty("pole_count").GetInt32());
        var filtered = Assert.Single((await Get(client, url + $"&segment_id={road}")).GetProperty("rows").EnumerateArray());
        Assert.Equal(1, filtered.GetProperty("pole_count").GetInt32());
        Assert.Equal(0, filtered.GetProperty("out").GetInt32());
        var defaultBefore = (await Get(client, "/api/v1/statistics/fixture-status")).GetRawText();
        await Pole(null, null, source: DataSource.CalibrationRig);
        var defaultAfter = await Get(client, "/api/v1/statistics/fixture-status");
        Assert.DoesNotContain(defaultAfter.GetProperty("rows").EnumerateArray(), r => r.GetProperty("data_source").GetString() == "calibration_rig");
        Assert.Equal(JsonDocument.Parse(defaultBefore).RootElement.GetProperty("rows").GetRawText(), defaultAfter.GetProperty("rows").GetRawText());
        var warranties = await All(client, "/api/v1/statistics/warranty?data_source=public_imagery");
        foreach (var id in new[] { missing, unknown }) Null(warranties.Single(p => Id(p) == id), "segment_id");
        Assert.Equal(road, warranties.Single(p => Id(p) == assigned).GetProperty("segment_id").GetString());
        var csv = await client.GetStringAsync("/api/v1/statistics/warranty?data_source=public_imagery&format=csv");
        Assert.Contains($"{missing},,{fixture.CommuneId},public_imagery,", csv, StringComparison.Ordinal);
        var statusCsv = await client.GetStringAsync(url + "&format=csv");
        Assert.EndsWith(",unassigned_segment_pole_count", statusCsv.TrimStart('\uFEFF').Split("\r\n")[0]);
    }

    [Fact]
    public async Task Qr_lookup_and_citizen_review_work_for_unassigned_pole()
    {
        var client = await fixture.ManagerClientAsync();
        var pole = await Pole(null, null);
        var issued = await client.PostAsJsonAsync("/api/v1/qr-codes", new { pole_id = pole });
        Assert.Equal(HttpStatusCode.Created, issued.StatusCode);
        var code = (await Json(issued)).GetProperty("code").GetString();
        var anonymous = fixture.CreateClient();
        var lookup = await anonymous.PostAsJsonAsync("/api/v1/public/qr/lookup", new { code });
        Assert.Equal(HttpStatusCode.OK, lookup.StatusCode);
        Null(await Json(lookup), "segment_name");
        using var form = new MultipartFormDataContent
        {
            { new StringContent(code!), "code" }, { new StringContent("lamp_out"), "kind" },
            { new StringContent(Guid.NewGuid().ToString()), "client_op_id" },
        };
        Assert.Equal(HttpStatusCode.Accepted, (await anonymous.PostAsync("/api/v1/public/reports", form)).StatusCode);
        var report = Assert.Single(await All(client, $"/api/v1/citizen-reports?pole_id={pole}"), r => r.GetProperty("pole_id").GetString() == pole);
        var id = report.GetProperty("report_id").GetString();
        var detail = await Get(client, $"/api/v1/citizen-reports/{id}");
        Null(detail.GetProperty("pole"), "segment_id");
        var foreign = await fixture.ForeignCommuneClientAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await foreign.GetAsync($"/api/v1/citizen-reports/{id}")).StatusCode);
    }

    [Fact]
    public async Task Lighting_handles_unassigned_poles_without_null_affected_ids()
    {
        var rig = await Rigs.RigAsync();
        var client = await fixture.ManagerClientAsync();
        await Pole(null, rig.Feeders[0]);
        await Pole(null, rig.Feeders[0]);
        var preview = await Get(client, $"/api/v1/lighting/preview?feeder_id={rig.Feeders[0]}");
        Assert.Equal(new[] { rig.Segment }, preview.GetProperty("affected_segment_ids").EnumerateArray().Select(s => s.GetString()));
        Assert.Equal(2, preview.GetProperty("unassigned_segment_pole_count").GetInt32());
        Assert.Equal(0, preview.GetProperty("uncontrollable_pole_count").GetInt32());
        var input = new { feeder_id = rig.Feeders[0], mode = "on", client_op_id = Guid.NewGuid() };
        var press = await client.PostAsJsonAsync("/api/v1/lighting/commands", input);
        Assert.Equal(HttpStatusCode.Accepted, press.StatusCode);
        var result = await Json(press);
        Assert.Equal(2, result.GetProperty("unassigned_segment_pole_count").GetInt32());
        Assert.Equal(0, result.GetProperty("uncontrollable_pole_count").GetInt32());
        Assert.Equal(new[] { rig.Segment }, result.GetProperty("affected_segment_ids").EnumerateArray().Select(s => s.GetString()));
        var replay = await client.PostAsJsonAsync("/api/v1/lighting/commands", input);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Null(await Json(replay), "unassigned_segment_pole_count");
        await Rigs.AsSystemAsync(async db =>
        {
            var poles = await db.Set<Pole>().IgnoreQueryFilters().Where(p => p.FeederId == rig.Feeders[0]).ToListAsync();
            foreach (var p in poles) p.SegmentId = null;
            return await db.SaveChangesAsync();
        });
        preview = await Get(client, $"/api/v1/lighting/preview?feeder_id={rig.Feeders[0]}");
        Assert.Empty(preview.GetProperty("affected_segment_ids").EnumerateArray());
        Assert.Equal(3, preview.GetProperty("unassigned_segment_pole_count").GetInt32());
        Assert.Equal(0, preview.GetProperty("uncontrollable_pole_count").GetInt32());
        var unwired = await Rigs.NewFeederAsync(rig.Cabinet);
        await Pole(null, unwired);
        preview = await Get(client, $"/api/v1/lighting/preview?feeder_id={unwired}");
        Assert.Equal(1, preview.GetProperty("uncontrollable_pole_count").GetInt32());
        Assert.Equal(0, preview.GetProperty("unassigned_segment_pole_count").GetInt32());
        Assert.Empty(preview.GetProperty("affected_segment_ids").EnumerateArray());
        var segmentPreview = await Get(client, $"/api/v1/lighting/preview?segment_id={rig.Segment}");
        Assert.Equal(0, segmentPreview.GetProperty("unassigned_segment_pole_count").GetInt32());
    }

    private Pole NewPole(string? segment, string? feeder, bool foreign = false, DataSource source = DataSource.Simulated) => new()
    {
        CommuneId = foreign ? fixture.ForeignCommuneId : fixture.CommuneId,
        SegmentId = segment, FeederId = feeder, FeederSource = feeder is null ? null : TopologySource.Inferred,
        DataSource = source, Geom = new Point(109.5, 13.5) { SRID = 4326 },
    };

    private Task<string> Pole(string? segment, string? feeder, bool foreign = false, DataSource source = DataSource.Simulated)
        => Rigs.AsSystemAsync(async db =>
        {
            var pole = NewPole(segment, feeder, foreign, source);
            db.Add(pole);
            await db.SaveChangesAsync();
            return pole.PoleId;
        });

    private static string? Id(JsonElement pole) => pole.GetProperty("pole_id").GetString();
    private static string[] Keys(JsonElement value) => value.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal).ToArray();
    private static void Null(JsonElement value, string key) => Assert.Equal(JsonValueKind.Null, value.GetProperty(key).ValueKind);
    private static async Task<JsonElement> Json(HttpResponseMessage response)
        => JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
    private static async Task<JsonElement> Get(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await Json(response);
    }
    private static async Task<JsonElement[]> All(HttpClient client, string url)
    {
        var items = new List<JsonElement>();
        var page = 1;
        while (true)
        {
            var body = await Get(client, url + (url.Contains('?') ? '&' : '?') + $"page={page++}&page_size=200");
            items.AddRange(body.GetProperty("items").EnumerateArray());
            if (items.Count >= body.GetProperty("total").GetInt32()) return items.ToArray();
        }
    }
}
