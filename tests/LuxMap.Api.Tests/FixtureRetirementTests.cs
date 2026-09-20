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
/// BE-REVIEW-02, Q-4 — a lamp is retired once, and never before it was installed. The API answers
/// 400 naming the field; <c>ck_fixture_removed_after_install</c> answers for every other writer.
/// </summary>
[Collection(nameof(AssetDatabaseCollection))]
public sealed class FixtureRetirementTests(AssetImportFixture fixture)
{
    private const string FixtureRoute = "/api/v1/assets/fixtures";

    [Fact]
    public async Task Retiring_a_lamp_before_its_install_date_is_400_and_the_row_is_untouched()
    {
        var client = await fixture.AdminClientAsync();
        var lampId = await NewLampAsync(installDate: new DateOnly(2024, 6, 1));

        var response = await client.PutAsJsonAsync($"{FixtureRoute}/{lampId}/removal", new { removed_date = "2024-05-31" });
        var body = await ReadErrorAsync(response);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ErrorCodes.ValidationFailed, body.GetProperty("code").GetString());
        Assert.Equal("2024-06-01", body.GetProperty("details").GetProperty("install_date").GetString());
        Assert.Null(await RemovedDateOfAsync(lampId));
    }

    [Fact]
    public async Task Retiring_a_lamp_on_its_install_date_or_later_succeeds_and_a_second_retirement_is_400()
    {
        var client = await fixture.AdminClientAsync();
        var lampId = await NewLampAsync(installDate: new DateOnly(2024, 6, 1));

        // Same day is allowed: a lamp can be installed and found dead within the shift.
        var first = await client.PutAsJsonAsync($"{FixtureRoute}/{lampId}/removal", new { removed_date = "2024-06-01" });
        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal(new DateOnly(2024, 6, 1), await RemovedDateOfAsync(lampId));

        var second = await client.PutAsJsonAsync($"{FixtureRoute}/{lampId}/removal", new { removed_date = "2025-01-01" });
        var body = await ReadErrorAsync(second);

        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
        Assert.Equal(ErrorCodes.ValidationFailed, body.GetProperty("code").GetString());

        // The history is not rewritten.
        Assert.Equal(new DateOnly(2024, 6, 1), await RemovedDateOfAsync(lampId));
    }

    [Fact]
    public async Task Creating_a_historical_lamp_with_removed_date_before_install_date_is_400()
    {
        var client = await fixture.AdminClientAsync();
        var poleId = await NewPoleAsync();

        var response = await client.PostAsJsonAsync(FixtureRoute, new
        {
            pole_id = poleId,
            fixture_type = "led_road_lamp",
            power_source = "grid",
            lamp_watt = 100,
            install_date = "2022-03-24",
            removed_date = "2021-01-01",
            data_source = "public_imagery",
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ErrorCodes.ValidationFailed, (await ReadErrorAsync(response)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Import_reports_a_removed_date_before_install_date_as_a_row_error()
    {
        var client = await fixture.AdminClientAsync();
        var tag = $"T{Guid.NewGuid():N}"[..9].ToUpperInvariant();
        await NewPoleAsync(externalRef: $"{tag}-P1");

        var result = await AssetImportTests.ImportAsync(client, "fixtures", "fixtures.csv",
            "pole_external_ref,fixture_type,power_source,lamp_watt,install_date,removed_date,warranty_expiry,data_source"
            + $"\n{tag}-P1,led_road_lamp,grid,100,2022-03-24,2021-01-01,,public_imagery");

        Assert.Equal(0, result.GetProperty("inserted").GetInt32());
        Assert.Equal(1, result.GetProperty("failed").GetInt32());
        Assert.Equal("removed_date", result.GetProperty("rows")[0].GetProperty("column").GetString());
    }

    /// <summary>
    /// The database refuses the ordering violation with no application code in the way — and accepts
    /// the boundary case, so the constraint is not merely refusing everything.
    /// </summary>
    [Fact]
    public async Task The_check_constraint_refuses_removed_before_install_even_when_written_as_the_system()
    {
        var poleId = await NewPoleAsync();

        var error = await Assert.ThrowsAsync<DbUpdateException>(() => WriteLampAsync(
            poleId, installDate: new DateOnly(2024, 6, 1), removedDate: new DateOnly(2024, 5, 31)));
        Assert.Contains("ck_fixture_removed_after_install", error.InnerException?.Message);

        // Sabotage the other way: removed ON the install date is legal.
        await WriteLampAsync(poleId, installDate: new DateOnly(2024, 6, 1), removedDate: new DateOnly(2024, 6, 1));
    }

    // ── helpers ────────────────────────────────────────────────────────────────────────────

    private static async Task<JsonElement> ReadErrorAsync(HttpResponseMessage response)
        => JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("error");

    private Task<DateOnly?> RemovedDateOfAsync(string lampId)
        => fixture.QueryAsync(db => db.Set<Fixture>().IgnoreQueryFilters().AsNoTracking()
            .Where(lamp => lamp.FixtureId == lampId).Select(lamp => lamp.RemovedDate).SingleAsync());

    private async Task<string> NewLampAsync(DateOnly installDate)
    {
        var poleId = await NewPoleAsync();
        return await WriteLampAsync(poleId, installDate, removedDate: null);
    }

    private Task<string> WriteLampAsync(string poleId, DateOnly installDate, DateOnly? removedDate)
        => fixture.QueryAsync(async db =>
        {
            using (db.EnterUnscopedSystemWriteBackdoor())
            {
                var lamp = new Fixture
                {
                    PoleId = poleId,
                    CommuneId = fixture.CommuneId,
                    FixtureType = FixtureType.LedRoadLamp,
                    PowerSource = PowerSource.Grid,
                    LampWatt = 100,
                    InstallDate = installDate,
                    RemovedDate = removedDate,
                    DataSource = DataSource.PublicImagery,
                };
                db.Set<Fixture>().Add(lamp);
                await db.SaveChangesAsync();
                return lamp.FixtureId;
            }
        });

    private Task<string> NewPoleAsync(string? externalRef = null)
        => fixture.QueryAsync(async db =>
        {
            using (db.EnterUnscopedSystemWriteBackdoor())
            {
                var segment = new RoadSegment
                {
                    SegmentName = "fixture retirement probe",
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
