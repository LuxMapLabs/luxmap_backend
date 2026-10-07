using System.Net;
using System.Text.Json;
using LuxMap.Modules.Assets.Entities;
using LuxMap.Modules.Telemetry;
using LuxMap.Modules.Telemetry.Entities;
using LuxMap.Shared.Contracts.Enums;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using Npgsql;

namespace LuxMap.Api.Tests;

/// <summary>
/// BE-14b — cabinet devices on the map and the feeders they switch (drift "BE-14 / IoT").
/// </summary>
/// <remarks>
/// Expectations are LITERALS on purpose: the key set, the status names, the list orders. A test
/// that read its expectation from the code under test would agree with any code.
/// </remarks>
[Collection(nameof(AssetDatabaseCollection))]
public sealed class IotNodeTests(AssetImportFixture fixture)
{
    private const string Nodes = "/api/v1/map/iot-nodes";
    private const string Segments = "/api/v1/map/segments";

    /// <summary>A box of its own, away from the mock set and from MapEndpointTests' box.</summary>
    private const string Box = "?bbox=108.40,12.40,108.60,12.60";

    private const double Lng = 108.5;
    private const double Lat = 12.5;

    [Fact]
    public async Task A_device_is_a_flat_point_feature_with_exactly_the_decided_keys()
    {
        var client = await fixture.ManagerClientAsync();
        var node = await NewNodeAsync(fixture.CommuneId);

        var feature = Find(await GetAsync(client, Nodes + Box), node);

        Assert.False(feature.TryGetProperty("id", out _));
        Assert.Equal("Point", feature.GetProperty("geometry").GetProperty("type").GetString());
        var coordinates = feature.GetProperty("geometry").GetProperty("coordinates");
        Assert.Equal(Lng, coordinates[0].GetDouble(), 6);
        Assert.Equal(Lat, coordinates[1].GetDouble(), 6);

        var properties = feature.GetProperty("properties");
        string[] expected =
        [
            "node_id", "node_role", "node_status", "pole_id", "segment_ids", "feeder_ids",
            "supports_remote_control", "last_report_at",
        ];
        Assert.Equal(
            [.. expected.Order(StringComparer.Ordinal)],
            [.. properties.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal)]);

