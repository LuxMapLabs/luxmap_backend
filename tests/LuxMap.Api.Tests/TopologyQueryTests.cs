using System.Net;
using System.Text.Json;
using LuxMap.Modules.Assets.Entities;
using LuxMap.Shared.Contracts.Enums;
using LuxMap.Shared.Contracts.Errors;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;

namespace LuxMap.Api.Tests;

/// <summary>
/// BE-13 — reading the topology: which poles hang off a circuit, which sit on a road, which are on
/// no circuit at all.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ <b>PROVISIONAL SHAPE.</b> The Contract specifies no topology endpoint; this follows the
/// proposal in <c>docs/review/BE-13-topology-shape.md</c>, registered as drift 46, and it is not
/// stable until the next FW confirms it. These tests pin the behaviour that survives any of the
/// shapes under discussion — commune scoping, the 404-not-403 rule, where power_source comes from —
/// and only incidentally the route spellings.
/// </para>
/// <para>
/// None of this needs O-6. The mock set carries no circuit at all, so every test builds its own
/// cabinet and wires its own poles; O-6 blocks CV-15 having real data to cluster, not this code
/// having something to answer.
/// </para>
/// </remarks>
[Collection(nameof(AssetDatabaseCollection))]
public sealed class TopologyQueryTests(AssetImportFixture fixture)
{
    private const string Feeders = "/api/v1/assets/feeders";
    private const string Segments = "/api/v1/assets/segments";
    private const string Unassigned = "/api/v1/assets/feeders/poles?unassigned=true";

    [Fact]
    public async Task A_circuit_reports_the_poles_wired_to_it_and_nobody_elses()
    {
        var client = await fixture.AdminClientAsync();
        var segmentId = await NewSegmentAsync(fixture.CommuneId);
        var mine = await NewFeederAsync(fixture.CommuneId);
        var theirs = await NewFeederAsync(fixture.CommuneId);

        var wired = await NewPoleAsync(fixture.CommuneId, segmentId, mine);
        await NewPoleAsync(fixture.CommuneId, segmentId, theirs);
        await NewPoleAsync(fixture.CommuneId, segmentId, feederId: null);

        var page = await ReadPageAsync(client, $"{Feeders}/{mine}/poles");

        Assert.Equal(1, page.GetProperty("total").GetInt32());
        var item = page.GetProperty("items")[0];
        Assert.Equal(wired, item.GetProperty("pole_id").GetString());
        Assert.Equal(segmentId, item.GetProperty("segment_id").GetString());
        Assert.Equal(mine, item.GetProperty("feeder_id").GetString());
    }

    /// <summary>
    /// Coordinates travel WITH the list, which is the whole reason the item is not just an id.
    /// </summary>
    /// <remarks>
    /// CV-15 clusters along the circuit in space. An id-only list would make it fetch each pole
    /// separately — 46 extra requests for one real feeder — which is the anti-pattern CLAUDE.md names
    /// for the pole detail screen, and the reason does not change when the caller is an engine.
    /// EPSG:4326: 3405 exists only inside the SQL tree (BE-10, rule 3).
    /// </remarks>
    [Fact]
    public async Task Each_pole_carries_its_own_coordinates_in_4326()
    {
        var client = await fixture.AdminClientAsync();
        var segmentId = await NewSegmentAsync(fixture.CommuneId);
        var feederId = await NewFeederAsync(fixture.CommuneId);
        await NewPoleAsync(fixture.CommuneId, segmentId, feederId, lng: 106.4912, lat: 10.9734);

        var item = (await ReadPageAsync(client, $"{Feeders}/{feederId}/poles")).GetProperty("items")[0];

        Assert.Equal(10.9734, item.GetProperty("lat").GetDouble(), 6);
        Assert.Equal(106.4912, item.GetProperty("lng").GetDouble(), 6);
    }

    /// <summary>
    /// A feeder in another commune is <b>404</b>, not 403 and not an empty page.
    /// </summary>
    /// <remarks>
    /// The query filter makes the row not exist for this caller, and Contract section 7 wants absence
    /// rather than a refusal. An empty 200 would be worse than either: it turns the endpoint into a
    /// probe for whether an id is real in some other commune, which is exactly what decision A
    /// settled for <c>GET /faults?pole_id=</c>.
    /// </remarks>
    [Fact]
    public async Task A_circuit_in_another_commune_is_not_found_rather_than_forbidden()
    {
        var client = await fixture.AdminClientAsync();
        var foreign = await NewFeederAsync(fixture.ForeignCommuneId);

        var response = await client.GetAsync($"{Feeders}/{foreign}/poles");
        var error = await ReadErrorAsync(response);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(ErrorCodes.AssetNotFound, error.GetProperty("code").GetString());
    }

