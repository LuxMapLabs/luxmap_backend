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
/// MULTI-FIXTURE (drift MF-1, replacing BE-REVIEW-02 D-11) — a pole carries AT MOST TWO lamps in service, one per
/// slot; retired lamps are history and do not count. Three paths, one rule: CRUD, import, and the database itself.
/// </summary>
/// <remarks>
/// Every case is sabotaged both ways: the third lamp is refused AND the second one, and the legitimate replacement
/// (retire first, then install), go through. A check that refused every second lamp — the old rule — would pass
/// half of each test and nobody would notice until the first double-arm pole.
/// </remarks>
[Collection(nameof(AssetDatabaseCollection))]
public sealed class FixtureCardinalityTests(AssetImportFixture fixture)
{
    private const string FixtureRoute = "/api/v1/assets/fixtures";

    // ── CRUD ───────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_second_active_lamp_is_accepted_and_a_third_is_409()
    {
        var client = await fixture.ManagerClientAsync();
        var poleId = await NewPoleAsync();

        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync(FixtureRoute, LampBody(poleId))).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync(FixtureRoute, LampBody(poleId))).StatusCode);

        var third = await client.PostAsJsonAsync(FixtureRoute, LampBody(poleId));
        var body = await ReadErrorAsync(third);

        Assert.Equal(HttpStatusCode.Conflict, third.StatusCode);
        Assert.Equal(ErrorCodes.PoleHasActiveFixture, body.GetProperty("code").GetString());
        Assert.Equal(poleId, body.GetProperty("details").GetProperty("pole_id").GetString());
        Assert.Equal(2, await ActiveLampsAsync(poleId));
        Assert.Equal([(short)1, (short)2], await ActiveSlotsAsync(poleId));
    }

    [Fact]
    public async Task Retiring_one_of_two_lamps_frees_its_slot_for_the_replacement()
    {
        var client = await fixture.ManagerClientAsync();
        var poleId = await NewPoleAsync();

        var first = await client.PostAsJsonAsync(FixtureRoute, LampBody(poleId));
        var firstId = first.Headers.Location!.ToString().Split('/').Last();
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync(FixtureRoute, LampBody(poleId))).StatusCode);

        var retire = await client.PutAsJsonAsync(
            $"{FixtureRoute}/{firstId}/removal", new { removed_date = "2026-01-15" });
        Assert.Equal(HttpStatusCode.NoContent, retire.StatusCode);

        var replacement = await client.PostAsJsonAsync(FixtureRoute, LampBody(poleId, installDate: "2026-01-16"));

        Assert.Equal(HttpStatusCode.Created, replacement.StatusCode);
        Assert.Equal(2, await ActiveLampsAsync(poleId));
        Assert.Equal(3, await AllLampsAsync(poleId));
        Assert.Equal([(short)1, (short)2], await ActiveSlotsAsync(poleId));
    }

    [Fact]
    public async Task A_historical_row_that_arrives_already_retired_may_coexist_with_two_active_lamps()
    {
        var client = await fixture.ManagerClientAsync();
        var poleId = await NewPoleAsync();

        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync(FixtureRoute, LampBody(poleId))).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync(FixtureRoute, LampBody(poleId))).StatusCode);

        var history = await client.PostAsJsonAsync(
            FixtureRoute, LampBody(poleId, installDate: "2019-03-01", removedDate: "2022-03-01"));

        Assert.Equal(HttpStatusCode.Created, history.StatusCode);
        Assert.Equal(2, await ActiveLampsAsync(poleId));
    }

    // ── read side ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task The_pole_detail_lists_both_lamps_and_the_inventory_and_map_say_how_many()
    {
        var client = await fixture.ManagerClientAsync();
        var poleId = await NewPoleAsync();
        await client.PostAsJsonAsync(FixtureRoute, LampBody(poleId, lampWatt: 80));
        await client.PostAsJsonAsync(FixtureRoute, LampBody(poleId, lampWatt: 120));

        var detail = JsonDocument.Parse(await client.GetStringAsync($"/api/v1/map/poles/{poleId}")).RootElement;
        var lamps = detail.GetProperty("fixtures");
        Assert.Equal(2, lamps.GetArrayLength());
        Assert.Equal([80, 120], lamps.EnumerateArray().Select(l => l.GetProperty("lamp_watt").GetInt32()));
        Assert.NotEqual(lamps[0].GetProperty("fixture_id").GetString(), lamps[1].GetProperty("fixture_id").GetString());
        // The singular field a one-lamp reader uses stays, and is the first lamp.
        Assert.Equal(80, detail.GetProperty("fixture").GetProperty("lamp_watt").GetInt32());

        var inventory = await InventoryRowAsync(client, poleId);
        Assert.Equal(2, inventory.GetProperty("active_fixtures").GetArrayLength());
        Assert.Equal(80, inventory.GetProperty("active_fixture").GetProperty("lamp_watt").GetInt32());

        var map = JsonDocument.Parse(await client.GetStringAsync(
            "/api/v1/map/poles?bbox=106.48,10.96,106.51,10.99")).RootElement;
        var props = map.GetProperty("features").EnumerateArray()
            .Select(f => f.GetProperty("properties"))
            .Single(p => p.GetProperty("pole_id").GetString() == poleId);
        Assert.Equal(2, props.GetProperty("fixture_count").GetInt32());
        Assert.Equal(80, props.GetProperty("lamp_watt").GetInt32());
    }

    [Fact]
    public async Task The_baseline_anchor_is_the_newest_lamp_so_replacing_either_lamp_moves_it()
    {
        var client = await fixture.ManagerClientAsync();
        var poleId = await NewPoleAsync();
        var first = await client.PostAsJsonAsync(FixtureRoute, LampBody(poleId));
        var firstId = first.Headers.Location!.ToString().Split('/').Last();
        var second = await client.PostAsJsonAsync(FixtureRoute, LampBody(poleId));
        var secondId = second.Headers.Location!.ToString().Split('/').Last();
        Assert.Equal(secondId, await AnchorAsync(poleId));

        // Replace the OLDER lamp: its replacement is newer than both, so the anchor — and with it the baseline — moves.
        await client.PutAsJsonAsync($"{FixtureRoute}/{firstId}/removal", new { removed_date = "2026-02-01" });
        var replacement = await client.PostAsJsonAsync(FixtureRoute, LampBody(poleId, installDate: "2026-02-02"));
        var replacementId = replacement.Headers.Location!.ToString().Split('/').Last();

        Assert.Equal(replacementId, await AnchorAsync(poleId));
        Assert.NotEqual(secondId, await AnchorAsync(poleId));
    }

    [Fact]
    public void First_free_slot_takes_the_lowest_and_runs_out_after_two()
    {
        Assert.Equal((short)1, FixtureSlots.FirstFree([]));
        Assert.Equal((short)2, FixtureSlots.FirstFree([1]));
        Assert.Equal((short)1, FixtureSlots.FirstFree([2]));
        Assert.Null(FixtureSlots.FirstFree([1, 2]));
    }

    // ── import ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Import_fills_both_slots_then_refuses_a_third_lamp_until_one_is_retired()
    {
        var client = await fixture.ManagerClientAsync();
        var tag = $"T{Guid.NewGuid():N}"[..9].ToUpperInvariant();
        var poleId = await NewPoleAsync(externalRef: $"{tag}-P1");

        const string header =
            "pole_external_ref,fixture_type,power_source,lamp_watt,install_date,removed_date,warranty_expiry,data_source";
        var row = $"{tag}-P1,led_road_lamp,grid,100,2026-02-01,,,public_imagery";

        // Three active rows for one pole in a single file: the first two take the slots, the third has none left.
        var filled = await AssetImportTests.ImportAsync(client, "fixtures", "fixtures.csv",
            header + $"\n{row}\n{row}\n{row}");
        Assert.Equal(2, filled.GetProperty("inserted").GetInt32());
        Assert.Equal(1, filled.GetProperty("failed").GetInt32());
        Assert.Contains("2 lamps in service", filled.GetProperty("rows")[0].GetProperty("message").GetString()!);
        Assert.Equal([(short)1, (short)2], await ActiveSlotsAsync(poleId));

        // With both slots held by lamps already in the database, one more is refused.
        var refused = await AssetImportTests.ImportAsync(client, "fixtures", "fixtures.csv", header + $"\n{row}");
        Assert.Equal(0, refused.GetProperty("inserted").GetInt32());
        Assert.Equal(1, refused.GetProperty("failed").GetInt32());

        // Retire the slot-1 lamp as the system, and the same file goes through into the freed slot.
        await fixture.QueryAsync(async db =>
        {
            using (db.EnterUnscopedSystemWriteBackdoor())
            {
                var lamp = await db.Set<Fixture>().IgnoreQueryFilters()
                    .SingleAsync(candidate => candidate.PoleId == poleId && candidate.RemovedDate == null && candidate.Slot == 1);
                lamp.RemovedDate = new DateOnly(2026, 3, 1);
                return await db.SaveChangesAsync();
            }
        });

        var replaced = await AssetImportTests.ImportAsync(client, "fixtures", "fixtures.csv", header + $"\n{row}");
        Assert.Equal(1, replaced.GetProperty("inserted").GetInt32());
        Assert.Equal([(short)1, (short)2], await ActiveSlotsAsync(poleId));
    }

    // ── database ───────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The rule holds with no application code in the way: two active rows in the SAME slot trip
    /// <c>ux_fixture_pole_slot_active</c>, a third slot is refused by <c>ck_fixture_slot</c>, and a retired row holds no slot.
    /// </summary>
    [Fact]
    public async Task The_database_allows_two_slots_and_refuses_a_repeated_or_third_slot()
    {
        var poleId = await NewPoleAsync();

        await WriteLampAsync(poleId, removedDate: null, slot: 1);
        await WriteLampAsync(poleId, removedDate: null, slot: 2);

        var repeated = await Assert.ThrowsAsync<DbUpdateException>(() => WriteLampAsync(poleId, removedDate: null, slot: 2));
        Assert.Contains("ux_fixture_pole_slot_active", repeated.InnerException?.Message);

        var third = await Assert.ThrowsAsync<DbUpdateException>(() => WriteLampAsync(poleId, removedDate: null, slot: 3));
        Assert.Contains("ck_fixture_slot", third.InnerException?.Message);

        // Sabotage the other way: a retired row is NOT a collision, even in an occupied slot.
        await WriteLampAsync(poleId, removedDate: new DateOnly(2020, 1, 1), slot: 1);
        Assert.Equal(3, await AllLampsAsync(poleId));
    }

    // ── helpers ────────────────────────────────────────────────────────────────────────────

    private static object LampBody(
        string poleId, string installDate = "2026-01-04", string? removedDate = null, int lampWatt = 100) => new
    {
        pole_id = poleId,
        fixture_type = "led_road_lamp",
        power_source = "grid",
        lamp_watt = lampWatt,
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
