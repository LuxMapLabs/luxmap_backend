using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LuxMap.Modules.Assets.Entities;
using Microsoft.EntityFrameworkCore;

namespace LuxMap.Api.Tests;

/// <summary>
/// POLE-GEOM-NULL PR 2b — the WRITE paths accept a pole that has no position yet: POST, PUT, the CSV / GeoJSON import, the
/// <c>missing_location</c> filter and the first position through <c>PUT …/location</c>. A malformed value must still be an error,
/// never "no position", and a pole that has a position never loses it.
/// </summary>
[Collection(nameof(AssetDatabaseCollection))]
public sealed class PoleWithoutLocationWriteTests(AssetImportFixture fixture)
{
    private const string Poles = "/api/v1/assets/poles";
    private const string Wkt = "POINT(109.7 13.7)";

    private LightingTestRig Rigs { get; } = new(fixture);

    private static string Tag() => $"L{Guid.NewGuid():N}"[..9].ToUpperInvariant();

    [Fact]
    public async Task Create_without_geom_wkt_or_with_null_makes_a_pole_with_no_position_and_a_blank_or_malformed_one_is_400()
    {
        var client = await fixture.ManagerClientAsync();

        var absent = await Post(client, new Dictionary<string, object?> { ["commune_id"] = fixture.CommuneId, ["data_source"] = "simulated" });
        var explicitNull = await Post(client, Create(wkt: null));
        Assert.All(new[] { absent, explicitNull }, response => Assert.Equal(HttpStatusCode.Created, response.StatusCode));
        foreach (var id in new[] { IdOf(absent), IdOf(explicitNull) })
        {
            Assert.False(await HasPosition(id));
            var detail = await Get(client, $"{Poles}/{id}");
            Assert.Equal(JsonValueKind.Null, detail.GetProperty("geom_wkt").ValueKind);
            Assert.Equal(JsonValueKind.Null, detail.GetProperty("pole").GetProperty("location").ValueKind);
        }

        foreach (var bad in new[] { "", "   ", "POINT(abc)", "LINESTRING(109 13, 109.1 13.1)" })
        {
            var refused = await Post(client, Create(wkt: bad));
            Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
            Assert.Equal("VALIDATION_FAILED", await ErrorCode(refused));
        }

        // A pole from the books may still be put on a road at once: sổ sách usually names the road.
        var road = await Rigs.NewSegmentAsync();
        var onRoad = await Post(client, Create(wkt: null, segment: road));
        Assert.Equal(HttpStatusCode.Created, onRoad.StatusCode);
        Assert.False(await HasPosition(IdOf(onRoad)));
        Assert.Equal(road, await StoredSegment(IdOf(onRoad)));
    }

    [Fact]
    public async Task Update_never_takes_a_position_away_and_gives_a_pole_without_one_its_first()
    {
        var client = await fixture.ManagerClientAsync();
        var located = IdOf(await Post(client, Create(wkt: Wkt)));
        var unlocated = IdOf(await Post(client, Create(wkt: null)));

        // A pole that has a position: absent or null geom_wkt is the same 400 as before, and nothing changes.
        foreach (var body in new[] { Update(wkt: null, includeWkt: false), Update(wkt: null) })
        {
            var refused = await Put(client, located, body);
            Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
            Assert.Equal("VALIDATION_FAILED", await ErrorCode(refused));
            Assert.True(await HasPosition(located));
        }

        // A pole without one stays without when the form leaves it out; a malformed WKT is still refused.
        Assert.True((await Put(client, unlocated, Update(wkt: null, includeWkt: false))).IsSuccessStatusCode);
        Assert.False(await HasPosition(unlocated));
        Assert.Equal(HttpStatusCode.BadRequest, (await Put(client, unlocated, Update(wkt: "POINT(abc)"))).StatusCode);
        Assert.False(await HasPosition(unlocated));

        Assert.True((await Put(client, unlocated, Update(wkt: Wkt))).IsSuccessStatusCode);
        Assert.True(await HasPosition(unlocated));
        Assert.Equal(JsonValueKind.String, (await Get(client, $"{Poles}/{unlocated}")).GetProperty("geom_wkt").ValueKind);
    }