    /// <summary>
    /// A road MAY carry poles from another commune, and the listing must show them.
    /// </summary>
    /// <remarks>
    /// <c>road_class = inter_commune</c> means the road runs BETWEEN communes (BE-REVIEW-02,
    /// constraint 1), so this is correct data rather than a leak. The asymmetry with the feeder rule
    /// is the design, and this test is what stops someone "fixing" the segment path for symmetry.
    /// The caller here holds both communes; a single-commune caller would still only see their own,
    /// because the filter applies to <c>pole</c> on its own.
    /// </remarks>
    [Fact]
    public async Task A_road_may_carry_poles_from_another_commune_and_they_are_listed()
    {
        var client = await fixture.BothCommunesClientAsync();
        var segmentId = await NewSegmentAsync(fixture.CommuneId);

        var here = await NewPoleAsync(fixture.CommuneId, segmentId, feederId: null);
        var across = await NewPoleAsync(fixture.ForeignCommuneId, segmentId, feederId: null);

        var page = await ReadPageAsync(client, $"{Segments}/{segmentId}/poles");
        var ids = page.GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("pole_id").GetString()).ToList();

        Assert.Equal(2, page.GetProperty("total").GetInt32());
        Assert.Contains(here, ids);
        Assert.Contains(across, ids);
    }

    /// <summary>
    /// The unassigned listing carries <c>power_source</c>, and it comes from the ACTIVE lamp.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the field that separates "solar, so it has no circuit" from "nobody has assigned it
    /// yet". Both are <c>feeder_id = NULL</c> in the database and identical without it, and CV-05 has
    /// to tell them apart to know what work is left.
    /// </para>
    /// <para>
    /// Reading it from the ACTIVE lamp is not arbitrary: BE-REVIEW-02 constraint 3 made that lamp
    /// unique per pole, so there is exactly one to read and no aggregation rule to invent. The
    /// retired lamp planted here has the OPPOSITE power source, so a version that read any lamp
    /// would return grid and this test would fail.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task Power_source_is_read_from_the_lamp_in_service_not_a_retired_one()
    {
        var client = await fixture.AdminClientAsync();
        var segmentId = await NewSegmentAsync(fixture.CommuneId);
        var poleId = await NewPoleAsync(fixture.CommuneId, segmentId, feederId: null);

        await AddFixtureAsync(poleId, PowerSource.Grid, retired: true);
        await AddFixtureAsync(poleId, PowerSource.Solar, retired: false);

        var item = await FindPoleAsync(client, Unassigned, poleId);

        Assert.Equal("solar", item.GetProperty("power_source").GetString());
        Assert.Equal(JsonValueKind.Null, item.GetProperty("feeder_id").ValueKind);
    }

    /// <summary>A pole with no lamp at all reports null rather than a guess.</summary>
    [Fact]
    public async Task A_pole_with_no_lamp_reports_no_power_source()
    {
        var client = await fixture.AdminClientAsync();
        var segmentId = await NewSegmentAsync(fixture.CommuneId);
        var poleId = await NewPoleAsync(fixture.CommuneId, segmentId, feederId: null);

        var item = await FindPoleAsync(client, Unassigned, poleId);

        Assert.Equal(JsonValueKind.Null, item.GetProperty("power_source").ValueKind);
    }

    /// <summary>A pole that HAS a circuit is absent from the unassigned listing.</summary>
    [Fact]
    public async Task A_pole_already_on_a_circuit_is_not_listed_as_unassigned()
    {
        var client = await fixture.AdminClientAsync();
        var segmentId = await NewSegmentAsync(fixture.CommuneId);
        var feederId = await NewFeederAsync(fixture.CommuneId);
        var poleId = await NewPoleAsync(fixture.CommuneId, segmentId, feederId);

        var ids = (await ReadAllAsync(client, Unassigned)).Select(i => i.GetProperty("pole_id").GetString());

        Assert.DoesNotContain(poleId, ids);
    }

    /// <summary>
    /// The bare path without <c>unassigned=true</c> is a 400, deliberately.
    /// </summary>
    /// <remarks>
    /// Section 4 of the proposal says this route is the weakest part of it and asks the reviewer to
    /// choose a better one. Refusing the bare path keeps that door open: nothing can start depending
    /// on <c>/assets/feeders/poles</c> meaning something on its own, so replacing it later does not
    /// silently change what an existing caller gets back.
    /// </remarks>
    [Fact]
    public async Task The_unassigned_listing_refuses_to_answer_without_the_flag()
    {
        var client = await fixture.AdminClientAsync();

        var response = await client.GetAsync($"{Feeders}/poles");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            ErrorCodes.ValidationFailed,
            (await ReadErrorAsync(response)).GetProperty("code").GetString());
    }

    /// <summary>
    /// A maintenance engineer MAY read the topology — no GET here carries a role policy.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A policy is one EXACT role rather than a rank, so adding one would lock out two of the four
    /// roles while looking like a security improvement (BE-12a, rule 4).
    /// </para>
    /// <para>
    /// ⚠️ Asserted differently on the two route shapes, and the difference is worth knowing. The
    /// collection route answers <b>200</b> for anyone logged in, like the BE-12a listings. The
    /// <c>{id}</c> routes read the parent first, so this engineer — scoped to neither test commune —
    /// gets <b>404</b>, and 404 is itself the proof: a role policy refuses at the endpoint, before
    /// the action runs, and would have produced 403 <c>ROLE_FORBIDDEN</c> instead (BE-REVIEW-02,
    /// constraint 6). Reaching the not-found means the request got past authorization.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_maintenance_engineer_may_read_the_topology()
    {
        var client = await fixture.SeededClientAsync("engineer", "SEED_ENGINEER_PASSWORD");
        var segmentId = await NewSegmentAsync(fixture.CommuneId);
        var feederId = await NewFeederAsync(fixture.CommuneId);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(Unassigned)).StatusCode);

        foreach (var url in new[] { $"{Feeders}/{feederId}/poles", $"{Segments}/{segmentId}/poles" })
        {
            var response = await client.GetAsync(url);

            Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal(
                ErrorCodes.AssetNotFound,
                (await ReadErrorAsync(response)).GetProperty("code").GetString());
        }
    }

    /// <summary>
    /// Paging works, and the order is NOT the display id.
    /// </summary>
    /// <remarks>
    /// Ordering by <c>pole_id</c> would be wrong the day the table passes 9999: the width is a
    /// MINIMUM, so <c>POLE-10000</c> sorts before <c>POLE-9999</c> as text (Contract section 0.3).
    /// The order here is <c>created_at</c>, the same one <c>ListAsync</c> uses, and paging is only
    /// stable because of it.
    /// </remarks>
    [Fact]
    public async Task The_listing_pages_without_repeating_or_dropping_a_pole()
    {
        var client = await fixture.AdminClientAsync();
        var segmentId = await NewSegmentAsync(fixture.CommuneId);
        var feederId = await NewFeederAsync(fixture.CommuneId);

        var expected = new List<string>();
        for (var i = 0; i < 5; i++)
        {
            expected.Add(await NewPoleAsync(fixture.CommuneId, segmentId, feederId));
        }

        var first = await ReadPageAsync(client, $"{Feeders}/{feederId}/poles?page=1&page_size=2");
        var second = await ReadPageAsync(client, $"{Feeders}/{feederId}/poles?page=2&page_size=2");
        var third = await ReadPageAsync(client, $"{Feeders}/{feederId}/poles?page=3&page_size=2");

        Assert.Equal(5, first.GetProperty("total").GetInt32());

        var seen = new[] { first, second, third }
            .SelectMany(p => p.GetProperty("items").EnumerateArray())
            .Select(i => i.GetProperty("pole_id").GetString()!)
            .ToList();

        Assert.Equal(5, seen.Count);
        Assert.Equal(expected.Count, seen.Distinct().Count());
        Assert.Equal([.. expected.Order(StringComparer.Ordinal)], [.. seen.Order(StringComparer.Ordinal)]);
    }

    /// <summary>
    /// 🔴 The topology item does NOT carry the BE-12b inventory fields.
    /// </summary>
    /// <remarks>
    /// Two endpoints answering the same question with two values is how drift starts, and nothing
    /// detects the day they disagree. This endpoint serves a clustering engine; the inventory screen
    /// is BE-12b's and still unapproved. Pinned so that "while we are here, add fixture_status"
    /// has to argue with a failing test rather than slip through review.
    /// </remarks>
    [Fact]
    public async Task The_item_carries_only_the_topology_fields_and_none_of_the_inventory_ones()
    {
        var client = await fixture.AdminClientAsync();
        var segmentId = await NewSegmentAsync(fixture.CommuneId);
        var feederId = await NewFeederAsync(fixture.CommuneId);
        await NewPoleAsync(fixture.CommuneId, segmentId, feederId);

        var item = (await ReadPageAsync(client, $"{Feeders}/{feederId}/poles")).GetProperty("items")[0];
        var keys = item.EnumerateObject().Select(property => property.Name).ToHashSet(StringComparer.Ordinal);

        string[] expected = ["pole_id", "segment_id", "feeder_id", "power_source", "lat", "lng"];

        Assert.Equal(
            [.. expected.Order(StringComparer.Ordinal)],
            [.. keys.Order(StringComparer.Ordinal)]);

        foreach (var forbidden in new[]
                 {
                     "fixture_status", "open_fault_count", "status_confidence", "install_date",
                     "warranty_expiry", "external_ref", "commune_id", "data_source",
                 })
        {
            Assert.DoesNotContain(forbidden, keys);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────────────────

    private static async Task<JsonElement> ReadErrorAsync(HttpResponseMessage response)
        => JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("error");

    private static async Task<JsonElement> ReadPageAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
    }

    /// <summary>
    /// Walks every page, because this fixture's database is shared and other tests' poles are in it.
    /// </summary>
    private static async Task<List<JsonElement>> ReadAllAsync(HttpClient client, string url)
    {
        var all = new List<JsonElement>();
        for (var page = 1; ; page++)
        {
            var body = await ReadPageAsync(client, $"{url}&page={page}&page_size=200");
            var items = body.GetProperty("items").EnumerateArray().ToList();
            all.AddRange(items);

            if (items.Count == 0 || all.Count >= body.GetProperty("total").GetInt32())
            {
                return all;
            }
        }
    }

    private static async Task<JsonElement> FindPoleAsync(HttpClient client, string url, string poleId)
    {
        var match = (await ReadAllAsync(client, url))
            .FirstOrDefault(item => item.GetProperty("pole_id").GetString() == poleId);

        Assert.True(match.ValueKind == JsonValueKind.Object, $"{poleId} was not in {url}.");
        return match;
    }

    // Built through the DbContext rather than over HTTP: some of these rows live in a commune the
    // test account cannot write to, which is the point of those cases.

    private Task<string> NewSegmentAsync(string communeId)
        => fixture.QueryAsync(async db =>
        {
            using (db.EnterUnscopedSystemWriteBackdoor())
            {
                var segment = new RoadSegment
                {
                    SegmentName = "topology probe road",
                    RoadClass = RoadClass.InterCommune,
                    LengthM = 100,
                    Geom = new LineString([new Coordinate(106.49, 10.97), new Coordinate(106.50, 10.98)]) { SRID = 4326 },
                    CommuneId = communeId,
                    DataSource = DataSource.PublicImagery,
                };

                db.Set<RoadSegment>().Add(segment);
                await db.SaveChangesAsync();
                return segment.SegmentId;
            }
        });

    private Task<string> NewFeederAsync(string communeId)
        => fixture.QueryAsync(async db =>
        {
            using (db.EnterUnscopedSystemWriteBackdoor())
            {
                var feeder = new Feeder { FeederName = "topology probe cabinet", CommuneId = communeId };
                db.Set<Feeder>().Add(feeder);
                await db.SaveChangesAsync();
                return feeder.FeederId;
            }
        });

    private Task<string> NewPoleAsync(
        string communeId, string segmentId, string? feederId, double lng = 106.49, double lat = 10.97)
        => fixture.QueryAsync(async db =>
        {
            using (db.EnterUnscopedSystemWriteBackdoor())
            {
                var pole = new Pole
                {
                    SegmentId = segmentId,
                    CommuneId = communeId,
                    FeederId = feederId,
                    Geom = new Point(lng, lat) { SRID = 4326 },
                    DataSource = DataSource.PublicImagery,
                };

                db.Set<Pole>().Add(pole);
                await db.SaveChangesAsync();
                return pole.PoleId;
            }
        });

    private Task AddFixtureAsync(string poleId, PowerSource power, bool retired)
        => fixture.QueryAsync(async db =>
        {
            using (db.EnterUnscopedSystemWriteBackdoor())
            {
                var pole = await db.Set<Pole>().IgnoreQueryFilters().SingleAsync(p => p.PoleId == poleId);

                db.Set<Fixture>().Add(new Fixture
                {
                    PoleId = poleId,
                    CommuneId = pole.CommuneId,
                    FixtureType = power == PowerSource.Solar
                        ? FixtureType.SolarAllInOne
                        : FixtureType.LedRoadLamp,
                    PowerSource = power,
                    LampWatt = 80,
                    InstallDate = new DateOnly(2026, 1, 1),
                    RemovedDate = retired ? new DateOnly(2026, 6, 1) : null,
                    DataSource = DataSource.PublicImagery,
                });

                return await db.SaveChangesAsync();
            }
        });
}