        // I-2: battery_pct is gone. I-1/I-8: a device is never on a pole.
        Assert.False(properties.TryGetProperty("battery_pct", out _));
        Assert.Equal(JsonValueKind.Null, properties.GetProperty("pole_id").ValueKind);
        Assert.Equal("segment_controller", properties.GetProperty("node_role").GetString());
        Assert.False(properties.GetProperty("supports_remote_control").GetBoolean());
    }

    [Fact]
    public async Task A_missing_bbox_is_refused_rather_than_answered_with_everything()
    {
        var client = await fixture.ManagerClientAsync();

        var response = await client.GetAsync(Nodes);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Status_is_derived_from_the_last_report_and_the_one_hour_threshold()
    {
        var client = await fixture.ManagerClientAsync();
        var never = await NewNodeAsync(fixture.CommuneId, lastReportAt: null);
        var recent = await NewNodeAsync(fixture.CommuneId, lastReportAt: DateTime.UtcNow.AddMinutes(-30));
        var silent = await NewNodeAsync(fixture.CommuneId, lastReportAt: DateTime.UtcNow.AddHours(-2));

        var body = await GetAsync(client, Nodes + Box);

        Assert.Equal("never_reported", Status(body, never));
        Assert.Equal("online", Status(body, recent));
        Assert.Equal("offline", Status(body, silent));
    }

    /// <summary>
    /// Feeders come in RELAY order, segments in id order — and a segment reached only through a
    /// feeder no device switches is not listed.
    /// </summary>
    [Fact]
    public async Task Feeders_and_segments_are_derived_through_the_relays()
    {
        var client = await fixture.ManagerClientAsync();
        var node = await NewNodeAsync(fixture.CommuneId);

        // Created X then Y, wired the other way round: relay order must win over id order.
        var feederX = await NewFeederAsync(fixture.CommuneId);
        var feederY = await NewFeederAsync(fixture.CommuneId);
        var uncontrolled = await NewFeederAsync(fixture.CommuneId);
        await ControlAsync(feederX, node, relay: 2);
        await ControlAsync(feederY, node, relay: 1);

        var first = await NewSegmentAsync(fixture.CommuneId);
        var second = await NewSegmentAsync(fixture.CommuneId);
        var elsewhere = await NewSegmentAsync(fixture.CommuneId);
        await NewPoleAsync(second, feederX);
        await NewPoleAsync(first, feederY);
        await NewPoleAsync(first, feederX);
        await NewPoleAsync(elsewhere, uncontrolled);

        var properties = Find(await GetAsync(client, Nodes + Box), node).GetProperty("properties");

        Assert.Equal([feederY, feederX], Strings(properties.GetProperty("feeder_ids")));
        Assert.Equal([first, second], Strings(properties.GetProperty("segment_ids")));
    }

    /// <summary>I-7b — a segment fed by two cabinets lists both devices, in id order.</summary>
    [Fact]
    public async Task A_segment_lists_every_device_that_switches_one_of_its_feeders()
    {
        var client = await fixture.ManagerClientAsync();
        var nodeA = await NewNodeAsync(fixture.CommuneId);
        var nodeB = await NewNodeAsync(fixture.CommuneId);
        var feederA = await NewFeederAsync(fixture.CommuneId);
        var feederB = await NewFeederAsync(fixture.CommuneId);
        await ControlAsync(feederA, nodeA, relay: 1);
        await ControlAsync(feederB, nodeB, relay: 1);

        var shared = await NewSegmentAsync(fixture.CommuneId);
        var alone = await NewSegmentAsync(fixture.CommuneId);
        await NewPoleAsync(shared, feederB);
        await NewPoleAsync(shared, feederA);
        await NewPoleAsync(alone, feederA);

        var body = await GetAsync(client, Segments + Box);

        Assert.Equal([nodeA, nodeB], Controllers(body, shared));
        Assert.Equal([nodeA], Controllers(body, alone));
    }

    [Fact]
    public async Task The_pole_flag_stays_false_even_on_a_controlled_feeder()
    {
        var client = await fixture.ManagerClientAsync();
        var node = await NewNodeAsync(fixture.CommuneId);
        var feeder = await NewFeederAsync(fixture.CommuneId);
        await ControlAsync(feeder, node, relay: 1);
        var pole = await NewPoleAsync(await NewSegmentAsync(fixture.CommuneId), feeder);

        var body = await GetAsync(client, "/api/v1/map/poles" + Box);

        Assert.False(Find(body, pole, "pole_id").GetProperty("properties").GetProperty("has_iot_node").GetBoolean());
    }

    [Fact]
    public async Task A_device_of_another_commune_is_invisible_and_asking_for_it_is_403()
    {
        var client = await fixture.ManagerClientAsync();
        var foreign = await NewNodeAsync(fixture.ForeignCommuneId);

        var body = await GetAsync(client, Nodes + Box);
        Assert.DoesNotContain(foreign, Ids(body));

        var response = await client.GetAsync($"{Nodes}{Box}&commune_id={fixture.ForeignCommuneId}");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("COMMUNE_FORBIDDEN", (await ErrorAsync(response)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task The_testbed_is_hidden_by_default_and_shown_when_asked_for_by_name()
    {
        var client = await fixture.ManagerClientAsync();
        var testbed = await NewNodeAsync(fixture.CommuneId, source: DataSource.CalibrationRig, remote: true);

        Assert.DoesNotContain(testbed, Ids(await GetAsync(client, Nodes + Box)));

        var shown = Find(await GetAsync(client, Nodes + Box + "&data_source=calibration_rig"), testbed);
        Assert.True(shown.GetProperty("properties").GetProperty("supports_remote_control").GetBoolean());
    }

    // ── Database rules ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Two_feeders_cannot_claim_the_same_relay_of_one_device()
    {
        var node = await NewNodeAsync(fixture.CommuneId);
        await ControlAsync(await NewFeederAsync(fixture.CommuneId), node, relay: 1);
        var second = await NewFeederAsync(fixture.CommuneId);

        var error = await Assert.ThrowsAsync<DbUpdateException>(() => ControlAsync(second, node, relay: 1));

        Assert.Equal("ux_feeder_control_node_id_relay_no", Postgres(error).ConstraintName);
    }

    [Fact]
    public async Task A_device_cannot_switch_a_feeder_of_another_commune()
    {
        var node = await NewNodeAsync(fixture.CommuneId);
        var foreignFeeder = await NewFeederAsync(fixture.ForeignCommuneId);

        var error = await Assert.ThrowsAsync<DbUpdateException>(
            () => ControlAsync(foreignFeeder, node, relay: 1, commune: fixture.CommuneId));

        Assert.Equal("fk_feeder_control_feeder_feeder_id_commune_id", Postgres(error).ConstraintName);
    }

    [Theory]
    [InlineData(DataSource.Field)]
    [InlineData(DataSource.PublicImagery)]
    public async Task A_device_is_never_field_or_public_imagery_data(DataSource source)
    {
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => NewNodeAsync(fixture.CommuneId, source: source));

        Assert.Equal("ck_iot_node_data_source_not_field", Postgres(error).ConstraintName);
    }

    [Fact]
    public async Task Relay_zero_and_a_mode_without_its_timestamp_are_refused()
    {
        var node = await NewNodeAsync(fixture.CommuneId);
        var first = await NewFeederAsync(fixture.CommuneId);
        var second = await NewFeederAsync(fixture.CommuneId);

        var zero = await Assert.ThrowsAsync<DbUpdateException>(() => ControlAsync(first, node, relay: 0));
        Assert.Equal("ck_feeder_control_relay_no_positive", Postgres(zero).ConstraintName);

        var mode = await Assert.ThrowsAsync<DbUpdateException>(
            () => ControlAsync(second, node, relay: 3, mode: FeederControlMode.Auto));
        Assert.Equal("ck_feeder_control_mode_reported", Postgres(mode).ConstraintName);
    }

    // ── Threshold (no database) ────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(59, "Online")]
    [InlineData(60, "Online")]
    [InlineData(61, "Offline")]
    public void Exactly_at_the_threshold_is_still_online(int minutesSilent, string expected)
    {
        var now = new DateTime(2026, 9, 28, 12, 0, 0, DateTimeKind.Utc);

        var status = new IotOptions().StatusAt(now.AddMinutes(-minutesSilent), now);

        Assert.Equal(expected, status.ToString());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void A_non_positive_threshold_stops_startup(int minutes)
    {
        var options = new IotOptions { OfflineAfter = TimeSpan.FromMinutes(minutes) };

        Assert.Throws<InvalidOperationException>(options.Validate);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────────────────────

    private static async Task<JsonElement> GetAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
    }

    private static async Task<JsonElement> ErrorAsync(HttpResponseMessage response)
        => JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("error").Clone();

    private static IEnumerable<string?> Ids(JsonElement body)
        => body.GetProperty("features").EnumerateArray()
            .Select(feature => feature.GetProperty("properties").GetProperty("node_id").GetString());

    private static JsonElement Find(JsonElement body, string id, string key = "node_id")
    {
        var match = body.GetProperty("features").EnumerateArray()
            .FirstOrDefault(feature => feature.GetProperty("properties").GetProperty(key).GetString() == id);

        Assert.True(match.ValueKind == JsonValueKind.Object, $"{id} was not in the collection.");
        return match;
    }

    private static string? Status(JsonElement body, string node)
        => Find(body, node).GetProperty("properties").GetProperty("node_status").GetString();

    private static string[] Strings(JsonElement array)
        => [.. array.EnumerateArray().Select(item => item.GetString() ?? "<null>")];

    private static string[] Controllers(JsonElement body, string segment)
        => Strings(Find(body, segment, "segment_id").GetProperty("properties").GetProperty("controller_node_ids"));

    private static PostgresException Postgres(DbUpdateException error) => Assert.IsType<PostgresException>(error.InnerException);

    private Task<T> AsSystemAsync<T>(Func<LuxMap.Persistence.LuxMapDbContext, Task<T>> write)
        => fixture.QueryAsync(async db =>
        {
            using (db.EnterUnscopedSystemWriteBackdoor())
            {
                return await write(db);
            }
        });

    /// <summary>Which cabinet each device of this test sits in — a relay must name it (CAB-4).</summary>
    private readonly Dictionary<string, string> cabinetOf = new(StringComparer.Ordinal);

    /// <summary>
    /// A device in a cabinet of its own at (<see cref="Lng"/>, <see cref="Lat"/>) — the point the map shows (CAB-3).
    /// The cabinet is <c>simulated</c> whatever the device is, so the device's own CHECK is the one under test.
    /// </summary>
    private async Task<string> NewNodeAsync(
        string communeId, DateTime? lastReportAt = null, DataSource source = DataSource.Simulated, bool remote = false)
    {
        var cabinet = await NewCabinetAsync(communeId);
        var nodeId = await AsSystemAsync(async db =>
        {
            var node = new IotNode
            {
                CommuneId = communeId,
                CabinetId = cabinet,
                CabinetDataSource = DataSource.Simulated,
                DataSource = source,
                SupportsRemoteControl = remote,
                LastReportAt = lastReportAt,
            };
            db.Add(node);
            await db.SaveChangesAsync();
            return node.NodeId;
        });

        cabinetOf[nodeId] = cabinet;
        return nodeId;
    }

    private Task<string> NewCabinetAsync(string communeId, DataSource source = DataSource.Simulated)
        => AsSystemAsync(async db =>
        {
            var cabinet = new ElectricalCabinet
            {
                CabinetName = "iot probe cabinet",
                CommuneId = communeId,
                Geom = new Point(Lng, Lat) { SRID = 4326 },
                DataSource = source,
            };
            db.Add(cabinet);
            await db.SaveChangesAsync();
            return cabinet.CabinetId;
        });

    private Task<string> NewFeederAsync(string communeId)
        => AsSystemAsync(async db =>
        {
            var feeder = new Feeder { FeederName = "iot probe feeder", CommuneId = communeId };
            db.Add(feeder);
            await db.SaveChangesAsync();
            return feeder.FeederId;
        });

    private Task<string> NewSegmentAsync(string communeId)
        => AsSystemAsync(async db =>
        {
            var segment = new RoadSegment
            {
                SegmentName = "iot probe road",
                RoadClass = RoadClass.InterVillage,
                LengthM = 100,
                Geom = new LineString([new Coordinate(Lng, Lat), new Coordinate(Lng + 0.01, Lat + 0.01)]) { SRID = 4326 },
                CommuneId = communeId,
                DataSource = DataSource.PublicImagery,
            };
            db.Add(segment);
            await db.SaveChangesAsync();
            return segment.SegmentId;
        });

    private Task<string> NewPoleAsync(string segmentId, string feederId)
        => AsSystemAsync(async db =>
        {
            var pole = new Pole
            {
                SegmentId = segmentId,
                FeederId = feederId,
                CommuneId = fixture.CommuneId,
                Geom = new Point(Lng, Lat) { SRID = 4326 },
                DataSource = DataSource.PublicImagery,
            };
            db.Add(pole);
            await db.SaveChangesAsync();
            return pole.PoleId;
        });

    /// <summary>
    /// Wires one relay. The feeder joins the device's cabinet first (CAB-4) — unless it is in another commune,
    /// where the composite key would refuse that move before the relay under test is ever written.
    /// </summary>
    private Task<int> ControlAsync(
        string feederId, string nodeId, short relay, string? commune = null, FeederControlMode? mode = null)
        => AsSystemAsync(async db =>
        {
            var cabinet = cabinetOf[nodeId];
            var feeder = await db.Set<Feeder>().IgnoreQueryFilters().SingleAsync(candidate => candidate.FeederId == feederId);
            if (feeder.CommuneId == (commune ?? fixture.CommuneId) && feeder.CabinetId != cabinet)
            {
                feeder.CabinetId = cabinet;
                await db.SaveChangesAsync();
            }

            db.Add(new FeederControl
            {
                FeederId = feederId,
                NodeId = nodeId,
                CommuneId = commune ?? fixture.CommuneId,
                CabinetId = cabinet,
                RelayNo = relay,
                ControlMode = mode,
            });
            return await db.SaveChangesAsync();
        });
}