    [Fact]
    public async Task The_missing_location_filter_counts_before_paging_combines_with_unassigned_segment_and_false_is_no_restriction()
    {
        var client = await fixture.ManagerClientAsync();
        var road = await Rigs.NewSegmentAsync();
        var (missingBefore, bothBefore, allBefore) = (await Total(client, "?missing_location=true"),
            await Total(client, "?missing_location=true&unassigned_segment=true"), await Total(client, ""));

        await Post(client, Create(wkt: null));                    // no position, no road
        await Post(client, Create(wkt: null, segment: road));     // no position, on a road
        await Post(client, Create(wkt: Wkt));                     // positioned

        Assert.Equal(missingBefore + 2, await Total(client, "?missing_location=true"));
        Assert.Equal(bothBefore + 1, await Total(client, "?missing_location=true&unassigned_segment=true"));
        Assert.Equal(allBefore + 3, await Total(client, ""));
        Assert.Equal(allBefore + 3, await Total(client, "?missing_location=false"));

        var page = await Get(client, $"{Poles}?missing_location=true&page_size=1");
        Assert.Equal(missingBefore + 2, page.GetProperty("total").GetInt32());
        Assert.Single(page.GetProperty("items").EnumerateArray());
        var wide = await Get(client, $"{Poles}?missing_location=true&page_size=200");
        Assert.All(wide.GetProperty("items").EnumerateArray(), item => Assert.Equal(JsonValueKind.Null, item.GetProperty("location").ValueKind));

        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"{Poles}?missing_location=maybe")).StatusCode);
    }

    [Fact]
    public async Task Import_leaves_a_blank_geom_cell_without_a_position_for_new_poles_keeps_the_position_of_existing_ones_and_rejects_a_malformed_one()
    {
        var client = await fixture.ManagerClientAsync();
        var tag = Tag();
        var commune = fixture.CommuneId;
        const string header = "external_ref,commune_id,geom_wkt,data_source";

        var first = await AssetImportTests.ImportAsync(client, "poles", "poles.csv", header
            + $"\n{tag}-P1,{commune},POINT(106.490 10.970),public_imagery"
            + $"\n{tag}-P2,{commune},,public_imagery"
            + $"\n{tag}-P3,{commune},POINT(abc),public_imagery");
        Assert.Equal(2, first.GetProperty("inserted").GetInt32());
        Assert.Equal(1, first.GetProperty("failed").GetInt32());
        var error = first.GetProperty("rows")[0];
        Assert.Equal("geom_wkt", error.GetProperty("column").GetString());
        var stored = await StoredPositions(tag);
        Assert.Equal(new[] { true, false }, new[] { stored[$"{tag}-P1"], stored[$"{tag}-P2"] });
        Assert.DoesNotContain($"{tag}-P3", stored.Keys);   // malformed is never "no position"

        // Re-import: P1 with a blank cell KEEPS its position; P2 gets one.
        var second = await AssetImportTests.ImportAsync(client, "poles", "poles.csv", header
            + $"\n{tag}-P1,{commune},,public_imagery"
            + $"\n{tag}-P2,{commune},POINT(106.491 10.971),public_imagery");
        Assert.Equal(0, second.GetProperty("failed").GetInt32());
        stored = await StoredPositions(tag);
        Assert.Equal(new[] { true, true }, new[] { stored[$"{tag}-P1"], stored[$"{tag}-P2"] });

        // A file with no geom_wkt column at all is a valid poles file now.
        var bare = await AssetImportTests.ImportAsync(client, "poles", "poles.csv",
            $"external_ref,commune_id,data_source\n{tag}-P4,{commune},public_imagery");
        Assert.Equal(1, bare.GetProperty("inserted").GetInt32());
        Assert.False((await StoredPositions(tag))[$"{tag}-P4"]);
    }

    [Fact]
    public async Task A_geojson_feature_with_no_or_null_geometry_imports_without_a_position_but_a_wrong_type_is_a_row_error()
    {
        var client = await fixture.ManagerClientAsync();
        var tag = Tag();
        var commune = fixture.CommuneId;
        await AssetImportTests.ImportAsync(client, "poles", "poles.csv",
            $"external_ref,commune_id,geom_wkt,data_source\n{tag}-E1,{commune},POINT(106.490 10.970),public_imagery");

        object Feature(string name, bool hasGeometry, object? geometry) => hasGeometry
            ? new { type = "Feature", geometry, properties = Props(name) }
            : (object)new { type = "Feature", properties = Props(name) };
        Dictionary<string, object?> Props(string name) => new()
        {
            ["external_ref"] = $"{tag}-{name}", ["commune_id"] = commune, ["data_source"] = "public_imagery",
        };

        var body = JsonSerializer.Serialize(new
        {
            type = "FeatureCollection",
            features = new[]
            {
                Feature("G1", false, null),                        // key absent
                Feature("G2", true, null),                         // explicit null
                Feature("E1", true, null),                         // an EXISTING pole: null geometry keeps its position
                Feature("B1", true, "POINT(106 10)"),              // a string is not a geometry object
                Feature("B2", true, new[] { 106.0, 10.0 }),        // neither is an array
            },
        });

        var result = await AssetImportTests.ImportAsync(client, "poles", "poles.geojson", body);

        Assert.Equal(2, result.GetProperty("failed").GetInt32());
        Assert.Equal(2, result.GetProperty("inserted").GetInt32());
        var stored = await StoredPositions(tag);
        Assert.Equal(new[] { false, false, true }, new[] { stored[$"{tag}-G1"], stored[$"{tag}-G2"], stored[$"{tag}-E1"] });
        Assert.DoesNotContain($"{tag}-B1", stored.Keys);
        Assert.DoesNotContain($"{tag}-B2", stored.Keys);
    }

    [Fact]
    public async Task A_pole_created_from_the_books_gets_its_first_position_from_the_field_engineer_inside_its_road_corridor()
    {
        var manager = await fixture.ManagerClientAsync();
        var road = await Rigs.NewSegmentAsync();                  // runs east from (109.5, 13.5)
        var pole = IdOf(await Post(manager, Create(wkt: null, segment: road)));
        var engineer = await fixture.FieldEngineerClientAsync();
        var url = $"{Poles}/{pole}/location";

        var off = await engineer.PutAsJsonAsync(url, new { lat = 13.5 + 60 / 110_600.0, lng = 109.5005 });
        Assert.Equal(HttpStatusCode.Conflict, off.StatusCode);
        Assert.False(await HasPosition(pole));

        var ok = await engineer.PutAsJsonAsync(url, new { lat = 13.5 + 5 / 110_600.0, lng = 109.5005 });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal(JsonValueKind.Null, JsonDocument.Parse(await ok.Content.ReadAsStringAsync()).RootElement.GetProperty("moved_m").ValueKind);
        Assert.True(await HasPosition(pole));

        // Now it is an ordinary positioned pole: it is on the map, and off the work list.
        var map = await Get(manager, "/api/v1/map/poles?bbox=109.499,13.499,109.502,13.501&data_source=simulated");
        Assert.Contains(map.GetProperty("features").EnumerateArray(), f => f.GetProperty("properties").GetProperty("pole_id").GetString() == pole);
        var work = await Get(manager, $"{Poles}?missing_location=true&page_size=200");
        Assert.DoesNotContain(work.GetProperty("items").EnumerateArray(), item => item.GetProperty("pole_id").GetString() == pole);
    }

    // ── helpers ────────────────────────────────────────────────────────────────────────

    private Dictionary<string, object?> Create(string? wkt, string? segment = null) => new()
    {
        ["commune_id"] = fixture.CommuneId, ["segment_id"] = segment, ["geom_wkt"] = wkt, ["data_source"] = "simulated",
    };

    private static Dictionary<string, object?> Update(string? wkt, bool includeWkt = true)
    {
        var body = new Dictionary<string, object?> { ["segment_id"] = null, ["data_source"] = "simulated" };
        if (includeWkt) body["geom_wkt"] = wkt;
        return body;
    }

    private static Task<HttpResponseMessage> Post(HttpClient client, object body) => client.PostAsJsonAsync(Poles, body);

    private static Task<HttpResponseMessage> Put(HttpClient client, string pole, object body) => client.PutAsJsonAsync($"{Poles}/{pole}", body);

    private static string IdOf(HttpResponseMessage created)
    {
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        return created.Headers.Location!.ToString().Split('/')[^1];
    }

    private static async Task<string> ErrorCode(HttpResponseMessage response)
        => JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("error").GetProperty("code").GetString()!;

    private static async Task<JsonElement> Get(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
    }

    private static async Task<int> Total(HttpClient client, string query) => (await Get(client, $"{Poles}{query}")).GetProperty("total").GetInt32();

    private Task<bool> HasPosition(string pole)
        => fixture.QueryAsync(db => db.Set<Pole>().IgnoreQueryFilters().AnyAsync(p => p.PoleId == pole && p.Geom != null));

    private Task<string?> StoredSegment(string pole)
        => fixture.QueryAsync(db => db.Set<Pole>().IgnoreQueryFilters().Where(p => p.PoleId == pole).Select(p => p.SegmentId).SingleAsync());

    private Task<Dictionary<string, bool>> StoredPositions(string tag)
        => fixture.QueryAsync(async db => await db.Set<Pole>().IgnoreQueryFilters().Where(p => p.ExternalRef!.StartsWith(tag))
            .ToDictionaryAsync(p => p.ExternalRef!, p => p.Geom != null));
}
