using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LuxMap.Modules.Assets.Entities;
using LuxMap.Shared.Contracts.Enums;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;

namespace LuxMap.Api.Tests;

/// <summary>
/// LAMP-COUNT (drift LMC-1…LMC-4) — a double-arm pole is ONE fitting with <c>lamp_count = 2</c>, recorded at install,
/// defaulting to 1, bounded 1..4 by the API, the import and the database, and shown wherever <c>lamp_watt</c> is.
/// </summary>
[Collection(nameof(AssetDatabaseCollection))]
public sealed class FixtureLampCountTests(AssetImportFixture fixture)
{
    private const string FixtureRoute = "/api/v1/assets/fixtures";
    private const double Lng = 108.71;
    private const double Lat = 12.71;
    private const string Box = "?bbox=108.70,12.70,108.72,12.72";

    [Fact]
    public async Task A_double_arm_pole_shows_two_lamps_on_the_map_the_pole_detail_and_the_inventory()
    {
        var client = await fixture.ManagerClientAsync();
        var poleId = await NewPoleAsync();

        var created = await client.PostAsJsonAsync(FixtureRoute, LampBody(poleId, lampCount: 2));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var properties = (await GetAsync(client, "/api/v1/map/poles" + Box)).GetProperty("features").EnumerateArray()
            .Single(feature => feature.GetProperty("properties").GetProperty("pole_id").GetString() == poleId)
            .GetProperty("properties");
        Assert.Equal(2, properties.GetProperty("lamp_count").GetInt32());
        Assert.Equal(100, properties.GetProperty("lamp_watt").GetInt32());

        var detail = await GetAsync(client, $"/api/v1/map/poles/{poleId}");
        Assert.Equal(2, detail.GetProperty("fixture").GetProperty("lamp_count").GetInt32());

        var inventory = await InventoryRowAsync(client, poleId);
        Assert.Equal(2, inventory.GetProperty("active_fixture").GetProperty("lamp_count").GetInt32());
    }

