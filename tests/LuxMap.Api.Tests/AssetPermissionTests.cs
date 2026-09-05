using System.Net;
using System.Net.Http.Json;
using System.Text;
using LuxMap.Modules.Assets.Entities;
using LuxMap.Persistence;
using LuxMap.Shared.Authorization;
using LuxMap.Shared.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NetTopologySuite.Geometries;

namespace LuxMap.Api.Tests;

/// <summary>
/// BE-12a — who may write assets, and who may read them. The FIRST production use of the four BE-08
/// role policies.
/// </summary>
/// <remarks>
/// The read cases matter as much as the write ones. A policy is one EXACT role claim, not a rank, so
/// putting <c>MaintenanceEngineer</c> on a GET would lock out administrators and the managing
/// authority — a mistake that looks like tightening security and is actually a denial of service to
/// two of the four roles. These tests pin the asymmetry so nobody "tidies it up" later.
/// </remarks>
[Collection(nameof(AssetImportCollection))]
public sealed class AssetPermissionTests(AssetImportFixture fixture)
{
    [Fact]
    public async Task A_maintenance_engineer_may_NOT_create_an_asset()
    {
        var client = await fixture.SeededClientAsync("engineer", "SEED_ENGINEER_PASSWORD");

        var response = await client.PostAsJsonAsync("/api/v1/assets/segments", NewSegment());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task A_maintenance_engineer_may_NOT_import()
    {
        var client = await fixture.SeededClientAsync("engineer", "SEED_ENGINEER_PASSWORD");

        using var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent(Encoding.UTF8.GetBytes("a,b\n1,2")), "file", "segments.csv");

        var response = await client.PostAsync("/api/v1/assets/import/segments", content);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task A_maintenance_engineer_MAY_read()
    {
        var client = await fixture.SeededClientAsync("engineer", "SEED_ENGINEER_PASSWORD");

        var response = await client.GetAsync("/api/v1/assets/poles");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task The_managing_authority_MAY_read_which_proves_reads_carry_no_role_policy()
    {
        var client = await fixture.SeededClientAsync("agency", "SEED_AGENCY_PASSWORD");

        var response = await client.GetAsync("/api/v1/assets/segments");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task A_field_crew_member_may_read_but_not_write()
    {
        var client = await fixture.SeededClientAsync("crew", "SEED_CREW_PASSWORD");

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/assets/feeders")).StatusCode);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await client.PostAsJsonAsync("/api/v1/assets/feeders", new { feeder_name = "x", commune_id = fixture.CommuneId })).StatusCode);
    }

    [Fact]
    public async Task An_administrator_creates_an_asset_and_gets_201_with_a_Location_header_and_no_body()
    {
        var client = await fixture.AdminClientAsync();

        var response = await client.PostAsJsonAsync("/api/v1/assets/segments", NewSegment());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        // A3: no entity body until BE-12b settles the read shape. The id travels in the header.
        Assert.NotNull(response.Headers.Location);
        Assert.Contains("/api/v1/assets/segments/SEG-", response.Headers.Location!.ToString());
        Assert.Empty(await response.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task An_administrator_imports_and_gets_200_with_the_import_result()
    {
        var client = await fixture.AdminClientAsync();
        var tag = $"P{Guid.NewGuid():N}"[..9].ToUpperInvariant();

        var result = await AssetImportTests.ImportAsync(client, "segments", "segments.csv",
            "external_ref,segment_name,road_class,length_m,geom_wkt,commune_id,data_source"
            + $"\n{tag},Tuyen,inter_commune,900,\"LINESTRING(106.49 10.97, 106.50 10.98)\","
            + $"{fixture.CommuneId},public_imagery");

        Assert.Equal(1, result.GetProperty("inserted").GetInt32());
    }

    [Fact]
    public async Task Anonymous_callers_are_refused_before_any_role_is_considered()
    {
        var client = fixture.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/assets/poles")).StatusCode);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync("/api/v1/assets/segments", NewSegment())).StatusCode);
    }

    [Fact]
    public async Task Creating_an_asset_for_a_commune_outside_the_scope_is_403_naming_that_commune()
    {
        var client = await fixture.AdminClientAsync();

        var response = await client.PostAsJsonAsync(
            "/api/v1/assets/segments", NewSegment(fixture.ForeignCommuneId));

        var body = await response.Content.ReadAsStringAsync();

        // CommuneFilter.Narrow at the entry point, so the caller learns WHICH commune was refused.
        // The query filter alone would have answered 200 with nothing in it.
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("COMMUNE_FORBIDDEN", body);
        Assert.Contains(fixture.ForeignCommuneId, body);
    }

    /// <summary>
    /// The write guard, reached directly rather than over HTTP.
    /// </summary>
    /// <remarks>
    /// ⚠️ Deliberately NOT an HTTP test, and the reason is worth stating: in BE-12a every write path
    /// checks the scope at the entry point first, so the guard can never be the thing that answers.
    /// That is the intended layering — a 403 naming the commune beats a backstop — but it means an
    /// HTTP test would prove the entry-point check, not the guard. This one goes through
    /// <c>SaveChanges</c> to prove the backstop is really attached to the asset tables, so a future
    /// endpoint that forgets the entry-point check still cannot write across communes.
    /// </remarks>
    [Fact]
    public async Task The_write_guard_still_refuses_a_pole_for_a_foreign_commune_even_with_no_entry_point_check()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LuxMapDbContext>();

        var segmentId = await fixture.QueryAsync(async inner =>
        {
            using (inner.EnterUnscopedSystemWriteBackdoor())
            {
                var segment = new RoadSegment
                {
                    SegmentName = "guard probe",
                    RoadClass = Shared.Contracts.Enums.RoadClass.InterCommune,
                    LengthM = 100,
                    Geom = new LineString([new Coordinate(106.49, 10.97), new Coordinate(106.50, 10.98)]) { SRID = 4326 },
                    CommuneId = fixture.ForeignCommuneId,
                    DataSource = Shared.Contracts.Enums.DataSource.PublicImagery,
                };

                inner.Set<RoadSegment>().Add(segment);
                await inner.SaveChangesAsync();
                return segment.SegmentId;
            }
        });

        db.Set<Pole>().Add(new Pole
        {
            SegmentId = segmentId,
            CommuneId = fixture.ForeignCommuneId,
            Geom = new Point(106.49, 10.97) { SRID = 4326 },
            DataSource = Shared.Contracts.Enums.DataSource.PublicImagery,
        });

        // The scope here is empty — no HTTP request, so no claim — and an empty scope is a REFUSAL,
        // never a pass. That is the same empty scope an unauthenticated caller carries.
        var thrown = await Assert.ThrowsAsync<LuxMapException>(() => db.SaveChangesAsync());
        Assert.Equal("COMMUNE_FORBIDDEN", thrown.Code);

        var written = await fixture.QueryAsync(inner => inner.Set<Pole>().IgnoreQueryFilters()
            .CountAsync(pole => pole.SegmentId == segmentId));

        Assert.Equal(0, written);
    }

    private object NewSegment(string? communeId = null) => new
    {
        segment_name = "Tuyen kiem tra quyen",
        road_class = "inter_commune",
        length_m = 800,
        geom_wkt = "LINESTRING(106.49 10.97, 106.50 10.98)",
        commune_id = communeId ?? fixture.CommuneId,
        data_source = "public_imagery",
    };
}
