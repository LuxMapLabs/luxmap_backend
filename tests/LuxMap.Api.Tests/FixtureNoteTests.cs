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
/// FIXTURE-NOTE (drift FN-1…FN-3) — a fitting carries its own free-text note for the special case its record cannot
/// say. Written with the fitting (API and import), same rules as a pole note, shown on the pole detail and the
/// inventory, and kept apart from the pole's own <c>note</c>.
/// </summary>
[Collection(nameof(AssetDatabaseCollection))]
public sealed class FixtureNoteTests(AssetImportFixture fixture)
{
    private const string FixtureRoute = "/api/v1/assets/fixtures";

    [Fact]
    public async Task The_note_is_trimmed_and_shown_on_the_fitting_not_on_the_pole()
    {
        var client = await fixture.ManagerClientAsync();
        var poleId = await NewPoleAsync();

        var created = await client.PostAsJsonAsync(FixtureRoute, LampBody(poleId, note: "  Bóng 2 là 150W, khác loại  "));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var detail = JsonDocument.Parse(await client.GetStringAsync($"/api/v1/map/poles/{poleId}")).RootElement;
        Assert.Equal("Bóng 2 là 150W, khác loại", detail.GetProperty("fixture").GetProperty("note").GetString());
        Assert.Equal("Bóng 2 là 150W, khác loại", detail.GetProperty("fixtures")[0].GetProperty("note").GetString());
        // Sabotage the other way: the pole's own note is a different thing and stays empty.
        Assert.Equal(JsonValueKind.Null, detail.GetProperty("note").ValueKind);

        var inventory = await InventoryRowAsync(client, poleId);
        Assert.Equal("Bóng 2 là 150W, khác loại", inventory.GetProperty("active_fixture").GetProperty("note").GetString());
        Assert.Equal(JsonValueKind.Null, inventory.GetProperty("note").ValueKind);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public async Task A_missing_or_blank_note_is_stored_as_none(string? note)
    {
        var client = await fixture.ManagerClientAsync();
        var poleId = await NewPoleAsync();

        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync(FixtureRoute, LampBody(poleId, note: note))).StatusCode);

        Assert.Null(await StoredNoteAsync(poleId));
    }

    [Fact]
    public async Task A_note_over_1000_characters_is_400_and_writes_nothing()
    {
        var client = await fixture.ManagerClientAsync();
        var poleId = await NewPoleAsync();

        var response = await client.PostAsJsonAsync(FixtureRoute, LampBody(poleId, note: new string('x', 1001)));
        var error = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("error");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ErrorCodes.ValidationFailed, error.GetProperty("code").GetString());
        Assert.Equal("note", error.GetProperty("details").GetProperty("field").GetString());
        Assert.Equal(0, await fixture.QueryAsync(db => db.Set<Fixture>().IgnoreQueryFilters().CountAsync(f => f.PoleId == poleId)));

        // Exactly the limit is accepted.
        var atLimit = await client.PostAsJsonAsync(FixtureRoute, LampBody(poleId, note: new string('x', 1000)));
        Assert.Equal(HttpStatusCode.Created, atLimit.StatusCode);
    }

    [Fact]
    public async Task Import_reads_an_optional_note_column_and_fails_only_the_row_that_is_too_long()
    {
        var client = await fixture.ManagerClientAsync();
        var tag = $"N{Guid.NewGuid():N}"[..9].ToUpperInvariant();
        var withNote = await NewPoleAsync(externalRef: $"{tag}-P1");
        var tooLong = await NewPoleAsync(externalRef: $"{tag}-P2");

        const string header =
            "pole_external_ref,fixture_type,power_source,lamp_watt,install_date,removed_date,warranty_expiry,data_source,note";
        var result = await AssetImportTests.ImportAsync(client, "fixtures", "fixtures.csv",
            header
            + $"\n{tag}-P1,led_road_lamp,grid,100,2026-02-01,,,public_imagery,Chao đôi lệch hướng"
            + $"\n{tag}-P2,led_road_lamp,grid,100,2026-02-01,,,public_imagery,{new string('y', 1001)}");

        Assert.Equal(1, result.GetProperty("inserted").GetInt32());
        Assert.Equal(1, result.GetProperty("failed").GetInt32());
        Assert.Equal("note", result.GetProperty("rows")[0].GetProperty("column").GetString());
        Assert.Equal("Chao đôi lệch hướng", await StoredNoteAsync(withNote));
        Assert.Equal(0, await fixture.QueryAsync(db => db.Set<Fixture>().IgnoreQueryFilters().CountAsync(f => f.PoleId == tooLong)));
    }

    [Fact]
    public async Task The_database_refuses_a_note_over_1000_characters_from_any_writer()
    {
        var poleId = await NewPoleAsync();

        var failure = await Assert.ThrowsAsync<DbUpdateException>(() => fixture.QueryAsync(async db =>
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
                    InstallDate = new DateOnly(2026, 1, 1),
                    DataSource = DataSource.PublicImagery,
                    Note = new string('z', 1001),
                });
                return await db.SaveChangesAsync();
            }
        }));

        Assert.Contains("ck_fixture_note_length", failure.InnerException?.Message);
    }

    // ── helpers ────────────────────────────────────────────────────────────────────────────

    private static object LampBody(string poleId, string? note) => new
    {
        pole_id = poleId,
        fixture_type = "led_road_lamp",
        power_source = "grid",
        lamp_watt = 100,
        install_date = "2026-01-04",
        data_source = "public_imagery",
        note,
    };

    private Task<string?> StoredNoteAsync(string poleId)
        => fixture.QueryAsync(db => db.Set<Fixture>().IgnoreQueryFilters()
            .Where(f => f.PoleId == poleId && f.RemovedDate == null)
            .Select(f => f.Note).SingleAsync());

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

    /// <summary>A segment and a pole in the fixture's commune, built as the system.</summary>
    private Task<string> NewPoleAsync(string? externalRef = null)
        => fixture.QueryAsync(async db =>
        {
            using (db.EnterUnscopedSystemWriteBackdoor())
            {
                var segment = new RoadSegment
                {
                    SegmentName = "fixture note probe",
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
