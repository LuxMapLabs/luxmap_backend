using System.Net;
using System.Text.Json;
using LuxMap.Modules.Assets.Entities;
using LuxMap.Modules.Faults.Entities;
using LuxMap.Shared.Contracts.Enums;
using LuxMap.Shared.Contracts.Errors;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;

namespace LuxMap.Api.Tests;

/// <summary>
/// BE-20 — <c>GET /map/poles/{pole_id}</c> as the clients receive it: shape, scope, and the parts that
/// come from other modules. The survey-derived parts (history, baselines, frames) are covered next to
/// the publication tests that produce them, in <c>SurveyPublicationTests</c>.
/// </summary>
[Collection(nameof(AssetDatabaseCollection))]
public sealed class PoleDetailEndpointTests(AssetImportFixture fixture)
{
    private const double Lng = 107.7;
    private const double Lat = 11.7;

    private static string Url(string poleId) => $"/api/v1/map/poles/{poleId}";

    [Fact]
    public async Task A_pole_nothing_has_classified_answers_unknown_with_empty_lists_rather_than_failing()
    {
        var client = await fixture.ManagerClientAsync();
        var poleId = await NewPoleAsync(fixture.CommuneId);

        var response = await client.GetAsync(Url(poleId));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadAsync(response);

        // Contract 3.1: no published sweep for the pole IS `unknown`, with no confidence and no time.
        var status = body.GetProperty("current_status");
        Assert.Equal("unknown", status.GetProperty("fixture_status").GetString());
        Assert.Equal(JsonValueKind.Null, status.GetProperty("status_confidence").ValueKind);
        Assert.Equal(JsonValueKind.Null, status.GetProperty("determined_at").ValueKind);
        Assert.Equal(JsonValueKind.Null, status.GetProperty("source_channel").ValueKind);

        Assert.Equal(JsonValueKind.Null, body.GetProperty("fixture").ValueKind);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("luminance_baseline").ValueKind);
        foreach (var list in new[] { "luminance_baselines", "luminance_history", "runtime_history", "open_faults", "recent_frames" })
            Assert.Equal(0, body.GetProperty(list).GetArrayLength());

