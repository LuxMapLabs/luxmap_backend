using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LuxMap.Modules.Assets.Entities;
using LuxMap.Shared.Contracts.Enums;
using LuxMap.Shared.Contracts.Errors;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;

namespace LuxMap.Api.Tests;

/// <summary>
/// BE-12b — what reading an asset returns, Contract section 5.3.
/// </summary>
/// <remarks>
/// <para>
/// These endpoints answered <c>PagedResult&lt;string&gt;</c> — ids only — from BE-12a until
/// 22/09/2026. The placeholder was held open while three questions were decided: does section 5.1's
/// ban on emitting <c>external_ref</c>, <c>data_source</c> and <c>feeder_id</c> bind the INVENTORY
/// surface too? It does not. Each of the three has a test here, because the decision is the kind
/// that gets quietly reversed by someone tidying up.
/// </para>
/// <para>
/// 🔴 <b>The inventory shape must stay disjoint from the map shape.</b> Section 5.1 is the
/// operational view — status, faults, sweeps. Two endpoints answering the same question with two
/// values is how they start disagreeing, and nothing notices the day they do.
/// </para>
/// </remarks>
[Collection(nameof(AssetDatabaseCollection))]
public sealed class AssetReadShapeTests(AssetImportFixture fixture)
{
    private const string Poles = "/api/v1/assets/poles";
    private const string Segments = "/api/v1/assets/segments";
    private const string Feeders = "/api/v1/assets/feeders";

    [Fact]
    public async Task A_pole_row_carries_the_inventory_fields_and_none_of_the_operational_ones()
    {
        var client = await fixture.ManagerClientAsync();
        var segmentId = await NewSegmentAsync(fixture.CommuneId);
        var poleId = await NewPoleAsync(fixture.CommuneId, segmentId, externalRef: Ref());

        var row = await FindAsync(client, $"{Poles}?page_size=200", "pole_id", poleId);
        var keys = row.EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.Ordinal);

        string[] expected =
        [
            "pole_id", "external_ref", "segment_id", "feeder_id", "commune_id", "data_source",
            "near_sensitive_poi", "location", "active_fixture", "updated_at",
        ];

        Assert.Equal([.. expected.Order(StringComparer.Ordinal)], [.. keys.Order(StringComparer.Ordinal)]);

