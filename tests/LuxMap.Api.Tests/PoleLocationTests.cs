using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LuxMap.Modules.Assets.Entities;
using LuxMap.Persistence.Audit;
using LuxMap.Shared.Contracts.Enums;
using LuxMap.Shared.Contracts.Errors;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;

namespace LuxMap.Api.Tests;

/// <summary>
/// POLE-LOCATION — a field engineer or a manager moves a pole to where it really stands. Any distance along its road;
/// never farther from its own segment than the survey corridor (25 m), or later sweeps would silently stop seeing it.
/// </summary>
[Collection(nameof(AssetDatabaseCollection))]
public sealed class PoleLocationTests(AssetImportFixture fixture)
{
    // The probe road runs due east along one latitude, so a step north is a step straight off the road.
    private const double Lng = 107.72;
    private const double Lat = 11.72;
    private const double PoleLng = Lng + 0.005;

    /// <summary>Degrees of latitude per metre here, close enough for "well inside" and "well outside" 25 m.</summary>
    private const double DegreesPerMetre = 1 / 110_600.0;

    private const string EngineerName = "Kỹ sư hiện trường thử";

    private static string LocationUrl(string poleId) => $"/api/v1/assets/poles/{poleId}/location";

    [Fact]
    public async Task A_field_engineer_moves_a_pole_far_along_its_road_and_the_move_is_audited()
    {
        var poleId = await NewPoleAsync(fixture.CommuneId, fixture.CommuneId);
        var target = (Lat, Lng: Lng + 0.009);

        var response = await (await fixture.FieldEngineerClientAsync()).PutAsJsonAsync(LocationUrl(poleId), new { lat = target.Lat, lng = target.Lng });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await Json(response);
        Assert.Equal(target.Lat, body.GetProperty("lat").GetDouble());
        Assert.Equal(target.Lng, body.GetProperty("lng").GetDouble());
        Assert.InRange(body.GetProperty("moved_m").GetDouble(), 400, 470);
        Assert.Equal(EngineerName, body.GetProperty("updated_by_name").GetString());

        var stored = await StoredAsync(poleId);
        Assert.Equal((target.Lat, target.Lng), (stored.Lat, stored.Lng));

        var audit = Assert.Single(await AuditsAsync(poleId));
        Assert.Equal(AuditAction.Relocated, audit.Action);
        Assert.Equal(AuditActorKind.User, audit.ActorKind);
        Assert.Equal(UserRole.FieldEngineer, audit.ActorRole);
        Assert.Equal(body.GetProperty("updated_by").GetString(), audit.ActorUserId);
        Assert.Equal(fixture.CommuneId, audit.CommuneId);
        var before = JsonDocument.Parse(audit.BeforeState!).RootElement;
        var after = JsonDocument.Parse(audit.AfterState!).RootElement;
        Assert.Equal((Lat, PoleLng), (before.GetProperty("lat").GetDouble(), before.GetProperty("lng").GetDouble()));
        Assert.Equal(target, (after.GetProperty("lat").GetDouble(), after.GetProperty("lng").GetDouble()));
        Assert.Equal(body.GetProperty("moved_m").GetDouble(), after.GetProperty("moved_m").GetDouble());
    }

    [Fact]
    public async Task A_manager_may_move_it_off_the_road_inside_the_corridor()
    {
        var poleId = await NewPoleAsync(fixture.CommuneId, fixture.CommuneId);

        var response = await (await fixture.ManagerClientAsync()).PutAsJsonAsync(LocationUrl(poleId), new { lat = Lat + 15 * DegreesPerMetre, lng = PoleLng });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.InRange((await Json(response)).GetProperty("moved_m").GetDouble(), 13, 17);
        Assert.Single(await AuditsAsync(poleId));
    }

    /// <summary>A drop onto another road, or into a field — the survey would never see the pole there again.</summary>
    [Fact]
    public async Task A_position_beyond_the_survey_corridor_is_409_and_writes_nothing()
    {
        var poleId = await NewPoleAsync(fixture.CommuneId, fixture.CommuneId);
        var before = await StoredAsync(poleId);

        var response = await (await fixture.FieldEngineerClientAsync()).PutAsJsonAsync(LocationUrl(poleId), new { lat = Lat + 40 * DegreesPerMetre, lng = PoleLng });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var error = (await Json(response)).GetProperty("error");
        Assert.Equal(ErrorCodes.PoleOffSegment, error.GetProperty("code").GetString());
        Assert.Equal(25, error.GetProperty("details").GetProperty("corridor_m").GetDouble());
        Assert.InRange(error.GetProperty("details").GetProperty("distance_m").GetDouble(), 35, 45);
        Assert.Equal(before, await StoredAsync(poleId));
        Assert.Empty(await AuditsAsync(poleId));
    }