    [Fact]
    public async Task Omitting_lamp_count_records_one_lamp_and_a_pole_without_a_lamp_shows_null()
    {
        var client = await fixture.ManagerClientAsync();
        var bare = await NewPoleAsync();
        var lit = await NewPoleAsync();

        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync(FixtureRoute, LampBody(lit))).StatusCode);

        var features = (await GetAsync(client, "/api/v1/map/poles" + Box)).GetProperty("features").EnumerateArray()
            .ToDictionary(feature => feature.GetProperty("properties").GetProperty("pole_id").GetString()!,
                feature => feature.GetProperty("properties"));
        Assert.Equal(1, features[lit].GetProperty("lamp_count").GetInt32());
        Assert.Equal(JsonValueKind.Null, features[bare].GetProperty("lamp_count").ValueKind);
        Assert.Equal(1, await fixture.QueryAsync(db => db.Set<Fixture>().IgnoreQueryFilters()
            .Where(lamp => lamp.PoleId == lit).Select(lamp => lamp.LampCount).SingleAsync()));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(60)]
    public async Task A_count_outside_one_to_four_is_400_and_nothing_is_written(int lampCount)
    {
        var client = await fixture.ManagerClientAsync();
        var poleId = await NewPoleAsync();

        var response = await client.PostAsJsonAsync(FixtureRoute, LampBody(poleId, lampCount: lampCount));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, await fixture.QueryAsync(db => db.Set<Fixture>().IgnoreQueryFilters().CountAsync(lamp => lamp.PoleId == poleId)));
    }

    [Fact]
    public async Task Import_reads_lamp_count_with_an_empty_cell_meaning_one_and_refuses_a_count_out_of_range()
    {
        var client = await fixture.ManagerClientAsync();
        var tag = $"L{Guid.NewGuid():N}"[..9].ToUpperInvariant();
        var two = await NewPoleAsync($"{tag}-2");
        var one = await NewPoleAsync($"{tag}-1");
        var bad = await NewPoleAsync($"{tag}-X");

        var result = await AssetImportTests.ImportAsync(client, "fixtures", "fixtures.csv",
            "pole_external_ref,fixture_type,power_source,lamp_watt,lamp_count,install_date,data_source"
            + $"\n{tag}-2,led_road_lamp,grid,100,2,2026-02-01,public_imagery"
            + $"\n{tag}-1,led_road_lamp,grid,100,,2026-02-01,public_imagery"
            + $"\n{tag}-X,led_road_lamp,grid,100,60,2026-02-01,public_imagery");

        Assert.Equal(2, result.GetProperty("inserted").GetInt32());
        Assert.Equal(1, result.GetProperty("failed").GetInt32());
        Assert.Equal("lamp_count", result.GetProperty("rows")[0].GetProperty("column").GetString());
        var counts = await fixture.QueryAsync(db => db.Set<Fixture>().IgnoreQueryFilters()
            .Where(lamp => lamp.PoleId == two || lamp.PoleId == one || lamp.PoleId == bad)
            .ToDictionaryAsync(lamp => lamp.PoleId, lamp => lamp.LampCount));
        Assert.Equal(2, counts[two]);
        Assert.Equal(1, counts[one]);
        Assert.False(counts.ContainsKey(bad));
    }

    /// <summary>The CHECK holds with no API in the way — a wattage typed into the count is refused by the database itself.</summary>
    [Fact]
    public async Task The_database_refuses_a_count_out_of_range_even_written_as_the_system()
    {
        var poleId = await NewPoleAsync();

        var error = await Assert.ThrowsAsync<DbUpdateException>(() => fixture.QueryAsync(async db =>
        {
            using (db.EnterUnscopedSystemWriteBackdoor())
            {
                db.Set<Fixture>().Add(new Fixture
                {
                    PoleId = poleId, CommuneId = fixture.CommuneId, FixtureType = FixtureType.LedRoadLamp,
                    PowerSource = PowerSource.Grid, LampWatt = 100, LampCount = 60,
                    InstallDate = new DateOnly(2026, 1, 4), DataSource = DataSource.PublicImagery,
                });
                return await db.SaveChangesAsync();
            }
        }));

        Assert.Contains("ck_fixture_lamp_count", error.InnerException?.Message);
    }

    private static object LampBody(string poleId, int? lampCount = null) => new
    {
        pole_id = poleId,
        fixture_type = "led_road_lamp",
        power_source = "grid",
        lamp_watt = 100,
        lamp_count = lampCount,
        install_date = "2026-01-04",
        data_source = "public_imagery",
    };

    private static async Task<JsonElement> GetAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
    }

    /// <summary>The inventory list has no pole filter; walk its pages (ordered by the ID rule) until the pole turns up.</summary>
    private static async Task<JsonElement> InventoryRowAsync(HttpClient client, string poleId)
    {
        for (var page = 1; ; page++)
        {
            var body = await GetAsync(client, $"/api/v1/assets/poles?page={page}&page_size=200");
            foreach (var item in body.GetProperty("items").EnumerateArray())
            {
                if (item.GetProperty("pole_id").GetString() == poleId)
                {
                    return item;
                }
            }

            if (page * 200 >= body.GetProperty("total").GetInt32())
            {
                throw new Xunit.Sdk.XunitException($"{poleId} is not in the inventory.");
            }
        }
    }

    private Task<string> NewPoleAsync(string? externalRef = null)
        => fixture.QueryAsync(async db =>
        {
            using (db.EnterUnscopedSystemWriteBackdoor())
            {
                var segment = new RoadSegment
                {
                    SegmentName = "lamp count probe",
                    RoadClass = RoadClass.InterVillage,
                    LengthM = 100,
                    Geom = new LineString([new Coordinate(Lng - 0.001, Lat), new Coordinate(Lng + 0.001, Lat)]) { SRID = 4326 },
                    CommuneId = fixture.CommuneId,
                    DataSource = DataSource.PublicImagery,
                };
                db.Set<RoadSegment>().Add(segment);
                await db.SaveChangesAsync();

                var pole = new Pole
                {
                    SegmentId = segment.SegmentId,
                    CommuneId = fixture.CommuneId,
                    ExternalRef = externalRef,
                    Geom = new Point(Lng, Lat) { SRID = 4326 },
                    DataSource = DataSource.PublicImagery,
                };
                db.Set<Pole>().Add(pole);
                await db.SaveChangesAsync();
                return pole.PoleId;
            }
        });
}
