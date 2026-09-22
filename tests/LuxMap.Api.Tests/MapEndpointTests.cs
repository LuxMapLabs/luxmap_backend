using System.Net;
using System.Text.Json;
using LuxMap.Modules.Assets.Entities;
using LuxMap.Shared.Contracts.Enums;
using LuxMap.Shared.Contracts.Errors;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;

namespace LuxMap.Api.Tests;

/// <summary>
/// BE-14 — the map layers as the web and mobile clients actually receive them.
/// </summary>
/// <remarks>
/// <para>
/// <c>MapQueryPlanTests</c> covers the query PLAN; this covers the payload. Both matter and neither
/// substitutes for the other: a wrong plan returns right answers, and a right plan can still be
/// serialised into a shape the front end cannot bind.
/// </para>
/// <para>
/// <c>mock-poles.geojson</c> is the reference the Contract points at for this shape, so the
/// assertions here are about matching it — flat properties, <c>[lng, lat]</c>, no
/// <c>feature.id</c> — rather than about any opinion of mine.
/// </para>
/// </remarks>
[Collection(nameof(AssetDatabaseCollection))]
public sealed class MapEndpointTests(AssetImportFixture fixture)
{
    private const string Poles = "/api/v1/poles";
    private const string Segments = "/api/v1/segments";

    /// <summary>A box tight around the poles these tests plant, away from the seeded mock set.</summary>
    private const string Box = "?bbox=107.40,11.40,107.60,11.60";

    private const double Lng = 107.5;
    private const double Lat = 11.5;

    [Fact]
    public async Task A_pole_comes_back_as_a_flat_geojson_feature_with_lng_before_lat()
    {
        var client = await fixture.AdminClientAsync();
        var segmentId = await NewSegmentAsync(fixture.CommuneId);
        var poleId = await NewPoleAsync(fixture.CommuneId, segmentId);

        var body = await GetAsync(client, Poles + Box);

        Assert.Equal("FeatureCollection", body.GetProperty("type").GetString());

        var feature = Find(body, poleId);
        Assert.Equal("Feature", feature.GetProperty("type").GetString());

        // No feature.id — Contract section 1.5. MapLibre examples put the key there, so this is the
        // natural mistake; the front end binds properties.pole_id.
        Assert.False(feature.TryGetProperty("id", out _));

        var coordinates = feature.GetProperty("geometry").GetProperty("coordinates");
        Assert.Equal("Point", feature.GetProperty("geometry").GetProperty("type").GetString());
        Assert.Equal(Lng, coordinates[0].GetDouble(), 6);
        Assert.Equal(Lat, coordinates[1].GetDouble(), 6);
    }

    /// <summary>
    /// The fifteen keys of section 5.1, exactly — and the three that must never appear.
    /// </summary>
    /// <remarks>
    /// Asserted as a SET rather than field by field: the risk this guards is a field creeping in,
    /// and a per-field test cannot notice an extra one. <c>data_source</c>, <c>external_ref</c> and
    /// <c>feeder_id</c> are filterable but invisible — emitting <c>data_source</c> in particular
    /// would undo the default that keeps the calibration rig off the map.
    /// </remarks>
    [Fact]
    public async Task The_properties_are_exactly_the_fifteen_the_contract_lists()
    {
        var client = await fixture.AdminClientAsync();
        var segmentId = await NewSegmentAsync(fixture.CommuneId);
        var poleId = await NewPoleAsync(fixture.CommuneId, segmentId);

        var properties = Find(await GetAsync(client, Poles + Box), poleId).GetProperty("properties");
        var keys = properties.EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.Ordinal);

        string[] expected =
        [
            "pole_id", "segment_id", "fixture_status", "status_confidence", "power_source",
            "fixture_type", "lamp_watt", "install_date", "warranty_expiry", "commune_id",
            "last_seen_at", "last_sweep_id", "open_fault_count", "has_iot_node", "near_sensitive_poi",
        ];

        Assert.Equal([.. expected.Order(StringComparer.Ordinal)], [.. keys.Order(StringComparer.Ordinal)]);

