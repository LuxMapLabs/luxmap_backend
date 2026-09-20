using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LuxMap.Modules.Assets.Entities;
using LuxMap.Persistence;
using LuxMap.Shared.Contracts.Enums;
using LuxMap.Shared.Contracts.Errors;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NetTopologySuite.Geometries;

namespace LuxMap.Api.Tests;

/// <summary>
/// BE-REVIEW-02, D-11 — a pole carries AT MOST ONE lamp in service; retired lamps are history and do
/// not count. Three paths, one rule: CRUD, import, and the database itself.
/// </summary>
/// <remarks>
/// Every case is sabotaged both ways: the collision is refused AND the legitimate replacement (retire
/// first, then install) goes through. A check that refused every second lamp would pass half of each
/// test and nobody would notice until the first real lamp change.
/// </remarks>
[Collection(nameof(AssetDatabaseCollection))]
public sealed class FixtureCardinalityTests(AssetImportFixture fixture)
{
    private const string FixtureRoute = "/api/v1/assets/fixtures";

    // ── CRUD ───────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_second_active_lamp_on_the_same_pole_is_409_and_the_first_stays_the_only_one()
    {
        var client = await fixture.AdminClientAsync();
        var poleId = await NewPoleAsync();

        var first = await client.PostAsJsonAsync(FixtureRoute, LampBody(poleId));
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var second = await client.PostAsJsonAsync(FixtureRoute, LampBody(poleId));
        var body = await ReadErrorAsync(second);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal(ErrorCodes.PoleHasActiveFixture, body.GetProperty("code").GetString());
        Assert.Equal(poleId, body.GetProperty("details").GetProperty("pole_id").GetString());
        Assert.Equal(1, await ActiveLampsAsync(poleId));
    }

    [Fact]
    public async Task Retiring_the_lamp_first_lets_its_replacement_through()
    {
        var client = await fixture.AdminClientAsync();
        var poleId = await NewPoleAsync();

        var first = await client.PostAsJsonAsync(FixtureRoute, LampBody(poleId));
        var firstId = first.Headers.Location!.ToString().Split('/').Last();

        var retire = await client.PutAsJsonAsync(
            $"{FixtureRoute}/{firstId}/removal", new { removed_date = "2026-01-15" });
        Assert.Equal(HttpStatusCode.NoContent, retire.StatusCode);

        var replacement = await client.PostAsJsonAsync(FixtureRoute, LampBody(poleId, installDate: "2026-01-16"));

        Assert.Equal(HttpStatusCode.Created, replacement.StatusCode);
        Assert.Equal(1, await ActiveLampsAsync(poleId));
        Assert.Equal(2, await AllLampsAsync(poleId));
    }