        // IoT hangs off the feeder cabinet, never a pole (drift I-1/I-8): the key stays, the value is null.
        Assert.Equal(JsonValueKind.Null, body.GetProperty("iot_node").ValueKind);
    }

    [Fact]
    public async Task The_keys_are_exactly_the_documented_ones_and_data_source_external_ref_feeder_id_never_appear()
    {
        var client = await fixture.ManagerClientAsync();
        var poleId = await NewPoleAsync(fixture.CommuneId);

        var body = await ReadAsync(await client.GetAsync(Url(poleId)));

        // A SET, not field by field: the risk is a field creeping in, and a per-field test cannot see one.
        var keys = body.EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
        Assert.Equal(new[]
        {
            "pole_id", "segment_id", "segment_name", "commune_id", "location", "fixture", "current_status",
            "iot_node", "luminance_baseline", "luminance_baselines", "luminance_history", "runtime_history",
            "open_faults", "recent_frames", "note",
        }.ToHashSet(StringComparer.Ordinal), keys);

        Assert.Equal(poleId, body.GetProperty("pole_id").GetString());
        Assert.Equal("detail probe road", body.GetProperty("segment_name").GetString());
        var location = body.GetProperty("location");
        Assert.Equal(Lat, location.GetProperty("lat").GetDouble(), 6);
        Assert.Equal(Lng, location.GetProperty("lng").GetDouble(), 6);
    }

    [Fact]
    public async Task The_fixture_is_the_one_in_use_not_a_retired_one()
    {
        var client = await fixture.ManagerClientAsync();
        var poleId = await NewPoleAsync(fixture.CommuneId);
        await NewFixtureAsync(poleId, watt: 40, removed: new DateOnly(2026, 6, 1));
        await NewFixtureAsync(poleId, watt: 60, removed: null, warranty: new DateOnly(2031, 1, 1));

        var lamp = (await ReadAsync(await client.GetAsync(Url(poleId)))).GetProperty("fixture");

        Assert.Equal(60, lamp.GetProperty("lamp_watt").GetInt32());
        Assert.Equal("led_road_lamp", lamp.GetProperty("fixture_type").GetString());
        Assert.Equal("grid", lamp.GetProperty("power_source").GetString());
        Assert.Equal("2031-01-01", lamp.GetProperty("warranty_expiry").GetString());
    }

    [Fact]
    public async Task A_published_status_row_gives_confidence_time_and_the_cv_channel()
    {
        var client = await fixture.ManagerClientAsync();
        var poleId = await NewPoleAsync(fixture.CommuneId);
        var evaluated = new DateTime(2026, 10, 3, 14, 30, 0, DateTimeKind.Utc);
        await fixture.QueryAsync(async db =>
        {
            using (db.EnterUnscopedSystemWriteBackdoor())
            {
                db.Set<PoleCurrentStatus>().Add(new PoleCurrentStatus
                {
                    PoleId = poleId, CommuneId = fixture.CommuneId, FixtureStatus = FixtureStatus.Dim,
                    StatusConfidence = 0.81, LastSeenAt = evaluated, LastEvaluatedAt = evaluated,
                    UpdatedAt = evaluated,
                });
                await db.SaveChangesAsync();
            }
            return 0;
        });

        var status = (await ReadAsync(await client.GetAsync(Url(poleId)))).GetProperty("current_status");

        Assert.Equal("dim", status.GetProperty("fixture_status").GetString());
        Assert.Equal(0.81, status.GetProperty("status_confidence").GetDouble(), 6);
        Assert.Equal("2026-10-03T14:30:00Z", status.GetProperty("determined_at").GetString());
        Assert.Equal("cv", status.GetProperty("source_channel").GetString());
    }

    /// <summary>
    /// Only OPEN faults, most severe first. Severity is stored as text, so ordering by the column
    /// would give critical, high, low, medium — the plants below are chosen so that order is WRONG.
    /// </summary>
    [Fact]
    public async Task Open_faults_are_ranked_by_severity_then_priority_and_closed_ones_stay_out()
    {
        var client = await fixture.ManagerClientAsync();
        var poleId = await NewPoleAsync(fixture.CommuneId);
        var low = await NewFaultAsync(poleId, Severity.Low, FaultStatus.Detected, priority: 90);
        var medium = await NewFaultAsync(poleId, Severity.Medium, FaultStatus.Confirmed, priority: null);
        var mediumScored = await NewFaultAsync(poleId, Severity.Medium, FaultStatus.InProgress, priority: 10);
        var critical = await NewFaultAsync(poleId, Severity.Critical, FaultStatus.Detected, priority: 1);
        await NewFaultAsync(poleId, Severity.Critical, FaultStatus.Rejected, priority: 99);
        await NewFaultAsync(poleId, Severity.Critical, FaultStatus.Resolved, priority: 99);
        await NewFaultAsync(poleId, Severity.Critical, FaultStatus.Verified, priority: 99);

        var faults = (await ReadAsync(await client.GetAsync(Url(poleId)))).GetProperty("open_faults")
            .EnumerateArray().Select(f => f.GetProperty("fault_id").GetString()!).ToArray();

        // critical; then medium with its score before medium never scored (NULL last); then low.
        Assert.Equal([critical, mediumScored, medium, low], faults);
    }

    [Fact]
    public async Task A_reviewers_reclassification_shows_as_the_fault_type()
    {
        var client = await fixture.ManagerClientAsync();
        var poleId = await NewPoleAsync(fixture.CommuneId);
        await NewFaultAsync(poleId, Severity.High, FaultStatus.Confirmed, priority: null,
            type: FaultType.LampDim, overrideType: FaultType.LampOut);

        var fault = (await ReadAsync(await client.GetAsync(Url(poleId)))).GetProperty("open_faults")[0];

        Assert.Equal("lamp_out", fault.GetProperty("fault_type").GetString());
    }

    [Fact]
    public async Task A_pole_outside_the_callers_communes_is_404_exactly_like_one_that_does_not_exist()
    {
        var foreignPole = await NewPoleAsync(fixture.ForeignCommuneId);
        var mine = await fixture.ManagerClientAsync();

        var foreign = await mine.GetAsync(Url(foreignPole));
        var missing = await mine.GetAsync(Url("POLE-9999999"));

        // Contract section 7: no 403 that would confirm the pole exists.
        foreach (var response in new[] { foreign, missing })
        {
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal(ErrorCodes.PoleNotFound, (await ReadAsync(response)).GetProperty("error").GetProperty("code").GetString());
        }

        // The manager scoped to both communes does see it.
        var both = await (await fixture.BothCommunesClientAsync()).GetAsync(Url(foreignPole));
        Assert.Equal(HttpStatusCode.OK, both.StatusCode);
    }

    [Fact]
    public async Task Without_a_token_the_endpoint_is_closed()
    {
        var anonymous = fixture.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(Url("POLE-0001"))).StatusCode);
    }

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response)
        => JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    private Task<string> NewPoleAsync(string communeId)
        => fixture.QueryAsync(async db =>
        {
            using (db.EnterUnscopedSystemWriteBackdoor())
            {
                var segment = new RoadSegment
                {
                    SegmentName = "detail probe road", RoadClass = RoadClass.InterVillage, LengthM = 100,
                    Geom = new LineString([new Coordinate(Lng, Lat), new Coordinate(Lng + 0.01, Lat + 0.01)]) { SRID = 4326 },
                    CommuneId = communeId, DataSource = DataSource.PublicImagery,
                };
                db.Set<RoadSegment>().Add(segment);
                await db.SaveChangesAsync();

                var pole = new Pole
                {
                    SegmentId = segment.SegmentId, CommuneId = communeId,
                    Geom = new Point(Lng, Lat) { SRID = 4326 }, DataSource = DataSource.PublicImagery,
                };
                db.Set<Pole>().Add(pole);
                await db.SaveChangesAsync();
                return pole.PoleId;
            }
        });

    private Task NewFixtureAsync(string poleId, int watt, DateOnly? removed, DateOnly? warranty = null)
        => fixture.QueryAsync(async db =>
        {
            using (db.EnterUnscopedSystemWriteBackdoor())
            {
                var pole = await db.Set<Pole>().IgnoreQueryFilters().SingleAsync(p => p.PoleId == poleId);
                db.Set<Fixture>().Add(new Fixture
                {
                    PoleId = poleId, CommuneId = pole.CommuneId, FixtureType = FixtureType.LedRoadLamp,
                    PowerSource = PowerSource.Grid, LampWatt = watt, InstallDate = new DateOnly(2026, 1, 1),
                    RemovedDate = removed, WarrantyExpiry = warranty, DataSource = DataSource.PublicImagery,
                });
                await db.SaveChangesAsync();
            }
            return 0;
        });

    private Task<string> NewFaultAsync(string poleId, Severity severity, FaultStatus status, double? priority,
        FaultType type = FaultType.LampOut, FaultType? overrideType = null)
        => fixture.QueryAsync(async db =>
        {
            using (db.EnterUnscopedSystemWriteBackdoor())
            {
                var pole = await db.Set<Pole>().IgnoreQueryFilters().SingleAsync(p => p.PoleId == poleId);
                var fault = new Fault
                {
                    CommuneId = pole.CommuneId, SegmentId = pole.SegmentId, PoleId = poleId, Lat = Lat, Lng = Lng,
                    FaultType = type, OverrideFaultType = overrideType, FaultStatus = status, Severity = severity,
                    PriorityScore = priority, SourceChannel = SourceChannel.Cv, DataSource = DataSource.PublicImagery,
                    DetectedAt = DateTime.UtcNow,
                };
                db.Set<Fault>().Add(fault);
                await db.SaveChangesAsync();
                return fault.FaultId;
            }
        });
}
