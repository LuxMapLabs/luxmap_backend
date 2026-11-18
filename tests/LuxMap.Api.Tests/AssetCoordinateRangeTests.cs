using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LuxMap.Modules.Assets.Entities;
using Microsoft.EntityFrameworkCore;

namespace LuxMap.Api.Tests;

/// <summary>
/// ASSET-GEOM-RANGE — every WKT / GeoJSON position an asset write accepts must be a real EPSG:4326 position. Neither WKTReader
/// nor PostGIS refuses <c>POINT(10.97 106.49)</c> (latitude 106°), so before this a pair typed latitude-first was stored.
/// The check lives in <c>AssetGeometry</c>, shared by POST / PUT, the CSV and GeoJSON imports and the workbook.
/// </summary>
[Collection(nameof(AssetDatabaseCollection))]
public sealed class AssetCoordinateRangeTests(AssetImportFixture fixture)
{
    private const string Poles = "/api/v1/assets/poles";

    private static string Tag() => $"R{Guid.NewGuid():N}"[..9].ToUpperInvariant();

    private Dictionary<string, object?> Create(string wkt) => new()
    {
        ["commune_id"] = fixture.CommuneId, ["geom_wkt"] = wkt, ["data_source"] = "simulated",
    };

    [Theory]
    [InlineData("POINT(10.97 106.49)", "looks swapped")]
    [InlineData("POINT(181 10)", "outside EPSG:4326")]
    [InlineData("POINT(106.49 -90.5)", "outside EPSG:4326")]
    [InlineData("POINT(NaN 10.97)", "finite")]
    public async Task Create_refuses_a_position_outside_EPSG_4326(string wkt, string reason)
    {
        var client = await fixture.ManagerClientAsync();
        var response = await client.PostAsJsonAsync(Poles, Create(wkt));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("VALIDATION_FAILED", body);
        Assert.Contains(reason, body);
    }

    [Theory]
    [InlineData("POINT(180 90)")]
    [InlineData("POINT(-180 -90)")]
    [InlineData("POINT(106.49 10.97)")]
    public async Task Create_accepts_the_edges_and_an_ordinary_position(string wkt)
    {
        var client = await fixture.ManagerClientAsync();
        var response = await client.PostAsJsonAsync(Poles, Create(wkt));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Csv_import_fails_only_the_row_with_a_swapped_pair_and_writes_nothing_for_it()
    {
        var client = await fixture.ManagerClientAsync();
        var tag = Tag();
        var commune = fixture.CommuneId;

        var result = await AssetImportTests.ImportAsync(client, "poles", "poles.csv",
            "external_ref,commune_id,geom_wkt,data_source"
            + $"\n{tag}-OK,{commune},POINT(106.490 10.970),public_imagery"
            + $"\n{tag}-SW,{commune},POINT(10.970 106.490),public_imagery");

        Assert.Equal(1, result.GetProperty("inserted").GetInt32());
        Assert.Equal(1, result.GetProperty("failed").GetInt32());
        var error = result.GetProperty("rows")[0];
        Assert.Equal(3, error.GetProperty("row").GetInt32());
        Assert.Equal("geom_wkt", error.GetProperty("column").GetString());
        Assert.Contains("looks swapped", error.GetProperty("message").GetString());
        Assert.Equal(new[] { $"{tag}-OK" }, await StoredPoleRefs(tag));
    }

    [Fact]
    public async Task One_vertex_out_of_range_fails_a_segment_route()
    {
        var client = await fixture.ManagerClientAsync();
        var tag = Tag();

        var result = await AssetImportTests.ImportAsync(client, "segments", "segments.csv",
            "external_ref,segment_name,road_class,length_m,geom_wkt,commune_id,data_source"
            + $"\n{tag}-S,Route,inter_village,100,\"LINESTRING(106.49 10.97, 106.50 91.0)\",{fixture.CommuneId},public_imagery");

        Assert.Equal(0, result.GetProperty("inserted").GetInt32());
        Assert.Equal("geom_wkt", result.GetProperty("rows")[0].GetProperty("column").GetString());
        Assert.Contains("outside EPSG:4326", result.GetProperty("rows")[0].GetProperty("message").GetString());
    }

    [Fact]
    public async Task GeoJson_import_refuses_a_swapped_position_too()
    {
        var client = await fixture.ManagerClientAsync();
        var tag = Tag();
        var feature = new
        {
            type = "FeatureCollection",
            features = new[]
            {
                new
                {
                    type = "Feature",
                    geometry = new { type = "Point", coordinates = new[] { 10.97, 106.49 } },
                    properties = new Dictionary<string, object?>
                    {
                        ["external_ref"] = $"{tag}-G", ["commune_id"] = fixture.CommuneId, ["data_source"] = "public_imagery",
                    },
                },
            },
        };

        var result = await AssetImportTests.ImportAsync(client, "poles", "poles.geojson", JsonSerializer.Serialize(feature));

        Assert.Equal(0, result.GetProperty("inserted").GetInt32());
        Assert.Contains("looks swapped", result.GetProperty("rows")[0].GetProperty("message").GetString());
        Assert.Empty(await StoredPoleRefs(tag));
    }

    private Task<string[]> StoredPoleRefs(string tag) => fixture.QueryAsync(db => db.Set<Pole>().IgnoreQueryFilters()
        .Where(pole => pole.ExternalRef!.StartsWith(tag)).Select(pole => pole.ExternalRef!).ToArrayAsync());
}