    [Fact]
    public async Task A_historical_row_that_arrives_already_retired_may_coexist_with_the_active_lamp()
    {
        var client = await fixture.AdminClientAsync();
        var poleId = await NewPoleAsync();

        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync(FixtureRoute, LampBody(poleId))).StatusCode);

        var history = await client.PostAsJsonAsync(
            FixtureRoute, LampBody(poleId, installDate: "2019-03-01", removedDate: "2022-03-01"));

        Assert.Equal(HttpStatusCode.Created, history.StatusCode);
        Assert.Equal(1, await ActiveLampsAsync(poleId));
    }

    // ── import ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Import_accepts_a_lamp_for_a_pole_whose_previous_lamp_was_retired_and_refuses_one_that_is_still_lit()
    {
        var client = await fixture.AdminClientAsync();
        var tag = $"T{Guid.NewGuid():N}"[..9].ToUpperInvariant();
        var poleId = await NewPoleAsync(externalRef: $"{tag}-P1");

        const string header =
            "pole_external_ref,fixture_type,power_source,lamp_watt,install_date,removed_date,warranty_expiry,data_source";

        // Refused: a lamp is in service. Both rows in one file — the first is refused by the
        // database's active lamp, the second by the first.
        await client.PostAsJsonAsync(FixtureRoute, LampBody(poleId));
        var refused = await AssetImportTests.ImportAsync(client, "fixtures", "fixtures.csv",
            header + $"\n{tag}-P1,led_road_lamp,grid,100,2026-02-01,,,public_imagery");
        Assert.Equal(0, refused.GetProperty("inserted").GetInt32());
        Assert.Equal(1, refused.GetProperty("failed").GetInt32());
        Assert.Contains("in service", refused.GetProperty("rows")[0].GetProperty("message").GetString()!);

        // Retire it as the system, then the same file goes through.
        await fixture.QueryAsync(async db =>
        {
            using (db.EnterUnscopedSystemWriteBackdoor())
            {
                var lamp = await db.Set<Fixture>().IgnoreQueryFilters()
                    .SingleAsync(candidate => candidate.PoleId == poleId && candidate.RemovedDate == null);
                lamp.RemovedDate = new DateOnly(2026, 1, 31);
                return await db.SaveChangesAsync();
            }
        });

        var accepted = await AssetImportTests.ImportAsync(client, "fixtures", "fixtures.csv",
            header + $"\n{tag}-P1,led_road_lamp,grid,100,2026-02-01,,,public_imagery");
        Assert.Equal(1, accepted.GetProperty("inserted").GetInt32());
        Assert.Equal(0, accepted.GetProperty("failed").GetInt32());
        Assert.Equal(1, await ActiveLampsAsync(poleId));
    }

    // ── database ───────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The rule holds with no application code in the way: two active rows written straight through
    /// the DbContext trip <c>ux_fixture_pole_id_active</c>, and a retired second row does not.
    /// </summary>
    [Fact]
    public async Task The_partial_unique_index_refuses_a_second_active_lamp_even_when_written_as_the_system()
    {
        var poleId = await NewPoleAsync();

        await WriteLampAsync(poleId, removedDate: null);

        var error = await Assert.ThrowsAsync<DbUpdateException>(() => WriteLampAsync(poleId, removedDate: null));
        Assert.Contains("ux_fixture_pole_id_active", error.InnerException?.Message);

        // Sabotage the other way: a retired row is NOT a collision.
        await WriteLampAsync(poleId, removedDate: new DateOnly(2020, 1, 1));
        Assert.Equal(2, await AllLampsAsync(poleId));
    }

    // ── helpers ────────────────────────────────────────────────────────────────────────────

    private static object LampBody(string poleId, string installDate = "2026-01-04", string? removedDate = null) => new
    {
        pole_id = poleId,
        fixture_type = "led_road_lamp",
        power_source = "grid",
        lamp_watt = 100,
        install_date = installDate,
        removed_date = removedDate,
        data_source = "public_imagery",
    };

    private static async Task<JsonElement> ReadErrorAsync(HttpResponseMessage response)
        => JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("error");

    private Task<int> ActiveLampsAsync(string poleId)
        => fixture.QueryAsync(db => db.Set<Fixture>().IgnoreQueryFilters()
            .CountAsync(lamp => lamp.PoleId == poleId && lamp.RemovedDate == null));

    private Task<int> AllLampsAsync(string poleId)
        => fixture.QueryAsync(db => db.Set<Fixture>().IgnoreQueryFilters()
            .CountAsync(lamp => lamp.PoleId == poleId));

    private Task<int> WriteLampAsync(string poleId, DateOnly? removedDate)
        => fixture.QueryAsync(async db =>
        {
            using (db.EnterUnscopedSystemWriteBackdoor())
            {
                db.Set<Fixture>().Add(new Fixture
                {
                    PoleId = poleId,
                    CommuneId = fixture.CommuneId,
                    FixtureType = FixtureType.LedRoadLamp,
                    PowerSource = PowerSource.Grid,
                    LampWatt = 100,
                    InstallDate = new DateOnly(2019, 1, 1),
                    RemovedDate = removedDate,
                    DataSource = DataSource.PublicImagery,
                });
                return await db.SaveChangesAsync();
            }
        });

    /// <summary>A segment and a pole in the fixture's commune, built as the system.</summary>
    private Task<string> NewPoleAsync(string? externalRef = null)
        => fixture.QueryAsync(async db =>
        {
            using (db.EnterUnscopedSystemWriteBackdoor())
            {
                var segment = new RoadSegment
                {
                    SegmentName = "fixture cardinality probe",
                    RoadClass = RoadClass.InterVillage,
                    LengthM = 100,
                    Geom = new LineString([new Coordinate(106.49, 10.97), new Coordinate(106.50, 10.98)]) { SRID = 4326 },
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
                    Geom = new Point(106.49, 10.97) { SRID = 4326 },
                    DataSource = DataSource.PublicImagery,
                };
                db.Set<Pole>().Add(pole);
                await db.SaveChangesAsync();
                return pole.PoleId;
            }
        });
}
