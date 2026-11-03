using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using LuxMap.Modules.Assets.Entities;
using Microsoft.EntityFrameworkCore;

namespace LuxMap.Api.Tests;

/// <summary>
/// POLE-SEGMENT-NULL PR 2e (D-2): the WRITE paths accept a pole that is on no segment — POST, PUT, the CSV / GeoJSON import and the
/// <c>unassigned_segment</c> filter. A typo must still be an error, never "no segment", and only PUT <c>null</c> takes a pole off its segment.
/// </summary>
[Collection(nameof(AssetDatabaseCollection))]
public sealed class PoleWithoutSegmentWriteTests(AssetImportFixture fixture)
{
    private const string Poles = "/api/v1/assets/poles";

    private static readonly string SegmentHeader = "external_ref,segment_name,road_class,length_m,geom_wkt,commune_id,data_source";
    private static readonly string PoleHeader = "external_ref,segment_external_ref,feeder_external_ref,commune_id,geom_wkt,near_sensitive_poi,data_source";

    private LightingTestRig Rigs { get; } = new(fixture);

    private static string Tag() => $"W{Guid.NewGuid():N}"[..9].ToUpperInvariant();

    [Fact]
    public async Task Create_without_a_segment_key_or_with_null_makes_an_unassigned_pole_and_a_blank_one_is_400()
    {
        var client = await fixture.ManagerClientAsync();

        var absent = await Post(client, new Dictionary<string, object?> { ["commune_id"] = fixture.CommuneId, ["geom_wkt"] = "POINT(109.7 13.7)", ["data_source"] = "simulated" });
        var explicitNull = await Post(client, Create(segment: null));
        Assert.All(new[] { absent, explicitNull }, response => Assert.Equal(HttpStatusCode.Created, response.StatusCode));
        foreach (var id in new[] { IdOf(absent), IdOf(explicitNull) })
        {
            Assert.Null(await StoredSegment(id));
            Assert.Equal(JsonValueKind.Null, (await Detail(client, id)).GetProperty("pole").GetProperty("segment_id").ValueKind);
        }

        foreach (var blank in new[] { "", "   " })
        {
            var refused = await Post(client, Create(segment: blank));
            Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
            Assert.Equal("VALIDATION_FAILED", await ErrorCode(refused));
        }

        var unknown = await Post(client, Create(segment: "SEG-9999999"));
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal("ASSET_NOT_FOUND", await ErrorCode(unknown));

        var road = await Rigs.NewSegmentAsync();
        var assigned = await Post(client, Create(segment: road));
        Assert.Equal(HttpStatusCode.Created, assigned.StatusCode);
        Assert.Equal(road, await StoredSegment(IdOf(assigned)));
    }

    [Fact]
    public async Task Update_requires_the_segment_key_and_only_an_explicit_null_takes_a_pole_off_its_segment()
    {
        var client = await fixture.ManagerClientAsync();
        var road = await Rigs.NewSegmentAsync();
        var other = await Rigs.NewSegmentAsync();
        var pole = IdOf(await Post(client, Create(segment: road)));

        // A form written before this change: no segment_id key at all. It must be refused, not read as "unassign".
        var withoutKey = await Put(client, pole, new Dictionary<string, object?> { ["geom_wkt"] = "POINT(109.7 13.7)", ["data_source"] = "simulated" });
        Assert.Equal(HttpStatusCode.BadRequest, withoutKey.StatusCode);
        Assert.Equal("VALIDATION_FAILED", await ErrorCode(withoutKey));
        Assert.Equal(road, await StoredSegment(pole));

        foreach (var invalid in new object[] { "", "   ", 123, true })
        {
            var refused = await Put(client, pole, Update(segment: invalid));
            Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
            Assert.Equal(road, await StoredSegment(pole));
        }

        Assert.Equal(HttpStatusCode.NotFound, (await Put(client, pole, Update(segment: "SEG-9999999"))).StatusCode);
        Assert.Equal(road, await StoredSegment(pole));

        Assert.True((await Put(client, pole, Update(segment: null))).IsSuccessStatusCode);
        Assert.Null(await StoredSegment(pole));
        Assert.Equal(JsonValueKind.Null, (await Detail(client, pole)).GetProperty("pole").GetProperty("segment_id").ValueKind);

        Assert.True((await Put(client, pole, Update(segment: other))).IsSuccessStatusCode);
        Assert.Equal(other, await StoredSegment(pole));
    }

