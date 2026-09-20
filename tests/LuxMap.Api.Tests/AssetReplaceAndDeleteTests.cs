using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LuxMap.Modules.Assets.Entities;
using LuxMap.Persistence;
using LuxMap.Shared.Contracts.Enums;
using LuxMap.Shared.Contracts.Errors;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;

namespace LuxMap.Api.Tests;

/// <summary>
/// BE-12 — the update and delete halves of asset CRUD for road segments, feeders and poles.
/// </summary>
/// <remarks>
/// <b>PUT here is a FULL REPLACEMENT, not a patch.</b> Most of these tests exist because that has
/// consequences a reader would not guess: a body that leaves <c>feeder_id</c> out CLEARS the pole's
/// circuit, and an asset's <c>commune_id</c> is not writable at all.
/// <para>
/// The delete tests are really tests of the FOREIGN KEYS. Nothing in the service asks whether a
/// segment or feeder may go — <c>pole</c>, <c>fault</c> and <c>fault_cluster</c> hold them with
/// RESTRICT — so each case builds a real referencing row and checks the refusal arrives as a 409
/// rather than a 500, with nothing half-deleted.
/// </para>
/// <para>
/// No teardown of its own: every row these tests create is a segment, feeder or pole in one of the
/// fixture's two communes, and <see cref="AssetImportFixture"/> already deletes those four tables for
/// both. Nothing here touches <c>fault</c> or <c>lux_reading</c>, which are the two it does not clean.
/// </para>
/// </remarks>
[Collection(nameof(AssetDatabaseCollection))]
public sealed class AssetReplaceAndDeleteTests(AssetImportFixture fixture)
{
    private const string Segments = "/api/v1/assets/segments";
    private const string Feeders = "/api/v1/assets/feeders";
    private const string Poles = "/api/v1/assets/poles";

    // ── Segment replacement ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Replacing_a_segment_overwrites_every_writable_field()
    {
        var client = await fixture.AdminClientAsync();
        var segmentId = await NewSegmentAsync(fixture.CommuneId);

        var response = await client.PutAsJsonAsync($"{Segments}/{segmentId}", new
        {
            external_ref = "INV-SEG-9",
            segment_name = "renamed road",
            road_class = "inter_commune",
            length_m = 4242,
            geom_wkt = "LINESTRING(106.60 10.90, 106.61 10.91)",
            data_source = "calibration_rig",
        });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var stored = await SegmentAsync(segmentId);
        Assert.Equal("INV-SEG-9", stored.ExternalRef);
        Assert.Equal("renamed road", stored.SegmentName);
        Assert.Equal(RoadClass.InterCommune, stored.RoadClass);
        Assert.Equal(4242, stored.LengthM);
        Assert.Equal(DataSource.CalibrationRig, stored.DataSource);
    }

    /// <summary>
    /// <c>length_m</c> is DECLARED, so the replacement keeps the number the body sent even when the
    /// geometry says something else entirely.
    /// </summary>
    /// <remarks>
    /// BE-10 rule 4: deriving it with <c>ST_Length</c> would make the figure the front end shows drift
    /// by about 73 ppm from the declared one for reasons nobody could explain. The geometry below is
    /// roughly 1.5 km of road carrying a declared length of 7 metres — absurd on purpose, so that a
    /// future "fix" that recomputes the value cannot pass this test by coincidence.
    /// </remarks>
    [Fact]
    public async Task The_declared_length_survives_a_geometry_that_disagrees_with_it()
    {
        var client = await fixture.AdminClientAsync();
        var segmentId = await NewSegmentAsync(fixture.CommuneId);

        var response = await client.PutAsJsonAsync($"{Segments}/{segmentId}", new
        {
            segment_name = "declared not derived",
            road_class = "inter_village",
            length_m = 7,
            geom_wkt = "LINESTRING(106.40 10.90, 106.41 10.91)",
            data_source = "public_imagery",
        });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(7, (await SegmentAsync(segmentId)).LengthM);
    }