    /// <summary>
    /// The corridor is the pole's OWN segment, and an inter_commune segment may belong to a commune the caller cannot
    /// see — the check must still find it, not 404 or 500.
    /// </summary>
    [Fact]
    public async Task The_corridor_is_measured_from_a_segment_owned_by_another_commune()
    {
        var poleId = await NewPoleAsync(poleCommune: fixture.CommuneId, segmentCommune: fixture.ForeignCommuneId);
        var engineer = await fixture.FieldEngineerClientAsync();

        var inside = await engineer.PutAsJsonAsync(LocationUrl(poleId), new { lat = Lat + 15 * DegreesPerMetre, lng = PoleLng });
        var outside = await engineer.PutAsJsonAsync(LocationUrl(poleId), new { lat = Lat + 40 * DegreesPerMetre, lng = PoleLng });

        Assert.Equal(HttpStatusCode.OK, inside.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, outside.StatusCode);
    }

    /// <summary>A drag that ends where it started is not a move: no audit, nobody becomes the last editor.</summary>
    [Fact]
    public async Task Sending_the_position_it_already_has_changes_nothing()
    {
        var poleId = await NewPoleAsync(fixture.CommuneId, fixture.CommuneId);
        var before = await StoredAsync(poleId);

        var response = await (await fixture.FieldEngineerClientAsync()).PutAsJsonAsync(LocationUrl(poleId), new { lat = Lat, lng = PoleLng });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, (await Json(response)).GetProperty("moved_m").GetDouble());
        Assert.Equal(before, await StoredAsync(poleId));
        Assert.Empty(await AuditsAsync(poleId));
    }

    [Fact]
    public async Task A_missing_or_out_of_range_coordinate_is_400()
    {
        var poleId = await NewPoleAsync(fixture.CommuneId, fixture.CommuneId);
        var engineer = await fixture.FieldEngineerClientAsync();

        foreach (var body in new object[] { new { }, new { lat = Lat }, new { lat = 91.0, lng = PoleLng }, new { lat = Lat, lng = -180.5 } })
        {
            var response = await engineer.PutAsJsonAsync(LocationUrl(poleId), body);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains(ErrorCodes.ValidationFailed, await response.Content.ReadAsStringAsync());
        }

        Assert.Empty(await AuditsAsync(poleId));
    }

    [Fact]
    public async Task A_pole_outside_the_callers_commune_is_404()
    {
        var foreignPole = await NewPoleAsync(fixture.ForeignCommuneId, fixture.ForeignCommuneId);

        var response = await (await fixture.FieldEngineerClientAsync()).PutAsJsonAsync(LocationUrl(foreignPole), new { lat = Lat, lng = Lng });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private Task<Stored> StoredAsync(string poleId)
        => fixture.QueryAsync(db => db.Set<Pole>().IgnoreQueryFilters().Where(p => p.PoleId == poleId)
            .Select(p => new Stored(p.Geom.Y, p.Geom.X, p.UpdatedBy, p.UpdatedAt)).SingleAsync());

    private Task<List<AuditEvent>> AuditsAsync(string poleId)
        => fixture.QueryAsync(db => db.Set<AuditEvent>().IgnoreQueryFilters()
            .Where(a => a.EntityType == AuditEntityType.Pole && a.EntityId == poleId).ToListAsync());

    private sealed record Stored(double Lat, double Lng, string? UpdatedBy, DateTime UpdatedAt);

    private Task<string> NewPoleAsync(string poleCommune, string segmentCommune)
        => fixture.QueryAsync(async db =>
        {
            using (db.EnterUnscopedSystemWriteBackdoor())
            {
                var segment = new RoadSegment
                {
                    SegmentName = "location probe road", RoadClass = RoadClass.InterCommune, LengthM = 1090,
                    Geom = new LineString([new Coordinate(Lng, Lat), new Coordinate(Lng + 0.01, Lat)]) { SRID = 4326 },
                    CommuneId = segmentCommune, DataSource = DataSource.PublicImagery,
                };
                db.Set<RoadSegment>().Add(segment);
                await db.SaveChangesAsync();

                var pole = new Pole
                {
                    SegmentId = segment.SegmentId, CommuneId = poleCommune,
                    Geom = new Point(PoleLng, Lat) { SRID = 4326 }, DataSource = DataSource.PublicImagery,
                };
                db.Set<Pole>().Add(pole);
                await db.SaveChangesAsync();
                return pole.PoleId;
            }
        });

    private static async Task<JsonElement> Json(HttpResponseMessage response)
        => JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
}