    [Fact]
    public async Task The_unassigned_segment_filter_counts_before_paging_and_false_is_no_restriction()
    {
        var client = await fixture.ManagerClientAsync();
        var road = await Rigs.NewSegmentAsync();
        var (onlyBefore, allBefore) = (await Total(client, "?unassigned_segment=true"), await Total(client, ""));

        await Post(client, Create(segment: null));
        await Post(client, Create(segment: null));
        await Post(client, Create(segment: road));

        Assert.Equal(onlyBefore + 2, await Total(client, "?unassigned_segment=true"));
        Assert.Equal(allBefore + 3, await Total(client, ""));
        Assert.Equal(allBefore + 3, await Total(client, "?unassigned_segment=false"));

        var page = await Get(client, $"{Poles}?unassigned_segment=true&page_size=1");
        Assert.Equal(onlyBefore + 2, page.GetProperty("total").GetInt32());
        var only = Assert.Single(page.GetProperty("items").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, only.GetProperty("segment_id").ValueKind);

        var wide = await Get(client, $"{Poles}?unassigned_segment=true&page_size=200");
        Assert.All(wide.GetProperty("items").EnumerateArray(), item => Assert.Equal(JsonValueKind.Null, item.GetProperty("segment_id").ValueKind));

        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"{Poles}?unassigned_segment=maybe")).StatusCode);
    }

    [Fact]
    public async Task Import_leaves_a_blank_cell_unassigned_for_new_poles_keeps_the_segment_of_existing_ones_and_still_rejects_a_typo()
    {
        var client = await fixture.ManagerClientAsync();
        var tag = Tag();
        var commune = fixture.CommuneId;
        await AssetImportTests.ImportAsync(client, "segments", "segments.csv",
            SegmentHeader + $"\n{tag}-S1,Road,inter_village,100,\"LINESTRING(106.4900 10.9700, 106.4950 10.9705)\",{commune},public_imagery");
        var road = await fixture.QueryAsync(db => db.Set<RoadSegment>().IgnoreQueryFilters().Where(s => s.ExternalRef == $"{tag}-S1").Select(s => s.SegmentId).SingleAsync());

        var first = await AssetImportTests.ImportAsync(client, "poles", "poles.csv", PoleHeader
            + $"\n{tag}-P1,{tag}-S1,,{commune},POINT(106.490 10.970),false,public_imagery"
            + $"\n{tag}-P2,,,{commune},POINT(106.491 10.971),false,public_imagery"
            + $"\n{tag}-P3,{tag}-TYPO,,{commune},POINT(106.492 10.972),false,public_imagery");
        Assert.Equal(2, first.GetProperty("inserted").GetInt32());
        Assert.Equal(1, first.GetProperty("failed").GetInt32());
        var error = first.GetProperty("rows")[0];
        Assert.Equal("segment_external_ref", error.GetProperty("column").GetString());
        Assert.Contains("matches nothing", error.GetProperty("message").GetString()!);
        var stored = await Stored(tag);
        Assert.Equal(new[] { road, null }, new[] { stored[$"{tag}-P1"], stored[$"{tag}-P2"] });
        Assert.DoesNotContain($"{tag}-P3", stored.Keys);   // a typo is never "no segment"

        // Re-import: P1 with a blank cell keeps its segment (only PUT null unassigns); P2 is given one.
        var second = await AssetImportTests.ImportAsync(client, "poles", "poles.csv", PoleHeader
            + $"\n{tag}-P1,,,{commune},POINT(106.490 10.970),false,public_imagery"
            + $"\n{tag}-P2,{tag}-S1,,{commune},POINT(106.491 10.971),false,public_imagery");
        Assert.Equal(0, second.GetProperty("failed").GetInt32());
        Assert.Equal(0, second.GetProperty("inserted").GetInt32());
        Assert.Equal(1, second.GetProperty("updated").GetInt32());
        Assert.Equal(1, second.GetProperty("unchanged").GetInt32());
        stored = await Stored(tag);
        Assert.Equal(new[] { road, road }, new[] { stored[$"{tag}-P1"], stored[$"{tag}-P2"] });
    }

    [Fact]
    public async Task A_poles_file_without_the_segment_column_imports_unassigned_poles_and_reloads_unchanged()
    {
        var client = await fixture.ManagerClientAsync();
        var tag = Tag();
        var file = $"external_ref,commune_id,geom_wkt,data_source\n{tag}-P1,{fixture.CommuneId},POINT(106.490 10.970),public_imagery";

        var first = await AssetImportTests.ImportAsync(client, "poles", "poles.csv", file);
        Assert.Equal(1, first.GetProperty("inserted").GetInt32());
        Assert.Equal(0, first.GetProperty("failed").GetInt32());
        Assert.Null((await Stored(tag))[$"{tag}-P1"]);

        var again = await AssetImportTests.ImportAsync(client, "poles", "poles.csv", file);
        Assert.Equal(1, again.GetProperty("unchanged").GetInt32());
        Assert.Equal(0, again.GetProperty("inserted").GetInt32());
    }

    [Fact]
    public async Task A_geojson_feature_with_a_missing_or_null_segment_reference_imports_unassigned()
    {
        var client = await fixture.ManagerClientAsync();
        var tag = Tag();
        object Feature(string name, bool explicitNull, double lng)
        {
            var properties = new Dictionary<string, object?>
            {
                ["external_ref"] = $"{tag}-{name}", ["commune_id"] = fixture.CommuneId, ["data_source"] = "public_imagery",
            };
            if (explicitNull) properties["segment_external_ref"] = null;
            return new { type = "Feature", geometry = new { type = "Point", coordinates = new[] { lng, 10.97 } }, properties };
        }

        var body = JsonSerializer.Serialize(new { type = "FeatureCollection", features = new[] { Feature("G1", false, 106.49), Feature("G2", true, 106.491) } });

        var result = await AssetImportTests.ImportAsync(client, "poles", "poles.geojson", body);

        Assert.Equal(0, result.GetProperty("failed").GetInt32());
        Assert.Equal(2, result.GetProperty("inserted").GetInt32());
        var stored = await Stored(tag);
        Assert.Equal(new string?[] { null, null }, new[] { stored[$"{tag}-G1"], stored[$"{tag}-G2"] });
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────────────

    private Dictionary<string, object?> Create(string? segment) => new()
    {
        ["commune_id"] = fixture.CommuneId, ["segment_id"] = segment, ["geom_wkt"] = "POINT(109.7 13.7)", ["data_source"] = "simulated",
    };

    private static Dictionary<string, object?> Update(object? segment) => new()
    {
        ["segment_id"] = segment, ["geom_wkt"] = "POINT(109.7 13.7)", ["data_source"] = "simulated",
    };

    private static Task<HttpResponseMessage> Post(HttpClient client, object body) => client.PostAsJsonAsync(Poles, body);

    private static Task<HttpResponseMessage> Put(HttpClient client, string pole, object body) => client.PutAsJsonAsync($"{Poles}/{pole}", body);

    private static string IdOf(HttpResponseMessage created) => created.Headers.Location!.ToString().Split('/')[^1];

    private static async Task<string> ErrorCode(HttpResponseMessage response)
        => JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("error").GetProperty("code").GetString()!;

    private static async Task<JsonElement> Get(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
    }

    private static Task<JsonElement> Detail(HttpClient client, string pole) => Get(client, $"{Poles}/{pole}");

    private static async Task<int> Total(HttpClient client, string query) => (await Get(client, $"{Poles}{query}")).GetProperty("total").GetInt32();

    private Task<string?> StoredSegment(string pole)
        => fixture.QueryAsync(db => db.Set<Pole>().IgnoreQueryFilters().Where(p => p.PoleId == pole).Select(p => p.SegmentId).SingleAsync());

    private Task<Dictionary<string, string?>> Stored(string tag)
        => fixture.QueryAsync(async db => await db.Set<Pole>().IgnoreQueryFilters().Where(p => p.ExternalRef!.StartsWith(tag))
            .ToDictionaryAsync(p => p.ExternalRef!, p => p.SegmentId));
}