        // The operational view's fields, which belong to Contract section 5.1 and must never appear
        // here. Listed by name rather than inferred, so adding one has to argue with a red test.
        foreach (var operational in new[]
                 {
                     "fixture_status", "status_confidence", "open_fault_count", "last_seen_at",
                     "last_sweep_id", "has_iot_node",
                 })
        {
            Assert.DoesNotContain(operational, keys);
        }
    }

    /// <summary>Q1 — <c>external_ref</c> IS emitted, although the map is forbidden to.</summary>
    /// <remarks>
    /// It is the authority's own inventory code and the one column an operator types during import.
    /// A stocktake screen that cannot show it gives them no way to match a row on screen to a row in
    /// their spreadsheet — which is the entire job of the screen.
    /// </remarks>
    [Fact]
    public async Task The_inventory_code_is_readable_on_all_three_resources()
    {
        var client = await fixture.ManagerClientAsync();
        string poleRef = Ref(), segmentRef = Ref(), feederRef = Ref();

        var segmentId = await NewSegmentAsync(fixture.CommuneId, segmentRef);
        var feederId = await NewFeederAsync(fixture.CommuneId, feederRef);
        var poleId = await NewPoleAsync(fixture.CommuneId, segmentId, externalRef: poleRef);

        Assert.Equal(poleRef,
            (await FindAsync(client, $"{Poles}?page_size=200", "pole_id", poleId))
                .GetProperty("external_ref").GetString());
        Assert.Equal(segmentRef,
            (await FindAsync(client, $"{Segments}?page_size=200", "segment_id", segmentId))
                .GetProperty("external_ref").GetString());
        Assert.Equal(feederRef,
            (await FindAsync(client, $"{Feeders}?page_size=200", "feeder_id", feederId))
                .GetProperty("external_ref").GetString());
    }

    /// <summary>Q2 — provenance is readable, and NOT invented for the entity that has none.</summary>
    /// <remarks>
    /// <c>data_source</c> separates Branch C's three data sources, and BE-12 made it writable. A
    /// field that can be written but not read is the condition for quietly mixing calibration data
    /// into real figures. <c>Feeder</c> is not one of the eight entities section 1.6 puts it on, so
    /// the feeder row does not carry it — answering a question the database cannot would be worse
    /// than the gap.
    /// </remarks>
    [Fact]
    public async Task Provenance_is_readable_where_it_exists_and_absent_where_it_does_not()
    {
        var client = await fixture.ManagerClientAsync();
        var segmentId = await NewSegmentAsync(fixture.CommuneId);
        var feederId = await NewFeederAsync(fixture.CommuneId);
        var poleId = await NewPoleAsync(fixture.CommuneId, segmentId, source: DataSource.CalibrationRig);

        Assert.Equal("calibration_rig",
            (await FindAsync(client, $"{Poles}?page_size=200", "pole_id", poleId))
                .GetProperty("data_source").GetString());

        var feeder = await FindAsync(client, $"{Feeders}?page_size=200", "feeder_id", feederId);
        Assert.False(feeder.TryGetProperty("data_source", out _));
    }

    /// <summary>Q3 — <c>feeder_id</c> is readable, because PUT is a full replacement.</summary>
    /// <remarks>
    /// <c>PUT /assets/poles/{id}</c> clears the circuit when the key is omitted — pinned by
    /// <c>Replacing_a_pole_without_a_feeder_id_clears_its_circuit</c>. An editor that cannot READ
    /// the current value therefore cannot preserve it, and would wipe circuits by simply saving a
    /// form it never showed.
    /// </remarks>
    [Fact]
    public async Task The_circuit_a_full_replacement_would_overwrite_is_readable()
    {
        var client = await fixture.ManagerClientAsync();
        var segmentId = await NewSegmentAsync(fixture.CommuneId);
        var feederId = await NewFeederAsync(fixture.CommuneId);
        var wired = await NewPoleAsync(fixture.CommuneId, segmentId, feederId: feederId);
        var loose = await NewPoleAsync(fixture.CommuneId, segmentId);

        Assert.Equal(feederId,
            (await FindAsync(client, $"{Poles}?page_size=200", "pole_id", wired))
                .GetProperty("feeder_id").GetString());
        Assert.Equal(JsonValueKind.Null,
            (await FindAsync(client, $"{Poles}?page_size=200", "pole_id", loose))
                .GetProperty("feeder_id").ValueKind);
    }

    /// <summary>
    /// The active lamp travels IN THE LIST, and it is the lamp in service.
    /// </summary>
    /// <remarks>
    /// In the list rather than only the detail because the inventory table shows lamp type and
    /// wattage per row; a boolean would make the screen fetch every pole separately to fill a
    /// visible column. The retired lamp planted here has a different wattage, so a version reading
    /// any lamp would return 100.
    /// </remarks>
    [Fact]
    public async Task The_lamp_in_service_travels_with_the_row_and_a_retired_one_does_not()
    {
        var client = await fixture.ManagerClientAsync();
        var segmentId = await NewSegmentAsync(fixture.CommuneId);
        var poleId = await NewPoleAsync(fixture.CommuneId, segmentId);

        await NewFixtureAsync(poleId, watt: 100, retired: true);
        await NewFixtureAsync(poleId, watt: 40, retired: false);

        var lamp = (await FindAsync(client, $"{Poles}?page_size=200", "pole_id", poleId))
            .GetProperty("active_fixture");

        Assert.Equal(40, lamp.GetProperty("lamp_watt").GetInt32());
        Assert.Equal("led_road_lamp", lamp.GetProperty("fixture_type").GetString());

        // Its OWN provenance: a lamp's can differ from its pole's.
        Assert.Equal("public_imagery", lamp.GetProperty("data_source").GetString());
    }

    [Fact]
    public async Task A_pole_with_no_lamp_reports_a_null_active_fixture_rather_than_an_empty_object()
    {
        var client = await fixture.ManagerClientAsync();
        var segmentId = await NewSegmentAsync(fixture.CommuneId);
        var poleId = await NewPoleAsync(fixture.CommuneId, segmentId);

        var row = await FindAsync(client, $"{Poles}?page_size=200", "pole_id", poleId);

        Assert.Equal(JsonValueKind.Null, row.GetProperty("active_fixture").ValueKind);
    }

    /// <summary><c>location</c>, not GeoJSON — this is a paginated list, not a map layer.</summary>
    /// <remarks>
    /// The <c>{lat, lng}</c> shape is already published for <c>GET /faults</c> (section 5.4).
    /// Reusing it beats inventing a second one for the same idea.
    /// </remarks>
    [Fact]
    public async Task A_pole_row_carries_a_plain_location_and_not_a_geojson_geometry()
    {
        var client = await fixture.ManagerClientAsync();
        var segmentId = await NewSegmentAsync(fixture.CommuneId);
        var poleId = await NewPoleAsync(fixture.CommuneId, segmentId);

        var row = await FindAsync(client, $"{Poles}?page_size=200", "pole_id", poleId);
        var location = row.GetProperty("location");

        Assert.Equal(Lat, location.GetProperty("lat").GetDouble(), 6);
        Assert.Equal(Lng, location.GetProperty("lng").GetDouble(), 6);
        Assert.False(row.TryGetProperty("geometry", out _));
        Assert.False(row.TryGetProperty("type", out _));
    }

    // ── Segments and feeders ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_segment_row_carries_its_declared_length_and_a_pole_count()
    {
        var client = await fixture.ManagerClientAsync();
        var segmentId = await NewSegmentAsync(fixture.CommuneId);
        await NewPoleAsync(fixture.CommuneId, segmentId);
        await NewPoleAsync(fixture.CommuneId, segmentId);

        var row = await FindAsync(client, $"{Segments}?page_size=200", "segment_id", segmentId);

        Assert.Equal(2, row.GetProperty("pole_count").GetInt32());
        Assert.Equal(250, row.GetProperty("length_m").GetDouble());
        Assert.Equal("inter_village", row.GetProperty("road_class").GetString());
    }

    /// <summary>
    /// 🔴 <c>pole_count</c> counts what THIS CALLER can see, not what the road carries.
    /// </summary>
    /// <remarks>
    /// An <c>inter_commune</c> road legitimately carries a neighbour's poles, and the query filter
    /// hides them. Reporting the true total would let a caller measure another commune's data
    /// without being able to list it — the probe Contract section 7 exists to prevent, and the same
    /// reasoning decision A used for <c>GET /faults?pole_id=</c>.
    /// </remarks>
    [Fact]
    public async Task A_segments_pole_count_stops_at_the_edge_of_the_callers_scope()
    {
        var client = await fixture.ManagerClientAsync();
        var segmentId = await NewSegmentAsync(fixture.CommuneId);
        await NewPoleAsync(fixture.CommuneId, segmentId);
        await NewPoleAsync(fixture.ForeignCommuneId, segmentId);

        var row = await FindAsync(client, $"{Segments}?page_size=200", "segment_id", segmentId);

        Assert.Equal(1, row.GetProperty("pole_count").GetInt32());
    }

    /// <summary>A feeder says WHETHER it has a cable route; the detail carries the route itself.</summary>
    [Fact]
    public async Task A_feeder_row_reports_whether_a_cable_route_was_surveyed()
    {
        var client = await fixture.ManagerClientAsync();
        var without = await NewFeederAsync(fixture.CommuneId);
        var with = await NewFeederAsync(fixture.CommuneId, withGeometry: true);

        Assert.False((await FindAsync(client, $"{Feeders}?page_size=200", "feeder_id", without))
            .GetProperty("has_geometry").GetBoolean());
        Assert.True((await FindAsync(client, $"{Feeders}?page_size=200", "feeder_id", with))
            .GetProperty("has_geometry").GetBoolean());
    }

    // ── Detail reads ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Reading_one_pole_adds_its_geometry_its_segment_name_and_when_it_was_created()
    {
        var client = await fixture.ManagerClientAsync();
        var segmentId = await NewSegmentAsync(fixture.CommuneId);
        var poleId = await NewPoleAsync(fixture.CommuneId, segmentId);

        var body = await GetAsync(client, $"{Poles}/{poleId}");

        Assert.Equal(poleId, body.GetProperty("pole").GetProperty("pole_id").GetString());
        Assert.Equal("read shape probe road", body.GetProperty("segment_name").GetString());
        Assert.Contains("POINT", body.GetProperty("geom_wkt").GetString()!, StringComparison.Ordinal);
        Assert.NotEqual(JsonValueKind.Null, body.GetProperty("created_at").ValueKind);
    }

    /// <summary>
    /// The WKT a detail read returns is the WKT a <c>PUT</c> body takes back.
    /// </summary>
    /// <remarks>
    /// Round-tripping matters more than the exact spelling: an editor that changes only a name has
    /// to hand the geometry back untouched, and it can only do that if the string it was given is
    /// one the write path accepts.
    /// </remarks>
    [Fact]
    public async Task The_geometry_a_detail_read_returns_is_accepted_straight_back_by_a_replacement()
    {
        var client = await fixture.ManagerClientAsync();
        var segmentId = await NewSegmentAsync(fixture.CommuneId);
        var poleId = await NewPoleAsync(fixture.CommuneId, segmentId);

        var wkt = (await GetAsync(client, $"{Poles}/{poleId}")).GetProperty("geom_wkt").GetString();

        var response = await client.PutAsJsonAsync($"{Poles}/{poleId}", new
        {
            segment_id = segmentId,
            geom_wkt = wkt,
            data_source = "public_imagery",
        });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task A_feeder_with_no_surveyed_route_reports_a_null_geometry_not_an_empty_string()
    {
        var client = await fixture.ManagerClientAsync();
        var feederId = await NewFeederAsync(fixture.CommuneId);

        var body = await GetAsync(client, $"{Feeders}/{feederId}");

        Assert.Equal(JsonValueKind.Null, body.GetProperty("geom_wkt").ValueKind);
    }

    [Theory]
    [InlineData("poles")]
    [InlineData("segments")]
    [InlineData("feeders")]
    public async Task An_asset_in_another_commune_is_not_found_rather_than_forbidden(string resource)
    {
        var client = await fixture.ManagerClientAsync();

        var id = resource switch
        {
            "poles" => await NewPoleAsync(
                fixture.ForeignCommuneId, await NewSegmentAsync(fixture.ForeignCommuneId)),
            "segments" => await NewSegmentAsync(fixture.ForeignCommuneId),
            _ => await NewFeederAsync(fixture.ForeignCommuneId),
        };

        var response = await client.GetAsync($"/api/v1/assets/{resource}/{id}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(
            ErrorCodes.AssetNotFound,
            (await ReadErrorAsync(response)).GetProperty("code").GetString());
    }

    /// <summary>
    /// 🔴 <c>/assets/feeders/poles</c> still reaches BE-13's unassigned listing, not this detail read.
    /// </summary>
    /// <remarks>
    /// The two routes are both two segments — <c>feeders/{feederId}</c> and <c>feeders/poles</c> —
    /// and ASP.NET Core resolves it by preferring the literal. That is correct but it is a
    /// coincidence of routing rules rather than a design, and BE-13's own proposal already calls
    /// this path its weakest part. Pinned so a rename of either route cannot silently swap which one
    /// answers, and so the collision is visible to whoever picks a better path.
    /// <para>
    /// The cost is real and accepted: a feeder whose id were literally <c>poles</c> could not be
    /// fetched. Ids are generated as <c>FDR-nnn</c>, so that cannot happen.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task The_literal_unassigned_route_still_wins_over_the_feeder_detail_route()
    {
        var client = await fixture.ManagerClientAsync();

        var unassigned = await client.GetAsync($"{Feeders}/poles?unassigned=true");
        Assert.Equal(HttpStatusCode.OK, unassigned.StatusCode);

        // The BE-13 listing, not a feeder: it answers with a paginated envelope of poles.
        var body = JsonDocument.Parse(await unassigned.Content.ReadAsStringAsync()).RootElement;
        Assert.True(body.TryGetProperty("items", out _));
        Assert.False(body.TryGetProperty("feeder", out _));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────────────────

    /// <summary>A unique inventory code per call.</summary>
    /// <remarks>
    /// ⚠️ Not a literal. <c>ux_*_commune_external_ref</c> is a partial UNIQUE index on
    /// <c>(commune_id, external_ref)</c>, so two tests in this class reusing the same readable code
    /// collide — and they did, passing alone and failing together, which is the worst way for it to
    /// show up.
    /// </remarks>
    private static string Ref() => $"REF-{Guid.NewGuid():N}"[..16];

    private const double Lng = 109.5;
    private const double Lat = 13.5;

    private static async Task<JsonElement> ReadErrorAsync(HttpResponseMessage response)
        => JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("error");

    private static async Task<JsonElement> GetAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
    }

    /// <summary>Walks the pages — this fixture's database is shared with every other asset test.</summary>
    private static async Task<JsonElement> FindAsync(
        HttpClient client, string url, string key, string id)
    {
        for (var page = 1; ; page++)
        {
            var body = await GetAsync(client, $"{url}&page={page}");
            var items = body.GetProperty("items").EnumerateArray().ToList();

            var match = items.FirstOrDefault(item => item.GetProperty(key).GetString() == id);
            if (match.ValueKind == JsonValueKind.Object)
            {
                return match.Clone();
            }

            Assert.True(items.Count > 0, $"{id} was not on any page of {url}.");
        }
    }

    private Task<string> NewSegmentAsync(string communeId, string? externalRef = null)
        => fixture.QueryAsync(async db =>
        {
            using (db.EnterUnscopedSystemWriteBackdoor())
            {
                var segment = new RoadSegment
                {
                    SegmentName = "read shape probe road",
                    RoadClass = RoadClass.InterVillage,
                    LengthM = 250,
                    Geom = new LineString([new Coordinate(Lng, Lat), new Coordinate(Lng + 0.01, Lat + 0.01)]) { SRID = 4326 },
                    CommuneId = communeId,
                    DataSource = DataSource.PublicImagery,
                    ExternalRef = externalRef,
                };

                db.Set<RoadSegment>().Add(segment);
                await db.SaveChangesAsync();
                return segment.SegmentId;
            }
        });

    private Task<string> NewFeederAsync(
        string communeId, string? externalRef = null, bool withGeometry = false)
        => fixture.QueryAsync(async db =>
        {
            using (db.EnterUnscopedSystemWriteBackdoor())
            {
                var feeder = new Feeder
                {
                    FeederName = "read shape probe cabinet",
                    CommuneId = communeId,
                    ExternalRef = externalRef,
                    Geom = withGeometry
                        ? new LineString([new Coordinate(Lng, Lat), new Coordinate(Lng + 0.01, Lat)]) { SRID = 4326 }
                        : null,
                };

                db.Set<Feeder>().Add(feeder);
                await db.SaveChangesAsync();
                return feeder.FeederId;
            }
        });

    private Task<string> NewPoleAsync(
        string communeId,
        string segmentId,
        string? feederId = null,
        string? externalRef = null,
        DataSource source = DataSource.PublicImagery)
        => fixture.QueryAsync(async db =>
        {
            using (db.EnterUnscopedSystemWriteBackdoor())
            {
                var pole = new Pole
                {
                    SegmentId = segmentId,
                    CommuneId = communeId,
                    FeederId = feederId,
                    ExternalRef = externalRef,
                    Geom = new Point(Lng, Lat) { SRID = 4326 },
                    DataSource = source,
                    NearSensitivePoi = true,
                };

                db.Set<Pole>().Add(pole);
                await db.SaveChangesAsync();
                return pole.PoleId;
            }
        });

    private Task NewFixtureAsync(string poleId, int watt, bool retired)
        => fixture.QueryAsync(async db =>
        {
            using (db.EnterUnscopedSystemWriteBackdoor())
            {
                var pole = await db.Set<Pole>().IgnoreQueryFilters().SingleAsync(p => p.PoleId == poleId);

                db.Set<Fixture>().Add(new Fixture
                {
                    PoleId = poleId,
                    CommuneId = pole.CommuneId,
                    FixtureType = FixtureType.LedRoadLamp,
                    PowerSource = PowerSource.Grid,
                    LampWatt = watt,
                    InstallDate = new DateOnly(2026, 1, 1),
                    RemovedDate = retired ? new DateOnly(2026, 6, 1) : null,
                    DataSource = DataSource.PublicImagery,
                });

                return await db.SaveChangesAsync();
            }
        });
}
