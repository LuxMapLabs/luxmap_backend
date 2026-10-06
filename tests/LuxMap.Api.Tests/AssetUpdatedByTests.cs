using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LuxMap.Modules.Assets.Entities;
using LuxMap.Modules.Identity.Entities;
using Microsoft.EntityFrameworkCore;

namespace LuxMap.Api.Tests;

/// <summary>
/// Drift POLE-NOTE N-4 — every asset row records who last changed it (<c>updated_by</c>) beside when
/// (<c>updated_at</c>), and only a REAL change moves either: re-importing an identical file is not an edit.
/// </summary>
[Collection(nameof(AssetDatabaseCollection))]
public sealed class AssetUpdatedByTests(AssetImportFixture fixture)
{
    private const double Lng = 107.72;
    private const double Lat = 11.72;

    [Fact]
    public async Task Every_asset_a_manager_creates_names_them_as_its_last_editor()
    {
        var manager = await fixture.ManagerClientAsync();
        var line = string.Create(CultureInfo.InvariantCulture, $"LINESTRING ({Lng} {Lat}, {Lng + 0.01} {Lat + 0.01})");

        var segmentId = await CreatedIdAsync(await manager.PostAsJsonAsync("/api/v1/assets/segments", new
        {
            segment_name = "updated_by probe", road_class = "inter_village", length_m = 100, geom_wkt = line,
            commune_id = fixture.CommuneId, data_source = "public_imagery",
        }));
        var feederId = await CreatedIdAsync(await manager.PostAsJsonAsync("/api/v1/assets/feeders", new
        {
            feeder_name = "updated_by probe", commune_id = fixture.CommuneId,
        }));
        var poleId = await CreatedIdAsync(await manager.PostAsJsonAsync("/api/v1/assets/poles", new
        {
            segment_id = segmentId, feeder_id = feederId, commune_id = fixture.CommuneId,
            geom_wkt = string.Create(CultureInfo.InvariantCulture, $"POINT ({Lng} {Lat})"), data_source = "public_imagery",
        }));
        var fixtureId = await CreatedIdAsync(await manager.PostAsJsonAsync("/api/v1/assets/fixtures", new
        {
            pole_id = poleId, fixture_type = "led_road_lamp", power_source = "grid", lamp_watt = 100,
            install_date = "2026-01-04", data_source = "public_imagery",
        }));

        var managerId = await ManagerIdAsync();
        var stamps = await fixture.QueryAsync(async db => new[]
        {
            await db.Set<RoadSegment>().IgnoreQueryFilters().Where(x => x.SegmentId == segmentId).Select(x => x.UpdatedBy).SingleAsync(),
            await db.Set<Feeder>().IgnoreQueryFilters().Where(x => x.FeederId == feederId).Select(x => x.UpdatedBy).SingleAsync(),
            await db.Set<Pole>().IgnoreQueryFilters().Where(x => x.PoleId == poleId).Select(x => x.UpdatedBy).SingleAsync(),
            await db.Set<Fixture>().IgnoreQueryFilters().Where(x => x.FixtureId == fixtureId).Select(x => x.UpdatedBy).SingleAsync(),
        });
        Assert.All(stamps, stamp => Assert.Equal(managerId, stamp));

        foreach (var (url, key) in new[]
                 {
                     ($"/api/v1/assets/segments/{segmentId}", "segment"), ($"/api/v1/assets/feeders/{feederId}", "feeder"),
                     ($"/api/v1/assets/poles/{poleId}", "pole"),
                 })
        {
            var row = (await Json(await manager.GetAsync(url))).GetProperty(key);
            Assert.Equal(managerId, row.GetProperty("updated_by").GetString());
            Assert.Equal("BE-12a commune-scoped manager", row.GetProperty("updated_by_name").GetString());
        }
    }

