using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LuxMap.Modules.Assets.Entities;
using LuxMap.Shared.Contracts.Enums;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;

namespace LuxMap.Api.Tests;

/// <summary>
/// ROAD-SEGMENT-GEOM-NULL PR 2b — the WRITE paths accept a road segment that has no drawn route yet: POST, PUT, the CSV / GeoJSON
/// import, the <c>missing_geometry</c> filter and the first line drawn through <c>PUT</c>. A malformed value must still be an error,
/// never "no route", and a segment that has a route never loses it (RG-2).
/// </summary>
[Collection(nameof(AssetDatabaseCollection))]
public sealed class SegmentWithoutGeometryWriteTests(AssetImportFixture fixture)
{
    private const string Segments = "/api/v1/assets/segments";
    private const double Lng = 110.51;
    private const double Lat = 14.51;
    private const double DegreesPerMetre = 1 / 110_600.0;

    private LightingTestRig Rigs { get; } = new(fixture);

    private static string Tag() => $"R{Guid.NewGuid():N}"[..9].ToUpperInvariant();

    private static string Line(double lat = Lat) => FormattableString.Invariant($"LINESTRING({Lng} {lat}, {Lng + .01} {lat})");

    [Fact]
    public async Task Create_without_geom_wkt_or_with_null_makes_an_undrawn_segment_and_a_blank_or_malformed_one_is_400()
    {
        var client = await fixture.ManagerClientAsync();

        var absent = await Post(client, Create(includeWkt: false));
        var explicitNull = await Post(client, Create(wkt: null));
        foreach (var id in new[] { IdOf(absent), IdOf(explicitNull) })
        {
            Assert.False(await HasRoute(id));
            var detail = await Get(client, $"{Segments}/{id}");
            Assert.Equal(JsonValueKind.Null, detail.GetProperty("geom_wkt").ValueKind);
            Assert.False(detail.GetProperty("segment").GetProperty("has_geometry").GetBoolean());
        }

        foreach (var bad in new[] { "", "   ", "LINESTRING(abc)", "POINT(110 14)", "LINESTRING EMPTY" })
        {
            var refused = await Post(client, Create(wkt: bad));
            Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
            Assert.Equal("VALIDATION_FAILED", await ErrorCode(refused));
        }

        // The route that DOES arrive is stored as before.
        var drawn = IdOf(await Post(client, Create(wkt: Line())));
        Assert.True(await HasRoute(drawn));
        Assert.True((await Get(client, $"{Segments}/{drawn}")).GetProperty("segment").GetProperty("has_geometry").GetBoolean());
    }

    [Fact]
    public async Task Poles_can_be_assigned_to_an_undrawn_segment_on_create_and_on_update_with_no_corridor_to_measure()
    {
        var client = await fixture.ManagerClientAsync();
        var farOnCreate = await FreePole(offsetM: 5_000);
        var farOnUpdate = await FreePole(offsetM: 8_000);

        var created = IdOf(await Post(client, Create(wkt: null, poleIds: [farOnCreate])));
        Assert.Equal(created, await StoredSegment(farOnCreate));
        Assert.False(await HasRoute(created));

        var updated = await Put(client, created, Update(includeWkt: false, poleIds: [farOnCreate, farOnUpdate]));
        Assert.Equal(HttpStatusCode.NoContent, updated.StatusCode);
        Assert.Equal(created, await StoredSegment(farOnUpdate));
        Assert.False(await HasRoute(created));
    }