        foreach (var forbidden in new[] { "data_source", "external_ref", "feeder_id" })
        {
            Assert.DoesNotContain(forbidden, keys);
        }
    }

    /// <summary>
    /// A pole nothing has classified reports <c>unknown</c> with a null confidence.
    /// </summary>
    /// <remarks>
    /// Not a placeholder: section 3.1 defines <c>unknown</c> as "the latest sweep did not cover this
    /// pole", which is precisely the state of a pole with no <c>pole_current_status</c> row. Section
    /// 5.1 then requires <c>status_confidence</c> to be null if and only if the status is unknown,
    /// so the two have to move together.
    /// <para>
    /// ⚠️ Until BE-15/BE-17 write that table this is EVERY pole, including the mock set whose
    /// GeoJSON carries real statuses. Registered as drift.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_pole_with_no_status_row_reads_as_unknown_with_no_confidence()
    {
        var client = await fixture.AdminClientAsync();
        var segmentId = await NewSegmentAsync(fixture.CommuneId);
        var poleId = await NewPoleAsync(fixture.CommuneId, segmentId);

        var properties = Find(await GetAsync(client, Poles + Box), poleId).GetProperty("properties");

        Assert.Equal("unknown", properties.GetProperty("fixture_status").GetString());
        Assert.Equal(JsonValueKind.Null, properties.GetProperty("status_confidence").ValueKind);
    }

    /// <summary>Installation fields come from the lamp IN SERVICE, never a retired one.</summary>
    /// <remarks>
    /// ⚠️ <b>The two lamps are told apart by WATTAGE now, not by power source.</b> This test used to
    /// plant a retired grid lamp beside an active solar one, which made the assertion self-evident.
    /// Contract v1.6 left <c>power_source</c> and <c>fixture_type</c> with one value each, so they
    /// can no longer discriminate anything — <c>lamp_watt</c> is what carries the test now, and 100
    /// against 40 is as decisive as grid against solar was.
    /// </remarks>
    [Fact]
    public async Task Installation_fields_come_from_the_lamp_in_service()
    {
        var client = await fixture.AdminClientAsync();
        var segmentId = await NewSegmentAsync(fixture.CommuneId);
        var poleId = await NewPoleAsync(fixture.CommuneId, segmentId);

        await NewFixtureAsync(poleId, PowerSource.Grid, watt: 100, retired: true);
        await NewFixtureAsync(poleId, PowerSource.Grid, watt: 40, retired: false);

        var properties = Find(await GetAsync(client, Poles + Box), poleId).GetProperty("properties");

        Assert.Equal(40, properties.GetProperty("lamp_watt").GetInt32());
        Assert.Equal("grid", properties.GetProperty("power_source").GetString());
        Assert.Equal("led_road_lamp", properties.GetProperty("fixture_type").GetString());
    }

    /// <summary><c>open_fault_count</c> counts the OPEN set and nothing else.</summary>
    /// <remarks>
    /// The resolved fault planted here is what makes the test meaningful: a version that counted
    /// every fault would answer 2. The set itself is <c>FaultStatusSets.Open</c>, written down once
    /// (BE-18) and only spread into an array here because EF cannot translate a set's Contains.
    /// </remarks>
    [Fact]
    public async Task Open_fault_count_ignores_faults_that_are_no_longer_open()
    {
        var client = await fixture.AdminClientAsync();
        var segmentId = await NewSegmentAsync(fixture.CommuneId);
        var poleId = await NewPoleAsync(fixture.CommuneId, segmentId);

        await NewFaultAsync(poleId, FaultStatus.Detected);
        await NewFaultAsync(poleId, FaultStatus.Resolved);

        var properties = Find(await GetAsync(client, Poles + Box), poleId).GetProperty("properties");

        Assert.Equal(1, properties.GetProperty("open_fault_count").GetInt32());
    }

    /// <summary>Past 2000 poles the answer is 413 with the real count, not a truncated map.</summary>
    /// <remarks>
    /// <para>
    /// Truncating would be worse than refusing: the client cannot tell a sparse area from a clipped
    /// one, and an operator would read "12 faults here" off a map showing a fraction of the poles.
    /// The count travels in <c>details</c> so the front end can say how far to zoom.
    /// </para>
    /// <para>
    /// ⚠️ It plants its own poles rather than leaning on <c>AssetSchemaFixture</c>'s 2500. Those sit
    /// in a DIFFERENT commune, so the BE-08 query filter hides them from this caller and the count
    /// would come back under the limit — the endpoint would answer 200 and the test would pass for
    /// the wrong reason. The rows are removed in a <c>finally</c>: a run that leaves poles behind
    /// poisons <c>pole_id_seq</c> for every later insert, which is the failure CLAUDE.md records as
    /// costing one run 36 red tests and the next 76.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task An_area_holding_more_than_two_thousand_poles_is_refused_with_the_count()
    {
        var client = await fixture.AdminClientAsync();
        var segmentId = await NewSegmentAsync(fixture.CommuneId);

        try
        {
            await PlantPolesAsync(segmentId, 2001);

            var response = await client.GetAsync(Poles + Box);
            var error = await ReadErrorAsync(response);

            Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
            Assert.Equal(ErrorCodes.BboxTooLarge, error.GetProperty("code").GetString());
            Assert.True(error.GetProperty("details").GetProperty("count").GetInt32() > 2000);
            Assert.Equal(2000, error.GetProperty("details").GetProperty("max").GetInt32());
        }
        finally
        {
            await RemovePlantedAsync(segmentId);
        }
    }

    /// <summary>
    /// Exactly at the limit the map is still served, so the refusal is a threshold and not a wall.
    /// </summary>
    /// <remarks>
    /// ⚠️ It plants into its OWN box, a degree away from <see cref="Box"/>. The other tests in this
    /// class leave their poles behind — they are cleaned up once, by the fixture — so counting
    /// inside the shared box would add their handful to the 2000 and trip the very limit this test
    /// exists to prove is not tripped. The bbox is what isolates it; no extra bookkeeping needed.
    /// </remarks>
    [Fact]
    public async Task An_area_at_the_limit_is_still_served()
    {
        const double lng = 108.5;
        const double lat = 12.5;

        var client = await fixture.AdminClientAsync();
        var segmentId = await NewSegmentAsync(fixture.CommuneId);

        try
        {
            await PlantPolesAsync(segmentId, 2000, lng, lat);

            var body = await GetAsync(client, $"{Poles}?bbox=108.40,12.40,108.60,12.60");

            Assert.Equal(2000, body.GetProperty("features").GetArrayLength());
        }
        finally
        {
            await RemovePlantedAsync(segmentId);
        }
    }

    [Theory]
    [InlineData("", "bbox is required")]
    [InlineData("?bbox=1,2,3", "exactly 4")]
    [InlineData("?bbox=NaN,10,107,11", "not a finite number")]
    [InlineData("?bbox=Infinity,10,107,11", "not a finite number")]
    [InlineData("?bbox=107,11,106,10", "min strictly below max")]
    [InlineData("?bbox=-200,10,107,11", "outside the valid range")]
    public async Task A_bbox_that_cannot_be_trusted_is_refused_rather_than_answered_emptily(
        string query, string expected)
    {
        var client = await fixture.AdminClientAsync();

        var response = await client.GetAsync(Poles + query);
        var error = await ReadErrorAsync(response);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ErrorCodes.ValidationFailed, error.GetProperty("code").GetString());
        Assert.Contains(expected, error.GetProperty("message").GetString()!, StringComparison.Ordinal);
    }

    /// <summary>
    /// 🔴 <c>NaN</c> deserves its own note: it is the one that would have answered 200.
    /// </summary>
    /// <remarks>
    /// <c>double.TryParse</c> accepts "NaN", and every PostGIS comparison against it is false, so an
    /// unchecked NaN bound returns an empty <c>FeatureCollection</c> with a 200 — a wrong answer
    /// wearing the shape of a right one. Same failure mode as the CHECK constraints CLAUDE.md
    /// requires on measured doubles.
    /// </remarks>
    [Fact]
    public async Task A_nan_bound_is_refused_and_would_otherwise_have_returned_an_empty_map()
    {
        var client = await fixture.AdminClientAsync();

        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await client.GetAsync($"{Poles}?bbox=106,NaN,107,11")).StatusCode);
    }

    /// <summary>A pole in another commune is invisible, without the caller being told it exists.</summary>
    [Fact]
    public async Task Poles_outside_the_callers_commune_are_not_in_the_collection()
    {
        var client = await fixture.AdminClientAsync();
        var segmentId = await NewSegmentAsync(fixture.CommuneId);
        var mine = await NewPoleAsync(fixture.CommuneId, segmentId);
        var theirs = await NewPoleAsync(fixture.ForeignCommuneId, segmentId);

        var ids = Ids(await GetAsync(client, Poles + Box));

        Assert.Contains(mine, ids);
        Assert.DoesNotContain(theirs, ids);
    }

    /// <summary>Asking for a commune outside the scope is 403 naming it, not a silent empty map.</summary>
    [Fact]
    public async Task Asking_for_a_commune_outside_the_scope_is_403_naming_that_commune()
    {
        var client = await fixture.AdminClientAsync();

        var response = await client.GetAsync($"{Poles}{Box}&commune_id={fixture.ForeignCommuneId}");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains(fixture.ForeignCommuneId, body, StringComparison.Ordinal);
    }

    /// <summary>
    /// The calibration rig is off the map by default and appears only when named.
    /// </summary>
    /// <remarks>
    /// Section 1.6, and CLAUDE.md is blunt about why: the FO-07 rig is registered as a real
    /// RoadSegment so the pipeline has one path, which puts its poles in the same table as the study
    /// area's. Mixing photometric ground truth into sensory-labelled figures is a serious error, not
    /// a presentation detail.
    /// </remarks>
    [Fact]
    public async Task The_calibration_rig_is_hidden_by_default_and_shown_when_asked_for_by_name()
    {
        var client = await fixture.AdminClientAsync();
        var segmentId = await NewSegmentAsync(fixture.CommuneId);
        var rig = await NewPoleAsync(fixture.CommuneId, segmentId, DataSource.CalibrationRig);

        Assert.DoesNotContain(rig, Ids(await GetAsync(client, Poles + Box)));
        Assert.Contains(rig, Ids(await GetAsync(client, $"{Poles}{Box}&data_source=calibration_rig")));
    }

    /// <summary>An unknown enum value is named in the refusal rather than swallowed.</summary>
    [Fact]
    public async Task An_unknown_status_value_is_refused_and_the_allowed_values_are_listed()
    {
        var client = await fixture.AdminClientAsync();

        var response = await client.GetAsync($"{Poles}{Box}&status=broken");
        var error = await ReadErrorAsync(response);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("broken", error.GetProperty("message").GetString()!, StringComparison.Ordinal);

        var allowed = error.GetProperty("details").GetProperty("allowed")
            .EnumerateArray().Select(v => v.GetString()).ToList();

        Assert.Contains("unknown", allowed);
        Assert.Contains("normal", allowed);
    }

    /// <summary>Filtering for <c>unknown</c> returns the poles that have no status row at all.</summary>
    /// <remarks>
    /// The filter and the rendered value have to agree on the same pole. A version that only matched
    /// rows present in <c>pole_current_status</c> would show a pole as <c>unknown</c> and then hide
    /// it when the operator filtered for exactly that — the kind of inconsistency nobody reports as
    /// a bug because it looks like the data.
    /// </remarks>
    [Fact]
    public async Task Filtering_for_unknown_finds_the_poles_that_have_no_status_row()
    {
        var client = await fixture.AdminClientAsync();
        var segmentId = await NewSegmentAsync(fixture.CommuneId);
        var poleId = await NewPoleAsync(fixture.CommuneId, segmentId);

        Assert.Contains(poleId, Ids(await GetAsync(client, $"{Poles}{Box}&status=unknown")));
        Assert.DoesNotContain(poleId, Ids(await GetAsync(client, $"{Poles}{Box}&status=normal,dim")));
    }

    // ── Segments ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_segment_comes_back_as_a_linestring_with_the_seven_contract_properties()
    {
        var client = await fixture.AdminClientAsync();
        var segmentId = await NewSegmentAsync(fixture.CommuneId);
        await NewPoleAsync(fixture.CommuneId, segmentId);

        var feature = Find(await GetAsync(client, Segments + Box), segmentId, "segment_id");

        Assert.Equal("LineString", feature.GetProperty("geometry").GetProperty("type").GetString());

        var keys = feature.GetProperty("properties").EnumerateObject()
            .Select(p => p.Name).ToHashSet(StringComparer.Ordinal);

        string[] expected =
        [
            "segment_id", "segment_name", "road_class", "length_m", "pole_count",
            "controller_node_id", "has_active_segment_fault",
        ];

        Assert.Equal([.. expected.Order(StringComparer.Ordinal)], [.. keys.Order(StringComparer.Ordinal)]);
        Assert.Equal(1, feature.GetProperty("properties").GetProperty("pole_count").GetInt32());
    }

    /// <summary>
    /// <c>has_active_segment_fault</c> is true for a segment-level outage, and a LAMP fault on the
    /// same road does not set it.
    /// </summary>
    /// <remarks>
    /// The distinction is the whole point of the flag: a segment outage is ONE cause that highlights
    /// the entire road, not N lamp faults (CLAUDE.md). A version that counted any open fault on the
    /// segment would light up every road with a single dim lamp on it.
    /// </remarks>
    [Fact]
    public async Task Only_a_segment_level_outage_highlights_the_whole_road()
    {
        var client = await fixture.AdminClientAsync();
        var withLampFault = await NewSegmentAsync(fixture.CommuneId);
        var withOutage = await NewSegmentAsync(fixture.CommuneId);

        var poleId = await NewPoleAsync(fixture.CommuneId, withLampFault);
        await NewFaultAsync(poleId, FaultStatus.Detected);
        await NewFaultAsync(poleId: null, FaultStatus.Detected, segmentId: withOutage, type: FaultType.SegmentOutage);

        var body = await GetAsync(client, Segments + Box);

        Assert.False(Find(body, withLampFault, "segment_id")
            .GetProperty("properties").GetProperty("has_active_segment_fault").GetBoolean());
        Assert.True(Find(body, withOutage, "segment_id")
            .GetProperty("properties").GetProperty("has_active_segment_fault").GetBoolean());
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────────────────

    private static async Task<JsonElement> ReadErrorAsync(HttpResponseMessage response)
        => JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("error");

    private static async Task<JsonElement> GetAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
    }

    private static IEnumerable<string?> Ids(JsonElement body, string key = "pole_id")
        => body.GetProperty("features").EnumerateArray()
            .Select(feature => feature.GetProperty("properties").GetProperty(key).GetString());

    private static JsonElement Find(JsonElement body, string id, string key = "pole_id")
    {
        var match = body.GetProperty("features").EnumerateArray()
            .FirstOrDefault(feature => feature.GetProperty("properties").GetProperty(key).GetString() == id);

        Assert.True(match.ValueKind == JsonValueKind.Object, $"{id} was not in the collection.");
        return match;
    }

    /// <summary>The range the bulk plants live in — far above anything the sequence will reach.</summary>
    /// <remarks>
    /// 🔴 <b>Explicit ids, so these inserts never call <c>nextval</c>.</b> Taking 2000 values out of
    /// the shared <c>pole_id_seq</c> would wreck <c>PrefixedIdOverflowTests</c>, which rewinds the
    /// sequence to a decade it has checked is free and then writes into it — a 2000-wide block makes
    /// "free decade" untrue and the two tests fight over ids. CLAUDE.md records what that costs: one
    /// run red at 36 tests, the next at 76, deterministic but looking flaky.
    /// <para>
    /// Seven digits also keeps them recognisable as synthetic in a shared database, and the width is
    /// a MINIMUM so a longer id is perfectly legal (Contract section 1.2).
    /// </para>
    /// </remarks>
    private const long PlantedIdBase = 9_000_000;

    /// <summary>
    /// Bulk-inserts poles on one segment with raw SQL — 2000 round trips through EF would dominate
    /// the run.
    /// </summary>
    private Task PlantPolesAsync(string segmentId, int count, double? lng = null, double? lat = null)
        => ExecuteAsync($"""
            INSERT INTO pole (pole_id, segment_id, commune_id, geom, near_sensitive_poi, data_source)
            SELECT 'POLE-' || ({PlantedIdBase} + i)::text, '{segmentId}', '{fixture.CommuneId}',
                   ST_SetSRID(ST_MakePoint({lng ?? Lng}, {lat ?? Lat}), 4326), false, 'public_imagery'
            FROM generate_series(1, {count}) AS i;
            """);

    /// <summary>
    /// Removes every planted pole. Runs in a <c>finally</c>, and deliberately clears the WHOLE
    /// planted range rather than one segment's rows: a run that died earlier would otherwise leave
    /// ids behind for the next one to collide with.
    /// </summary>
    private Task RemovePlantedAsync(string segmentId)
        => ExecuteAsync(
            $"DELETE FROM pole WHERE segment_id = '{segmentId}' "
            + $"OR substring(pole_id FROM 6)::bigint > {PlantedIdBase};");

    private Task ExecuteAsync(string sql)
        => fixture.QueryAsync(async db =>
        {
            var connection = db.Database.GetDbConnection();
            await db.Database.OpenConnectionAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            return await command.ExecuteNonQueryAsync();
        });

    private Task<string> NewSegmentAsync(string communeId)
        => fixture.QueryAsync(async db =>
        {
            using (db.EnterUnscopedSystemWriteBackdoor())
            {
                var segment = new RoadSegment
                {
                    SegmentName = "map probe road",
                    RoadClass = RoadClass.InterVillage,
                    LengthM = 250,
                    Geom = new LineString([new Coordinate(Lng, Lat), new Coordinate(Lng + 0.01, Lat + 0.01)]) { SRID = 4326 },
                    CommuneId = communeId,
                    DataSource = DataSource.PublicImagery,
                };

                db.Set<RoadSegment>().Add(segment);
                await db.SaveChangesAsync();
                return segment.SegmentId;
            }
        });

    private Task<string> NewPoleAsync(
        string communeId, string segmentId, DataSource dataSource = DataSource.PublicImagery)
        => fixture.QueryAsync(async db =>
        {
            using (db.EnterUnscopedSystemWriteBackdoor())
            {
                var pole = new Pole
                {
                    SegmentId = segmentId,
                    CommuneId = communeId,
                    Geom = new Point(Lng, Lat) { SRID = 4326 },
                    DataSource = dataSource,
                };

                db.Set<Pole>().Add(pole);
                await db.SaveChangesAsync();
                return pole.PoleId;
            }
        });

    private Task NewFixtureAsync(string poleId, PowerSource power, int watt, bool retired)
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
                    PowerSource = power,
                    LampWatt = watt,
                    InstallDate = new DateOnly(2026, 1, 1),
                    RemovedDate = retired ? new DateOnly(2026, 6, 1) : null,
                    DataSource = DataSource.PublicImagery,
                });

                return await db.SaveChangesAsync();
            }
        });

    private Task NewFaultAsync(
        string? poleId,
        FaultStatus status,
        string? segmentId = null,
        FaultType type = FaultType.LampOut)
        => fixture.QueryAsync(async db =>
        {
            using (db.EnterUnscopedSystemWriteBackdoor())
            {
                db.Set<Modules.Faults.Entities.Fault>().Add(new Modules.Faults.Entities.Fault
                {
                    PoleId = poleId,
                    SegmentId = segmentId,
                    CommuneId = fixture.CommuneId,

                    // ck_fault_pole_or_location: a fault names a pole OR carries a location. A
                    // segment_outage belongs to the whole road, so it has no pole and must say where.
                    Lat = poleId is null ? Lat : null,
                    Lng = poleId is null ? Lng : null,
                    FaultType = type,
                    FaultStatus = status,
                    Severity = Severity.Medium,
                    SourceChannel = SourceChannel.Cv,
                    DataSource = DataSource.PublicImagery,
                    DetectedAt = DateTime.UtcNow,
                });

                return await db.SaveChangesAsync();
            }
        });
}