    /// <summary>
    /// A replacement that keeps the segment's own inventory code must not collide with itself.
    /// </summary>
    /// <remarks>
    /// The uniqueness check looks for the code anywhere in the commune, and the row being updated is
    /// in the commune — so without excluding the row's current value, renaming anything else about a
    /// segment that HAS an <c>external_ref</c> would answer 409 forever. Nothing else covers this:
    /// the create path can never hit it.
    /// </remarks>
    [Fact]
    public async Task A_segment_may_keep_the_inventory_code_it_already_holds()
    {
        var client = await fixture.AdminClientAsync();
        var segmentId = await NewSegmentAsync(fixture.CommuneId, externalRef: "INV-KEEP-1");

        var response = await client.PutAsJsonAsync($"{Segments}/{segmentId}", new
        {
            external_ref = "INV-KEEP-1",
            segment_name = "same code, new name",
            road_class = "inter_village",
            length_m = 120,
            geom_wkt = "LINESTRING(106.49 10.97, 106.50 10.98)",
            data_source = "public_imagery",
        });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal("same code, new name", (await SegmentAsync(segmentId)).SegmentName);
    }

    [Fact]
    public async Task A_segment_may_not_take_an_inventory_code_another_segment_in_the_commune_holds()
    {
        var client = await fixture.AdminClientAsync();
        await NewSegmentAsync(fixture.CommuneId, externalRef: "INV-TAKEN-1");
        var segmentId = await NewSegmentAsync(fixture.CommuneId);

        var response = await client.PutAsJsonAsync($"{Segments}/{segmentId}", new
        {
            external_ref = "INV-TAKEN-1",
            segment_name = "thief",
            road_class = "inter_village",
            length_m = 120,
            geom_wkt = "LINESTRING(106.49 10.97, 106.50 10.98)",
            data_source = "public_imagery",
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(ErrorCodes.ExternalRefTaken, (await ReadErrorAsync(response)).GetProperty("code").GetString());
    }

    /// <summary>A segment in another commune answers <b>404</b>, not 403 (Contract section 7).</summary>
    /// <remarks>
    /// The query filter makes the row invisible, so "it is not there" is the honest answer. A 403
    /// would confirm the id exists to somebody who may not know that.
    /// </remarks>
    [Fact]
    public async Task Replacing_a_segment_in_another_commune_is_404_rather_than_403()
    {
        var client = await fixture.AdminClientAsync();
        var foreign = await NewSegmentAsync(fixture.ForeignCommuneId);

        var response = await client.PutAsJsonAsync($"{Segments}/{foreign}", new
        {
            segment_name = "reach across",
            road_class = "inter_village",
            length_m = 120,
            geom_wkt = "LINESTRING(106.49 10.97, 106.50 10.98)",
            data_source = "public_imagery",
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(ErrorCodes.AssetNotFound, (await ReadErrorAsync(response)).GetProperty("code").GetString());
    }

    // ── Segment delete ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Deleting_a_segment_nothing_references_succeeds()
    {
        var client = await fixture.AdminClientAsync();
        var segmentId = await NewSegmentAsync(fixture.CommuneId);

        var response = await client.DeleteAsync($"{Segments}/{segmentId}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(0, await CountAsync<RoadSegment>(s => s.SegmentId == segmentId));
    }

    [Fact]
    public async Task A_segment_that_still_carries_a_pole_cannot_be_deleted()
    {
        var client = await fixture.AdminClientAsync();
        var segmentId = await NewSegmentAsync(fixture.CommuneId);
        await NewPoleAsync(fixture.CommuneId, segmentId, feederId: null);

        var response = await client.DeleteAsync($"{Segments}/{segmentId}");
        var error = await ReadErrorAsync(response);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(ErrorCodes.AssetInUse, error.GetProperty("code").GetString());

        // The database's own answer, not a count taken afterwards that could disagree with it.
        Assert.Equal("pole", error.GetProperty("details").GetProperty("table").GetString());
        Assert.Equal(1, await CountAsync<RoadSegment>(s => s.SegmentId == segmentId));
    }

    [Fact]
    public async Task Deleting_a_segment_that_does_not_exist_is_404()
    {
        var client = await fixture.AdminClientAsync();

        var response = await client.DeleteAsync($"{Segments}/SEG-000000");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(ErrorCodes.AssetNotFound, (await ReadErrorAsync(response)).GetProperty("code").GetString());
    }

    // ── Feeder replacement and delete ─────────────────────────────────────────────────────

    [Fact]
    public async Task Replacing_a_feeder_overwrites_its_name_and_cable_route()
    {
        var client = await fixture.AdminClientAsync();
        var feederId = await NewFeederAsync(fixture.CommuneId);

        var response = await client.PutAsJsonAsync($"{Feeders}/{feederId}", new
        {
            external_ref = "INV-FDR-3",
            feeder_name = "cabinet renamed",
            geom_wkt = "LINESTRING(106.70 10.80, 106.71 10.81)",
        });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var stored = await FeederAsync(feederId);
        Assert.Equal("INV-FDR-3", stored.ExternalRef);
        Assert.Equal("cabinet renamed", stored.FeederName);
        Assert.NotNull(stored.Geom);
    }

    /// <summary>
    /// A feeder's cable route is optional, so a replacement may drop it.
    /// </summary>
    /// <remarks>
    /// Branch C surveyed no cable routes, so "no geometry" is the normal state rather than missing
    /// data, and a replacement has to be able to return a feeder to it.
    /// </remarks>
    [Fact]
    public async Task Replacing_a_feeder_without_a_geometry_clears_the_cable_route()
    {
        var client = await fixture.AdminClientAsync();
        var feederId = await NewFeederAsync(fixture.CommuneId, withGeometry: true);

        var response = await client.PutAsJsonAsync($"{Feeders}/{feederId}", new
        {
            feeder_name = "route forgotten",
        });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Null((await FeederAsync(feederId)).Geom);
    }

    /// <summary>
    /// A feeder with poles on it is REFUSED, and the poles keep their circuit.
    /// </summary>
    /// <remarks>
    /// <c>pole.feeder_id</c> is nullable AND Restrict, which is the combination worth pinning: were it
    /// <c>SetNull</c>, deleting a cabinet would quietly unwire every pole on it and the topology BE-13
    /// and CV-15 depend on would be gone with no record of who removed it.
    /// </remarks>
    [Fact]
    public async Task A_feeder_with_poles_wired_to_it_cannot_be_deleted_and_they_stay_wired()
    {
        var client = await fixture.AdminClientAsync();
        var segmentId = await NewSegmentAsync(fixture.CommuneId);
        var feederId = await NewFeederAsync(fixture.CommuneId);
        var poleId = await NewPoleAsync(fixture.CommuneId, segmentId, feederId);

        var response = await client.DeleteAsync($"{Feeders}/{feederId}");
        var error = await ReadErrorAsync(response);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(ErrorCodes.AssetInUse, error.GetProperty("code").GetString());
        Assert.Equal(1, await CountAsync<Feeder>(f => f.FeederId == feederId));
        Assert.Equal(feederId, await FeederOfAsync(poleId));
    }

    [Fact]
    public async Task Deleting_a_feeder_nothing_is_wired_to_succeeds()
    {
        var client = await fixture.AdminClientAsync();
        var feederId = await NewFeederAsync(fixture.CommuneId);

        var response = await client.DeleteAsync($"{Feeders}/{feederId}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(0, await CountAsync<Feeder>(f => f.FeederId == feederId));
    }

    // ── Pole replacement ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// 🔴 The sharp edge of PUT: a body with no <c>feeder_id</c> CLEARS the pole's circuit.
    /// </summary>
    /// <remarks>
    /// This is not a bug to be fixed by making the field sticky — it is what a full replacement means,
    /// and it is pinned here so the behaviour is a decision on the record rather than a surprise
    /// somebody discovers in production. A caller that wants to change only the circuit has
    /// <c>PUT /assets/poles/{id}/feeder</c>, which refuses a body that omits the key.
    /// </remarks>
    [Fact]
    public async Task Replacing_a_pole_without_a_feeder_id_clears_its_circuit()
    {
        var client = await fixture.AdminClientAsync();
        var segmentId = await NewSegmentAsync(fixture.CommuneId);
        var feederId = await NewFeederAsync(fixture.CommuneId);
        var poleId = await NewPoleAsync(fixture.CommuneId, segmentId, feederId);

        Assert.Equal(feederId, await FeederOfAsync(poleId));

        var response = await client.PutAsJsonAsync($"{Poles}/{poleId}", new
        {
            segment_id = segmentId,
            geom_wkt = "POINT(106.49 10.97)",
            data_source = "public_imagery",
        });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Null(await FeederOfAsync(poleId));
    }

    /// <summary>
    /// A pole may not be moved onto a feeder in another commune — <b>409</b>, not 403.
    /// </summary>
    /// <remarks>
    /// The caller here holds BOTH communes, so nothing is forbidden to them; the two rows simply may
    /// not be joined (BE-REVIEW-02, D-5). With a single-commune client the foreign feeder would be
    /// invisible and the answer would be 404 instead, which is a different statement.
    /// <para>
    /// <c>CommuneWriteGuard</c> cannot catch this: it reads the commune OF THE ROW BEING WRITTEN — the
    /// pole's own, which is in scope — so the check in the service is the only thing standing here.
    /// This test is what proves the replacement path calls it, not just the create path.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_pole_may_not_be_moved_onto_a_feeder_in_another_commune()
    {
        var client = await fixture.BothCommunesClientAsync();
        var segmentId = await NewSegmentAsync(fixture.CommuneId);
        var poleId = await NewPoleAsync(fixture.CommuneId, segmentId, feederId: null);
        var foreignFeeder = await NewFeederAsync(fixture.ForeignCommuneId);

        var response = await client.PutAsJsonAsync($"{Poles}/{poleId}", new
        {
            segment_id = segmentId,
            feeder_id = foreignFeeder,
            geom_wkt = "POINT(106.49 10.97)",
            data_source = "public_imagery",
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(
            ErrorCodes.CrossCommuneReference,
            (await ReadErrorAsync(response)).GetProperty("code").GetString());

        Assert.Null(await FeederOfAsync(poleId));
    }

    /// <summary>
    /// A pole MAY be moved onto a segment owned by another commune.
    /// </summary>
    /// <remarks>
    /// The mirror of the feeder rule, and the asymmetry is deliberate: <c>road_class =
    /// inter_commune</c> means the road runs BETWEEN communes, so a pole sitting in a different
    /// commune from the segment's owner is legitimate. Only the electrical circuit has to match
    /// (BE-REVIEW-02, constraint 1). Pinned so nobody "fixes" the segment path for symmetry.
    /// </remarks>
    [Fact]
    public async Task A_pole_may_sit_on_a_segment_owned_by_another_commune()
    {
        var client = await fixture.BothCommunesClientAsync();
        var homeSegment = await NewSegmentAsync(fixture.CommuneId);
        var poleId = await NewPoleAsync(fixture.CommuneId, homeSegment, feederId: null);
        var foreignSegment = await NewSegmentAsync(fixture.ForeignCommuneId);

        var response = await client.PutAsJsonAsync($"{Poles}/{poleId}", new
        {
            segment_id = foreignSegment,
            geom_wkt = "POINT(106.49 10.97)",
            data_source = "public_imagery",
        });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var stored = await fixture.QueryAsync(db => db.Set<Pole>().IgnoreQueryFilters().AsNoTracking()
            .Where(p => p.PoleId == poleId).Select(p => new { p.SegmentId, p.CommuneId }).SingleAsync());

        Assert.Equal(foreignSegment, stored.SegmentId);
        Assert.Equal(fixture.CommuneId, stored.CommuneId);
    }

    [Fact]
    public async Task A_pole_may_not_be_moved_onto_a_segment_the_caller_cannot_see()
    {
        var client = await fixture.AdminClientAsync();
        var segmentId = await NewSegmentAsync(fixture.CommuneId);
        var poleId = await NewPoleAsync(fixture.CommuneId, segmentId, feederId: null);
        var invisible = await NewSegmentAsync(fixture.ForeignCommuneId);

        var response = await client.PutAsJsonAsync($"{Poles}/{poleId}", new
        {
            segment_id = invisible,
            geom_wkt = "POINT(106.49 10.97)",
            data_source = "public_imagery",
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(ErrorCodes.AssetNotFound, (await ReadErrorAsync(response)).GetProperty("code").GetString());
    }

    // ── Permissions ───────────────────────────────────────────────────────────────────────

    /// <summary>
    /// All five new endpoints are administrator-only, and refuse with <c>ROLE_FORBIDDEN</c>.
    /// </summary>
    /// <remarks>
    /// Asserting the CODE, not just the 403: before BE-REVIEW-02 D-4 every bare 403 came back as
    /// <c>COMMUNE_FORBIDDEN</c> and the front end would tell an engineer they were outside their area
    /// when the real answer is that their role may not write assets.
    /// </remarks>
    [Theory]
    [InlineData("PUT", Segments)]
    [InlineData("PUT", Feeders)]
    [InlineData("PUT", Poles)]
    [InlineData("DELETE", Segments)]
    [InlineData("DELETE", Feeders)]
    public async Task A_maintenance_engineer_may_not_replace_or_delete_an_asset(string method, string route)
    {
        var client = await fixture.SeededClientAsync("engineer", "SEED_ENGINEER_PASSWORD");

        using var request = new HttpRequestMessage(new HttpMethod(method), $"{route}/SEG-000000");

        if (method == "PUT")
        {
            request.Content = JsonContent.Create(new { segment_name = "nope", feeder_name = "nope" });
        }

        var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains(ErrorCodes.RoleForbidden, body);
        Assert.DoesNotContain(ErrorCodes.CommuneForbidden, body);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────────────

    private static async Task<JsonElement> ReadErrorAsync(HttpResponseMessage response)
        => JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("error");

    private Task<RoadSegment> SegmentAsync(string segmentId)
        => fixture.QueryAsync(db => db.Set<RoadSegment>().IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(s => s.SegmentId == segmentId));

    private Task<Feeder> FeederAsync(string feederId)
        => fixture.QueryAsync(db => db.Set<Feeder>().IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(f => f.FeederId == feederId));

    private Task<string?> FeederOfAsync(string poleId)
        => fixture.QueryAsync(db => db.Set<Pole>().IgnoreQueryFilters().AsNoTracking()
            .Where(p => p.PoleId == poleId).Select(p => p.FeederId).SingleAsync());

    private Task<int> CountAsync<TEntity>(System.Linq.Expressions.Expression<Func<TEntity, bool>> predicate)
        where TEntity : class
        => fixture.QueryAsync(db => db.Set<TEntity>().IgnoreQueryFilters().CountAsync(predicate));

    /// <summary>
    /// Builds rows straight through the DbContext, as the system.
    /// </summary>
    /// <remarks>
    /// Not over HTTP: several of these assets live in a commune the test's own account cannot write
    /// to, which is the point of those cases. The backdoor is spelled the way it is so it shows in a
    /// diff.
    /// </remarks>
    private Task<string> NewSegmentAsync(string communeId, string? externalRef = null)
        => fixture.QueryAsync(async db =>
        {
            using (db.EnterUnscopedSystemWriteBackdoor())
            {
                var segment = new RoadSegment
                {
                    SegmentName = "replace probe",
                    RoadClass = RoadClass.InterVillage,
                    LengthM = 100,
                    Geom = new LineString([new Coordinate(106.49, 10.97), new Coordinate(106.50, 10.98)]) { SRID = 4326 },
                    CommuneId = communeId,
                    DataSource = DataSource.PublicImagery,
                    ExternalRef = externalRef,
                };

                db.Set<RoadSegment>().Add(segment);
                await db.SaveChangesAsync();
                return segment.SegmentId;
            }
        });

    private Task<string> NewFeederAsync(string communeId, bool withGeometry = false)
        => fixture.QueryAsync(async db =>
        {
            using (db.EnterUnscopedSystemWriteBackdoor())
            {
                var feeder = new Feeder
                {
                    FeederName = "replace probe cabinet",
                    CommuneId = communeId,
                    Geom = withGeometry
                        ? new LineString([new Coordinate(106.49, 10.97), new Coordinate(106.50, 10.98)]) { SRID = 4326 }
                        : null,
                };

                db.Set<Feeder>().Add(feeder);
                await db.SaveChangesAsync();
                return feeder.FeederId;
            }
        });

    private Task<string> NewPoleAsync(string communeId, string segmentId, string? feederId)
        => fixture.QueryAsync(async db =>
        {
            using (db.EnterUnscopedSystemWriteBackdoor())
            {
                var pole = new Pole
                {
                    SegmentId = segmentId,
                    FeederId = feederId,
                    CommuneId = communeId,
                    Geom = new Point(106.49, 10.97) { SRID = 4326 },
                    DataSource = DataSource.PublicImagery,
                };

                db.Set<Pole>().Add(pole);
                await db.SaveChangesAsync();
                return pole.PoleId;
            }
        });
}