    [Fact]
    public async Task Update_never_takes_a_route_away_and_gives_a_segment_without_one_its_first_only_when_a_valid_line_arrives()
    {
        var client = await fixture.ManagerClientAsync();
        var drawn = IdOf(await Post(client, Create(wkt: Line())));
        var undrawn = IdOf(await Post(client, Create(wkt: null)));

        // A segment that has a route: absent or null geom_wkt is a 400, and the stored line is untouched.
        var before = (await Get(client, $"{Segments}/{drawn}")).GetProperty("geom_wkt").GetString();
        foreach (var body in new[] { Update(includeWkt: false), Update(wkt: null), Update(wkt: "") })
        {
            var refused = await Put(client, drawn, body);
            Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
            Assert.Equal("VALIDATION_FAILED", await ErrorCode(refused));
        }
        Assert.Equal(before, (await Get(client, $"{Segments}/{drawn}")).GetProperty("geom_wkt").GetString());

        // A segment without one stays without when the form leaves it out; a malformed or blank line is refused.
        Assert.Equal(HttpStatusCode.NoContent, (await Put(client, undrawn, Update(includeWkt: false))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Put(client, undrawn, Update(wkt: null))).StatusCode);
        foreach (var bad in new[] { "LINESTRING(abc)", "   ", "POINT(110 14)" })
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await Put(client, undrawn, Update(wkt: bad))).StatusCode);
        }
        Assert.False(await HasRoute(undrawn));

        Assert.Equal(HttpStatusCode.NoContent, (await Put(client, undrawn, Update(wkt: Line()))).StatusCode);
        Assert.True(await HasRoute(undrawn));
        Assert.Equal(JsonValueKind.String, (await Get(client, $"{Segments}/{undrawn}")).GetProperty("geom_wkt").ValueKind);
    }

    [Fact]
    public async Task Drawing_a_segment_for_the_first_time_is_refused_while_a_placed_pole_would_be_off_the_line_and_accepted_once_it_is_not()
    {
        var client = await fixture.ManagerClientAsync();
        var placed = await PlacedPole(northM: 100);     // 100 m north of the line drawn at Lat
        var created = IdOf(await Post(client, Create(wkt: null, poleIds: [placed])));
        var unplaced = await FreePole(offsetM: null);
        Assert.Equal(HttpStatusCode.NoContent, (await Put(client, created, Update(includeWkt: false, poleIds: [placed, unplaced]))).StatusCode);

        var off = await Put(client, created, Update(wkt: Line()));
        Assert.Equal(HttpStatusCode.Conflict, off.StatusCode);
        Assert.Equal("POLE_OFF_SEGMENT", await ErrorCode(off));
        Assert.False(await HasRoute(created));          // the whole PUT rolled back, the segment is still undrawn

        // A line that passes the pole is fine; the pole with no position was never measured.
        var near = await Put(client, created, Update(wkt: Line(Lat + 100 * DegreesPerMetre)));
        Assert.Equal(HttpStatusCode.NoContent, near.StatusCode);
        Assert.True(await HasRoute(created));
    }

    [Fact]
    public async Task The_missing_geometry_filter_counts_before_paging_combines_with_commune_id_and_false_is_no_restriction()
    {
        var client = await fixture.ManagerClientAsync();
        var (missingBefore, allBefore) = (await Total(client, "?missing_geometry=true"), await Total(client, ""));

        await Post(client, Create(wkt: null));
        await Post(client, Create(wkt: null));
        await Post(client, Create(wkt: Line()));

        Assert.Equal(missingBefore + 2, await Total(client, "?missing_geometry=true"));
        Assert.Equal(allBefore + 3, await Total(client, ""));
        Assert.Equal(allBefore + 3, await Total(client, "?missing_geometry=false"));
        Assert.Equal(missingBefore + 2, await Total(client, $"?missing_geometry=true&commune_id={fixture.CommuneId}"));

        var page = await Get(client, $"{Segments}?missing_geometry=true&page_size=1");
        Assert.Equal(missingBefore + 2, page.GetProperty("total").GetInt32());
        Assert.Single(page.GetProperty("items").EnumerateArray());
        var wide = await Get(client, $"{Segments}?missing_geometry=true&page_size=200");
        Assert.All(wide.GetProperty("items").EnumerateArray(), item => Assert.False(item.GetProperty("has_geometry").GetBoolean()));

        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"{Segments}?missing_geometry=maybe")).StatusCode);
    }

    [Fact]
    public async Task Import_leaves_a_blank_geom_cell_undrawn_for_new_segments_keeps_the_route_of_existing_ones_and_rejects_a_malformed_one()
    {
        var client = await fixture.ManagerClientAsync();
        var tag = Tag();
        var commune = fixture.CommuneId;
        const string header = "external_ref,segment_name,road_class,length_m,geom_wkt,commune_id,data_source";

        var first = await AssetImportTests.ImportAsync(client, "segments", "segments.csv", header
            + $"\n{tag}-S1,Drawn,inter_village,1000,\"{Line()}\",{commune},public_imagery"
            + $"\n{tag}-S2,Undrawn,inter_village,1000,,{commune},public_imagery"
            + $"\n{tag}-S3,Broken,inter_village,1000,LINESTRING(abc),{commune},public_imagery");
        Assert.Equal(2, first.GetProperty("inserted").GetInt32());
        Assert.Equal(1, first.GetProperty("failed").GetInt32());
        Assert.Equal("geom_wkt", first.GetProperty("rows")[0].GetProperty("column").GetString());
        var stored = await StoredRoutes(tag);
        Assert.Equal(new[] { true, false }, new[] { stored[$"{tag}-S1"], stored[$"{tag}-S2"] });
        Assert.DoesNotContain($"{tag}-S3", stored.Keys);   // malformed is never "no route"

        // Re-import: S1 with a blank cell KEEPS its line (and takes the new name); S2 gets one.
        var second = await AssetImportTests.ImportAsync(client, "segments", "segments.csv", header
            + $"\n{tag}-S1,Renamed,inter_village,1000,,{commune},public_imagery"
            + $"\n{tag}-S2,Undrawn,inter_village,1000,\"{Line(Lat + .01)}\",{commune},public_imagery");
        Assert.Equal(0, second.GetProperty("failed").GetInt32());
        stored = await StoredRoutes(tag);
        Assert.Equal(new[] { true, true }, new[] { stored[$"{tag}-S1"], stored[$"{tag}-S2"] });

        // A file with no geom_wkt column at all is a valid segments file now.
        var bare = await AssetImportTests.ImportAsync(client, "segments", "segments.csv",
            $"external_ref,segment_name,road_class,length_m,commune_id,data_source\n{tag}-S4,Bare,inter_village,500,{commune},public_imagery");
        Assert.Equal(1, bare.GetProperty("inserted").GetInt32());
        Assert.False((await StoredRoutes(tag))[$"{tag}-S4"]);
    }

    [Fact]
    public async Task A_geojson_feature_with_no_or_null_geometry_imports_undrawn_but_a_wrong_type_is_a_row_error()
    {
        var client = await fixture.ManagerClientAsync();
        var tag = Tag();
        var commune = fixture.CommuneId;
        await AssetImportTests.ImportAsync(client, "segments", "segments.csv",
            $"external_ref,segment_name,road_class,length_m,geom_wkt,commune_id,data_source\n{tag}-E1,Existing,inter_village,1000,\"{Line()}\",{commune},public_imagery");

        Dictionary<string, object?> Props(string name) => new()
        {
            ["external_ref"] = $"{tag}-{name}", ["segment_name"] = name, ["road_class"] = "inter_village", ["length_m"] = 800,
            ["commune_id"] = commune, ["data_source"] = "public_imagery",
        };
        object Feature(string name, bool hasGeometry, object? geometry) => hasGeometry
            ? new { type = "Feature", geometry, properties = Props(name) }
            : new { type = "Feature", properties = Props(name) };

        var body = JsonSerializer.Serialize(new
        {
            type = "FeatureCollection",
            features = new[]
            {
                Feature("G1", false, null),                                              // key absent
                Feature("G2", true, null),                                               // explicit null
                Feature("E1", true, null),                                               // an EXISTING segment: null geometry keeps its line
                Feature("B1", true, "LINESTRING(110 14, 110.1 14)"),                     // a string is not a geometry object
                Feature("B2", true, new { type = "Point", coordinates = new[] { 110.0, 14.0 } }),   // a Point is not a LineString
            },
        });

        var result = await AssetImportTests.ImportAsync(client, "segments", "segments.geojson", body);

        Assert.Equal(2, result.GetProperty("failed").GetInt32());
        Assert.Equal(2, result.GetProperty("inserted").GetInt32());
        var stored = await StoredRoutes(tag);
        Assert.Equal(new[] { false, false, true }, new[] { stored[$"{tag}-G1"], stored[$"{tag}-G2"], stored[$"{tag}-E1"] });
        Assert.DoesNotContain($"{tag}-B1", stored.Keys);
        Assert.DoesNotContain($"{tag}-B2", stored.Keys);
    }

    // ── helpers ────────────────────────────────────────────────────────────────────────

    private Dictionary<string, object?> Create(string? wkt = null, bool includeWkt = true, string[]? poleIds = null)
    {
        var body = new Dictionary<string, object?>
        {
            ["segment_name"] = "write probe road", ["road_class"] = "inter_village", ["length_m"] = 1000,
            ["commune_id"] = fixture.CommuneId, ["data_source"] = "simulated",
        };
        if (includeWkt) body["geom_wkt"] = wkt;
        if (poleIds is not null) body["pole_ids"] = poleIds;
        return body;
    }

    private static Dictionary<string, object?> Update(string? wkt = null, bool includeWkt = true, string[]? poleIds = null)
    {
        var body = new Dictionary<string, object?>
        {
            ["segment_name"] = "write probe road", ["road_class"] = "inter_village", ["length_m"] = 1000, ["data_source"] = "simulated",
        };
        if (includeWkt) body["geom_wkt"] = wkt;
        if (poleIds is not null) body["pole_ids"] = poleIds;
        return body;
    }

    /// <summary>A pole on no segment, due east of the origin by <paramref name="offsetM"/> metres, or with no position at all.</summary>
    private Task<string> FreePole(double? offsetM)
        => Rigs.AsSystemAsync(async db =>
        {
            var pole = new Pole
            {
                CommuneId = fixture.CommuneId, DataSource = DataSource.Simulated,
                Geom = offsetM is null ? null : new Point(Lng + offsetM.Value * DegreesPerMetre, Lat) { SRID = 4326 },
            };
            db.Add(pole);
            await db.SaveChangesAsync();
            return pole.PoleId;
        });

    /// <summary>A pole on no segment, in the middle of the span, <paramref name="northM"/> metres north of the line drawn at <see cref="Lat"/>.</summary>
    private Task<string> PlacedPole(double northM)
        => Rigs.AsSystemAsync(async db =>
        {
            var pole = new Pole
            {
                CommuneId = fixture.CommuneId, DataSource = DataSource.Simulated,
                Geom = new Point(Lng + .005, Lat + northM * DegreesPerMetre) { SRID = 4326 },
            };
            db.Add(pole);
            await db.SaveChangesAsync();
            return pole.PoleId;
        });

    private static Task<HttpResponseMessage> Post(HttpClient client, object body) => client.PostAsJsonAsync(Segments, body);

    private static Task<HttpResponseMessage> Put(HttpClient client, string segment, object body) => client.PutAsJsonAsync($"{Segments}/{segment}", body);

    private static string IdOf(HttpResponseMessage created)
    {
        Assert.True(created.StatusCode == HttpStatusCode.Created, $"{created.StatusCode}: {created.Content.ReadAsStringAsync().GetAwaiter().GetResult()}");
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

    private static async Task<int> Total(HttpClient client, string query) => (await Get(client, $"{Segments}{query}")).GetProperty("total").GetInt32();

    private Task<bool> HasRoute(string segment)
        => fixture.QueryAsync(db => db.Set<RoadSegment>().IgnoreQueryFilters().AnyAsync(s => s.SegmentId == segment && s.Geom != null));

    private Task<string?> StoredSegment(string pole)
        => fixture.QueryAsync(db => db.Set<Pole>().IgnoreQueryFilters().Where(p => p.PoleId == pole).Select(p => p.SegmentId).SingleAsync());

    private Task<Dictionary<string, bool>> StoredRoutes(string tag)
        => fixture.QueryAsync(async db => await db.Set<RoadSegment>().IgnoreQueryFilters().Where(s => s.ExternalRef!.StartsWith(tag))
            .ToDictionaryAsync(s => s.ExternalRef!, s => s.Geom != null));
}