    /// <summary>
    /// Re-importing the same file must leave <c>updated_at</c> and <c>updated_by</c> as they were — geometry
    /// included, which is re-parsed from WKT into a new object every time — and count the rows as unchanged.
    /// </summary>
    [Fact]
    public async Task Re_importing_an_identical_file_changes_nothing_and_a_changed_row_names_the_importer()
    {
        var manager = await fixture.ManagerClientAsync();
        var tag = $"U{Guid.NewGuid():N}"[..9].ToUpperInvariant();
        string Segments(string secondName) => "external_ref,segment_name,road_class,length_m,geom_wkt,commune_id,data_source"
            + string.Create(CultureInfo.InvariantCulture,
                $"\n{tag}-S1,Tuyen mot,inter_village,100,\"LINESTRING({Lng} {Lat}, {Lng + 0.01} {Lat + 0.01})\",{fixture.CommuneId},public_imagery"
                + $"\n{tag}-S2,{secondName},inter_village,200,\"LINESTRING({Lng} {Lat + 0.1}, {Lng + 0.01} {Lat + 0.11})\",{fixture.CommuneId},public_imagery");
        var poles = "external_ref,segment_external_ref,commune_id,geom_wkt,near_sensitive_poi,data_source"
            + string.Create(CultureInfo.InvariantCulture,
                $"\n{tag}-P1,{tag}-S1,{fixture.CommuneId},POINT({Lng} {Lat}),true,public_imagery");

        await AssetImportTests.ImportAsync(manager, "segments", "segments.csv", Segments("Tuyen hai"));
        await AssetImportTests.ImportAsync(manager, "poles", "poles.csv", poles);
        var before = await StampsAsync(tag);

        var sameSegments = await AssetImportTests.ImportAsync(manager, "segments", "segments.csv", Segments("Tuyen hai"));
        var samePoles = await AssetImportTests.ImportAsync(manager, "poles", "poles.csv", poles);
        Assert.Equal((0, 2), (sameSegments.GetProperty("updated").GetInt32(), sameSegments.GetProperty("unchanged").GetInt32()));
        Assert.Equal((0, 1), (samePoles.GetProperty("updated").GetInt32(), samePoles.GetProperty("unchanged").GetInt32()));
        Assert.Equal(before, await StampsAsync(tag));

        var renamed = await AssetImportTests.ImportAsync(manager, "segments", "segments.csv", Segments("Tuyen hai - doi ten"));
        Assert.Equal((1, 1), (renamed.GetProperty("updated").GetInt32(), renamed.GetProperty("unchanged").GetInt32()));
        var after = await StampsAsync(tag);
        Assert.Equal(before[$"{tag}-S1"], after[$"{tag}-S1"]);
        Assert.True(after[$"{tag}-S2"].UpdatedAt > before[$"{tag}-S2"].UpdatedAt);
        Assert.Equal(await ManagerIdAsync(), after[$"{tag}-S2"].UpdatedBy);
    }

    private Task<Dictionary<string, (string? UpdatedBy, DateTime UpdatedAt)>> StampsAsync(string tag)
        => fixture.QueryAsync(async db =>
        {
            var segments = await db.Set<RoadSegment>().IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.ExternalRef!.StartsWith(tag)).Select(x => new { x.ExternalRef, x.UpdatedBy, x.UpdatedAt }).ToListAsync();
            var poles = await db.Set<Pole>().IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.ExternalRef!.StartsWith(tag)).Select(x => new { x.ExternalRef, x.UpdatedBy, x.UpdatedAt }).ToListAsync();
            return segments.Concat(poles).ToDictionary(x => x.ExternalRef!, x => (x.UpdatedBy, x.UpdatedAt));
        });

    private Task<string> ManagerIdAsync()
        => fixture.QueryAsync(db => db.Set<AppUser>().Where(u => u.Username == fixture.ManagerUsername).Select(u => u.UserId).SingleAsync());

    private static async Task<string> CreatedIdAsync(HttpResponseMessage response)
    {
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return response.Headers.Location!.ToString().Split('/').Last();
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response)
        => JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
}
