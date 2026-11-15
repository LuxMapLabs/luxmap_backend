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
/// ONE-FIXTURE (drift OF-1, restoring BE-REVIEW-02 D-11 after the MULTI-FIXTURE window) — a pole carries ONE fitting
/// in service; retired fittings are history and do not count. Three paths, one rule: CRUD, import, and the database.
/// </summary>
/// <remarks>
/// Every case is sabotaged both ways: the second fitting is refused AND the legitimate replacement (retire first,
/// then install) and the already-retired history row go through. A check that refused every fitting would pass half
/// of each test and nobody would notice until the first replacement.
/// </remarks>
[Collection(nameof(AssetDatabaseCollection))]
public sealed class FixtureCardinalityTests(AssetImportFixture fixture)
{
    private const string FixtureRoute = "/api/v1/assets/fixtures";

    // ── CRUD ───────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_second_fitting_in_service_is_409()
    {
        var client = await fixture.ManagerClientAsync();
        var poleId = await NewPoleAsync();

        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync(FixtureRoute, LampBody(poleId))).StatusCode);

        var second = await client.PostAsJsonAsync(FixtureRoute, LampBody(poleId));
        var body = await ReadErrorAsync(second);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal(ErrorCodes.PoleHasActiveFixture, body.GetProperty("code").GetString());
        Assert.Equal(poleId, body.GetProperty("details").GetProperty("pole_id").GetString());
        Assert.Equal(1, await ActiveLampsAsync(poleId));
        Assert.Equal([(short)1], await ActiveSlotsAsync(poleId));
    }

    [Fact]
    public async Task Retiring_the_fitting_frees_the_pole_for_its_replacement()
    {
        var client = await fixture.ManagerClientAsync();
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
        Assert.Equal([(short)1], await ActiveSlotsAsync(poleId));
    }

    [Fact]
    public async Task A_historical_row_that_arrives_already_retired_may_coexist_with_the_fitting_in_service()
    {
        var client = await fixture.ManagerClientAsync();
        var poleId = await NewPoleAsync();

        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync(FixtureRoute, LampBody(poleId))).StatusCode);

        var history = await client.PostAsJsonAsync(
            FixtureRoute, LampBody(poleId, installDate: "2019-03-01", removedDate: "2022-03-01"));

        Assert.Equal(HttpStatusCode.Created, history.StatusCode);
        Assert.Equal(1, await ActiveLampsAsync(poleId));
        Assert.Equal(2, await AllLampsAsync(poleId));
    }

    // ── read side ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task The_published_list_shapes_still_carry_the_one_fitting()
    {
        var client = await fixture.ManagerClientAsync();
        var poleId = await NewPoleAsync();
        await client.PostAsJsonAsync(FixtureRoute, LampBody(poleId, lampWatt: 80, lampCount: 2));

        var detail = JsonDocument.Parse(await client.GetStringAsync($"/api/v1/map/poles/{poleId}")).RootElement;
        var lamps = detail.GetProperty("fixtures");
        Assert.Equal(1, lamps.GetArrayLength());
        Assert.Equal(80, lamps[0].GetProperty("lamp_watt").GetInt32());
        Assert.Equal(2, lamps[0].GetProperty("lamp_count").GetInt32());
        Assert.Equal(80, detail.GetProperty("fixture").GetProperty("lamp_watt").GetInt32());

        var inventory = await InventoryRowAsync(client, poleId);
        Assert.Equal(1, inventory.GetProperty("active_fixtures").GetArrayLength());
        Assert.Equal(80, inventory.GetProperty("active_fixture").GetProperty("lamp_watt").GetInt32());

        var map = JsonDocument.Parse(await client.GetStringAsync(
            "/api/v1/map/poles?bbox=106.48,10.96,106.51,10.99")).RootElement;
        var props = map.GetProperty("features").EnumerateArray()
            .Select(f => f.GetProperty("properties"))
            .Single(p => p.GetProperty("pole_id").GetString() == poleId);
        Assert.Equal(1, props.GetProperty("fixture_count").GetInt32());
        Assert.Equal(2, props.GetProperty("lamp_count").GetInt32());
    }

    [Fact]
    public async Task Replacing_the_fitting_moves_the_baseline_anchor()
    {
        var client = await fixture.ManagerClientAsync();
        var poleId = await NewPoleAsync();
        var first = await client.PostAsJsonAsync(FixtureRoute, LampBody(poleId));
        var firstId = first.Headers.Location!.ToString().Split('/').Last();
        Assert.Equal(firstId, await AnchorAsync(poleId));

        await client.PutAsJsonAsync($"{FixtureRoute}/{firstId}/removal", new { removed_date = "2026-02-01" });
        var replacement = await client.PostAsJsonAsync(FixtureRoute, LampBody(poleId, installDate: "2026-02-02"));
        var replacementId = replacement.Headers.Location!.ToString().Split('/').Last();

        Assert.Equal(replacementId, await AnchorAsync(poleId));
    }

    [Fact]
    public void First_free_slot_is_slot_one_and_runs_out_after_it()
    {
        Assert.Equal((short)1, FixtureSlots.FirstFree([]));
        Assert.Null(FixtureSlots.FirstFree([1]));
        // A stray slot-2 row from the MULTI-FIXTURE window does not occupy slot 1.
        Assert.Equal((short)1, FixtureSlots.FirstFree([2]));
    }

    // ── import ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Import_records_one_fitting_then_refuses_another_until_it_is_retired()
    {
        var client = await fixture.ManagerClientAsync();
        var tag = $"T{Guid.NewGuid():N}"[..9].ToUpperInvariant();
        var poleId = await NewPoleAsync(externalRef: $"{tag}-P1");

        const string header =
            "pole_external_ref,fixture_type,power_source,lamp_watt,install_date,removed_date,warranty_expiry,data_source";
        var row = $"{tag}-P1,led_road_lamp,grid,100,2026-02-01,,,public_imagery";

        // Two active rows for one pole in a single file: the first takes the pole, the second is refused.
        var filled = await AssetImportTests.ImportAsync(client, "fixtures", "fixtures.csv", header + $"\n{row}\n{row}");
        Assert.Equal(1, filled.GetProperty("inserted").GetInt32());
        Assert.Equal(1, filled.GetProperty("failed").GetInt32());
        Assert.Contains("already has a fitting in service", filled.GetProperty("rows")[0].GetProperty("message").GetString()!);
        Assert.Equal([(short)1], await ActiveSlotsAsync(poleId));

        // With the fitting already in the database, one more is refused.
        var refused = await AssetImportTests.ImportAsync(client, "fixtures", "fixtures.csv", header + $"\n{row}");
        Assert.Equal(0, refused.GetProperty("inserted").GetInt32());
        Assert.Equal(1, refused.GetProperty("failed").GetInt32());

        // Retire it as the system, and the same file goes through as the replacement.
        await fixture.QueryAsync(async db =>
        {
            using (db.EnterUnscopedSystemWriteBackdoor())
            {
                var lamp = await db.Set<Fixture>().IgnoreQueryFilters()
                    .SingleAsync(candidate => candidate.PoleId == poleId && candidate.RemovedDate == null);
                lamp.RemovedDate = new DateOnly(2026, 3, 1);
                return await db.SaveChangesAsync();
            }
        });

        var replaced = await AssetImportTests.ImportAsync(client, "fixtures", "fixtures.csv", header + $"\n{row}");
        Assert.Equal(1, replaced.GetProperty("inserted").GetInt32());
        Assert.Equal([(short)1], await ActiveSlotsAsync(poleId));
    }

    // ── database ───────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The rule holds with no application code in the way: a second active row in slot 1 trips
    /// <c>ux_fixture_pole_slot_active</c>, an active row in slot 2 trips <c>ck_fixture_active_slot</c>, and
    /// <c>ck_fixture_slot</c> still bounds every row.
    /// </summary>
    [Fact]
    public async Task The_database_allows_one_fitting_in_service_and_keeps_slot_two_for_history_only()
    {
        var poleId = await NewPoleAsync();

        await WriteLampAsync(poleId, removedDate: null, slot: 1);

        var repeated = await Assert.ThrowsAsync<DbUpdateException>(() => WriteLampAsync(poleId, removedDate: null, slot: 1));
        Assert.Contains("ux_fixture_pole_slot_active", repeated.InnerException?.Message);

        var secondSlot = await Assert.ThrowsAsync<DbUpdateException>(() => WriteLampAsync(poleId, removedDate: null, slot: 2));
        Assert.Contains("ck_fixture_active_slot", secondSlot.InnerException?.Message);

        var thirdSlot = await Assert.ThrowsAsync<DbUpdateException>(
            () => WriteLampAsync(poleId, removedDate: new DateOnly(2020, 1, 1), slot: 3));
        Assert.Contains("ck_fixture_slot", thirdSlot.InnerException?.Message);

        // Sabotage the other way: retired rows in slot 1 or slot 2 are history, not a collision.
        await WriteLampAsync(poleId, removedDate: new DateOnly(2020, 1, 1), slot: 1);
        await WriteLampAsync(poleId, removedDate: new DateOnly(2020, 1, 1), slot: 2);
        Assert.Equal(3, await AllLampsAsync(poleId));
    }

    // ── helpers ────────────────────────────────────────────────────────────────────────────

    private static object LampBody(
        string poleId, string installDate = "2026-01-04", string? removedDate = null, int lampWatt = 100,
        int lampCount = 1) => new
    {
        pole_id = poleId,
        fixture_type = "led_road_lamp",
        power_source = "grid",
        lamp_watt = lampWatt,
        lamp_count = lampCount,
        install_date = installDate,
        removed_date = removedDate,
        data_source = "public_imagery",
    };

    private static async Task<JsonElement> InventoryRowAsync(HttpClient client, string poleId)
    {
        for (var page = 1; ; page++)
        {
            var body = JsonDocument.Parse(await client.GetStringAsync($"/api/v1/assets/poles?page={page}&page_size=200")).RootElement;
            var items = body.GetProperty("items").EnumerateArray().ToList();
            if (items.FirstOrDefault(item => item.GetProperty("pole_id").GetString() == poleId) is { ValueKind: JsonValueKind.Object } found)
            {
                return found;
            }

            Assert.True(items.Count > 0, $"{poleId} is not in the inventory list.");
        }
    }

    private static async Task<JsonElement> ReadErrorAsync(HttpResponseMessage response)
        => JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("error");

    private Task<int> ActiveLampsAsync(string poleId)
        => fixture.QueryAsync(db => db.Set<Fixture>().IgnoreQueryFilters()
            .CountAsync(lamp => lamp.PoleId == poleId && lamp.RemovedDate == null));

    private Task<int> AllLampsAsync(string poleId)
        => fixture.QueryAsync(db => db.Set<Fixture>().IgnoreQueryFilters()
            .CountAsync(lamp => lamp.PoleId == poleId));

    private Task<List<short>> ActiveSlotsAsync(string poleId)
        => fixture.QueryAsync(db => db.Set<Fixture>().IgnoreQueryFilters()
            .Where(lamp => lamp.PoleId == poleId && lamp.RemovedDate == null)
            .OrderBy(lamp => lamp.Slot).Select(lamp => lamp.Slot).ToListAsync());

    private Task<string?> AnchorAsync(string poleId)
        => fixture.QueryAsync(db => db.Set<Fixture>().IgnoreQueryFilters()
            .Where(lamp => lamp.PoleId == poleId && lamp.RemovedDate == null)
            .NewestFirst().Select(lamp => lamp.FixtureId).FirstOrDefaultAsync());

    private Task<int> WriteLampAsync(string poleId, DateOnly? removedDate, short slot)
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
                    Slot = slot,
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
