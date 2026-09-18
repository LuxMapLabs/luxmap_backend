using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using LuxMap.Modules.Assets.Entities;
using LuxMap.Modules.Faults.Entities;
using LuxMap.Modules.Survey.Entities;
using LuxMap.Persistence;
using LuxMap.Shared.Contracts.Enums;
using LuxMap.Shared.Contracts.Errors;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NetTopologySuite.Geometries;

namespace LuxMap.Api.Tests;

/// <summary>
/// BE-12 — the two write endpoints a pole has beyond creation: <c>DELETE /assets/poles/{id}</c> and
/// <c>PUT /assets/poles/{id}/feeder</c>.
/// </summary>
/// <remarks>
/// The delete tests are really tests of the FOREIGN KEYS, not of the C#. Nothing in the service asks
/// whether a pole may go; <c>fault</c> and <c>lux_reading</c> hold it with RESTRICT and the database
/// answers. So each case builds a real referencing row and then checks that the refusal arrives as a
/// 409 rather than as a 500, and that nothing was half-deleted.
/// <para>
/// ⚠️ This class cleans up its own <c>fault</c> and <c>lux_reading</c> rows.
/// <see cref="AssetImportFixture"/> deletes poles and fixtures but not those two tables, so a row left
/// behind here would make the FIXTURE's teardown fail on the very RESTRICT these tests exercise.
/// </para>
/// </remarks>
[Collection(nameof(AssetImportCollection))]
public sealed class PoleWriteTests(AssetImportFixture fixture) : IAsyncLifetime
{
    private const string PoleRoute = "/api/v1/assets/poles";

    /// <summary>Ids this test created, so teardown removes those rows and only those.</summary>
    private readonly List<string> faultIds = [];

    private readonly List<string> luxIds = [];

    public Task InitializeAsync() => Task.CompletedTask;

    /// <summary>
    /// Removes the <c>fault</c> and <c>lux_reading</c> rows this test built.
    /// </summary>
    /// <remarks>
    /// <see cref="AssetImportFixture"/> deletes poles and fixtures but not these two tables, so a row
    /// left here would make the FIXTURE's own teardown fail on exactly the RESTRICT these tests are
    /// about.
    /// <para>
    /// ⚠️ Deleting by RECORDED ID, never "everything in the table". A whole-table delete would take
    /// rows belonging to another test class running beside this one — which is how this file first
    /// broke <c>LuxReadingTests</c>.
    /// </para>
    /// </remarks>
    public async Task DisposeAsync()
    {
        if (faultIds.Count == 0 && luxIds.Count == 0)
        {
            return;
        }

        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LuxMapDbContext>();

        var faults = faultIds.ToArray();
        var readings = luxIds.ToArray();

        #pragma warning disable RS0030 // Test TEARDOWN: bulk delete is the only way to clean up under an empty scope. BE-36 removes the need entirely — a fresh database per run.
        await db.Set<Fault>().IgnoreQueryFilters()
            .Where(fault => faults.Contains(fault.FaultId)).ExecuteDeleteAsync();
        await db.Set<LuxReading>().IgnoreQueryFilters()
            .Where(reading => readings.Contains(reading.LuxId)).ExecuteDeleteAsync();
        #pragma warning restore RS0030
    }

    // ── DELETE ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Deleting_a_pole_nothing_references_succeeds_and_takes_its_fixture_with_it()
    {
        var client = await fixture.AdminClientAsync();
        var (poleId, fixtureId) = await NewPoleWithFixtureAsync();

        var response = await client.DeleteAsync($"{PoleRoute}/{poleId}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(0, await CountPolesAsync(poleId));

        // pole -> fixture is CASCADE, so the lamp goes with it rather than being orphaned.
        var lamps = await fixture.QueryAsync(db => db.Set<Fixture>().IgnoreQueryFilters()
            .CountAsync(lamp => lamp.FixtureId == fixtureId));
        Assert.Equal(0, lamps);
    }

    [Fact]
    public async Task A_pole_with_a_lux_reading_cannot_be_deleted_because_that_is_research_data()
    {
        var client = await fixture.AdminClientAsync();
        var (poleId, _) = await NewPoleWithFixtureAsync();
        await AddLuxReadingAsync(poleId);

        var response = await client.DeleteAsync($"{PoleRoute}/{poleId}");
        var body = await ReadErrorAsync(response);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(ErrorCodes.AssetInUse, body.GetProperty("code").GetString());
        Assert.Equal(1, await CountPolesAsync(poleId));
    }

    [Fact]
    public async Task A_pole_with_a_fault_against_it_cannot_be_deleted()
    {
        var client = await fixture.AdminClientAsync();
        var (poleId, _) = await NewPoleWithFixtureAsync();
        await AddFaultAsync(poleId: poleId, fixtureId: null);

        var response = await client.DeleteAsync($"{PoleRoute}/{poleId}");
        var body = await ReadErrorAsync(response);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(ErrorCodes.AssetInUse, body.GetProperty("code").GetString());
        Assert.Equal(1, await CountPolesAsync(poleId));
    }

    /// <summary>
    /// The case nothing about the pole reveals: the fault hangs off its LAMP.
    /// </summary>
    /// <remarks>
    /// <c>fixture</c> CASCADES from <c>pole</c>, so the delete reaches the lamp, and the lamp is held
    /// by <c>fk_fault_fixture_fixture_id</c>. PostgreSQL aborts the whole statement — the constraint
    /// reported is on <c>fixture</c>, not on <c>pole</c>, which is the only hint the caller gets.
    /// </remarks>
    [Fact]
    public async Task A_pole_whose_fixture_has_a_fault_cannot_be_deleted_and_nothing_is_half_removed()
    {
        var client = await fixture.AdminClientAsync();
        var (poleId, fixtureId) = await NewPoleWithFixtureAsync();
        await AddFaultAsync(poleId: null, fixtureId: fixtureId);

        var response = await client.DeleteAsync($"{PoleRoute}/{poleId}");
        var body = await ReadErrorAsync(response);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(ErrorCodes.AssetInUse, body.GetProperty("code").GetString());

        // The constraint names the table the cascade tripped over, which is NOT the one asked about.
        Assert.Equal(
            "fk_fault_fixture_fixture_id",
            body.GetProperty("details").GetProperty("constraint").GetString());

        Assert.Equal(1, await CountPolesAsync(poleId));

        var lamps = await fixture.QueryAsync(db => db.Set<Fixture>().IgnoreQueryFilters()
            .CountAsync(lamp => lamp.FixtureId == fixtureId));
        Assert.Equal(1, lamps);
    }

    [Fact]
    public async Task Deleting_a_pole_that_does_not_exist_is_404()
    {
        var client = await fixture.AdminClientAsync();

        var response = await client.DeleteAsync($"{PoleRoute}/POLE-000000");
        var body = await ReadErrorAsync(response);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(ErrorCodes.AssetNotFound, body.GetProperty("code").GetString());
    }

    /// <summary>
    /// A pole in another commune answers <b>404</b>, not 403.
    /// </summary>
    /// <remarks>
    /// Contract section 7, and the same reasoning <c>ASSET_NOT_FOUND</c> is documented with: a 403
    /// would confirm the id exists, which is exactly what the caller is not allowed to learn. The
    /// query filter removes the row, so the service never sees it.
    /// </remarks>
    [Fact]
    public async Task Deleting_a_pole_in_another_commune_is_404_not_403_so_the_id_is_not_confirmed()
    {
        var foreignPoleId = await NewForeignPoleAsync();
        var client = await fixture.AdminClientAsync();

        var response = await client.DeleteAsync($"{PoleRoute}/{foreignPoleId}");
        var body = await ReadErrorAsync(response);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(ErrorCodes.AssetNotFound, body.GetProperty("code").GetString());
        Assert.Equal(1, await CountPolesAsync(foreignPoleId));
    }

    [Fact]
    public async Task A_maintenance_engineer_may_not_delete_a_pole()
    {
        var client = await fixture.SeededClientAsync("engineer", "SEED_ENGINEER_PASSWORD");
        var (poleId, _) = await NewPoleWithFixtureAsync();

        var response = await client.DeleteAsync($"{PoleRoute}/{poleId}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(1, await CountPolesAsync(poleId));
    }

    // ── PUT feeder ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Assigning_a_feeder_in_the_same_commune_succeeds_and_the_row_changes()
    {
        var client = await fixture.AdminClientAsync();
        var (poleId, _) = await NewPoleWithFixtureAsync();
        var feederId = await NewFeederAsync(fixture.CommuneId);

        var response = await PutFeederAsync(client, poleId, $"\"{feederId}\"");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(feederId, await FeederOfAsync(poleId));
    }

    [Fact]
    public async Task Assigning_null_clears_the_feeder_because_a_solar_pole_is_on_no_circuit()
    {
        var client = await fixture.AdminClientAsync();
        var (poleId, _) = await NewPoleWithFixtureAsync();
        var feederId = await NewFeederAsync(fixture.CommuneId);

        await PutFeederAsync(client, poleId, $"\"{feederId}\"");
        var response = await PutFeederAsync(client, poleId, "null");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Null(await FeederOfAsync(poleId));
    }

    /// <summary>
    /// An ABSENT key is a 400, and that is the whole reason the body is a <c>JsonElement</c>.
    /// </summary>
    /// <remarks>
    /// Read as <c>null</c> instead, an empty or malformed body would silently CLEAR a pole's circuit.
    /// </remarks>
    [Fact]
    public async Task An_empty_body_is_rejected_rather_than_read_as_a_request_to_clear_the_feeder()
    {
        var client = await fixture.AdminClientAsync();
        var (poleId, _) = await NewPoleWithFixtureAsync();
        var feederId = await NewFeederAsync(fixture.CommuneId);
        await PutFeederAsync(client, poleId, $"\"{feederId}\"");

        using var content = new StringContent("{}", Encoding.UTF8, "application/json");
        var response = await client.PutAsync($"{PoleRoute}/{poleId}/feeder", content);
        var body = await ReadErrorAsync(response);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ErrorCodes.ValidationFailed, body.GetProperty("code").GetString());

        // The assignment it already had is untouched.
        Assert.Equal(feederId, await FeederOfAsync(poleId));
    }

    [Fact]
    public async Task Assigning_a_feeder_that_does_not_exist_is_404()
    {
        var client = await fixture.AdminClientAsync();
        var (poleId, _) = await NewPoleWithFixtureAsync();

        var response = await PutFeederAsync(client, poleId, "\"FDR-000\"");
        var body = await ReadErrorAsync(response);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(ErrorCodes.AssetNotFound, body.GetProperty("code").GetString());
    }

    /// <summary>
    /// A feeder the caller CAN see, in a commune the pole is not in, is refused.
    /// </summary>
    /// <remarks>
    /// Uses the two-commune administrator on purpose: with a single-commune account the query filter
    /// hides the foreign feeder and the answer would be 404, which proves the filter rather than this
    /// check. <c>CommuneWriteGuard</c> cannot catch this at all — it reads the commune of the row being
    /// written, and the pole's own commune is in scope.
    /// </remarks>
    [Fact]
    public async Task A_feeder_from_another_commune_is_refused_even_when_the_caller_can_see_both()
    {
        var client = await fixture.BothCommunesClientAsync();
        var (poleId, _) = await NewPoleWithFixtureAsync();
        var foreignFeederId = await NewFeederAsync(fixture.ForeignCommuneId);

        var response = await PutFeederAsync(client, poleId, $"\"{foreignFeederId}\"");
        var body = await ReadErrorAsync(response);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(ErrorCodes.CommuneForbidden, body.GetProperty("code").GetString());
        Assert.Null(await FeederOfAsync(poleId));
    }

    [Fact]
    public async Task Setting_the_feeder_of_a_pole_in_another_commune_is_404()
    {
        var foreignPoleId = await NewForeignPoleAsync();
        var client = await fixture.AdminClientAsync();
        var feederId = await NewFeederAsync(fixture.CommuneId);

        var response = await PutFeederAsync(client, foreignPoleId, $"\"{feederId}\"");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task A_field_crew_member_may_not_set_a_feeder()
    {
        var client = await fixture.SeededClientAsync("crew", "SEED_CREW_PASSWORD");
        var (poleId, _) = await NewPoleWithFixtureAsync();

        var response = await PutFeederAsync(client, poleId, "null");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ── helpers ───────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// <c>async</c>, and the <c>await</c> is INSIDE the <c>using</c> — load-bearing.
    /// </summary>
    /// <remarks>
    /// Returning the Task from a non-async method disposes the <see cref="StringContent"/> while the
    /// request is still being written, and every call fails with "Cannot access a closed Stream".
    /// </remarks>
    private static async Task<HttpResponseMessage> PutFeederAsync(
        HttpClient client, string poleId, string feederJson)
    {
        using var content = new StringContent(
            $$"""{"feeder_id": {{feederJson}}}""", Encoding.UTF8, "application/json");

        return await client.PutAsync($"/api/v1/assets/poles/{poleId}/feeder", content);
    }

    private static async Task<JsonElement> ReadErrorAsync(HttpResponseMessage response)
        => JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("error");

    private Task<int> CountPolesAsync(string poleId)
        => fixture.QueryAsync(db => db.Set<Pole>().IgnoreQueryFilters().CountAsync(p => p.PoleId == poleId));

    private Task<string?> FeederOfAsync(string poleId)
        => fixture.QueryAsync(db => db.Set<Pole>().IgnoreQueryFilters().AsNoTracking()
            .Where(p => p.PoleId == poleId).Select(p => p.FeederId).SingleAsync());

    private Task<(string PoleId, string FixtureId)> NewPoleWithFixtureAsync()
        => NewPoleWithFixtureAsync(fixture.CommuneId);

    private async Task<string> NewForeignPoleAsync()
        => (await NewPoleWithFixtureAsync(fixture.ForeignCommuneId)).PoleId;

    /// <summary>
    /// Builds a pole and its lamp straight through the DbContext, as the system.
    /// </summary>
    /// <remarks>
    /// Not over HTTP: several of these poles live in a commune the test's own account cannot write to,
    /// which is the point of those cases. The backdoor is spelled the way it is so it shows in a diff.
    /// </remarks>
    private Task<(string PoleId, string FixtureId)> NewPoleWithFixtureAsync(string communeId)
        => fixture.QueryAsync(async db =>
        {
            using (db.EnterUnscopedSystemWriteBackdoor())
            {
                var segment = new RoadSegment
                {
                    SegmentName = "pole write probe",
                    RoadClass = RoadClass.InterVillage,
                    LengthM = 100,
                    Geom = new LineString([new Coordinate(106.49, 10.97), new Coordinate(106.50, 10.98)]) { SRID = 4326 },
                    CommuneId = communeId,
                    DataSource = DataSource.PublicImagery,
                };

                db.Set<RoadSegment>().Add(segment);
                await db.SaveChangesAsync();

                var pole = new Pole
                {
                    SegmentId = segment.SegmentId,
                    CommuneId = communeId,
                    Geom = new Point(106.49, 10.97) { SRID = 4326 },
                    DataSource = DataSource.PublicImagery,
                };

                db.Set<Pole>().Add(pole);
                await db.SaveChangesAsync();

                var lamp = new Fixture
                {
                    PoleId = pole.PoleId,
                    CommuneId = communeId,
                    FixtureType = FixtureType.LedRoadLamp,
                    PowerSource = PowerSource.Grid,
                    LampWatt = 100,
                    InstallDate = new DateOnly(2022, 3, 24),
                    DataSource = DataSource.PublicImagery,
                };

                db.Set<Fixture>().Add(lamp);
                await db.SaveChangesAsync();

                return (pole.PoleId, lamp.FixtureId);
            }
        });

    private Task<string> NewFeederAsync(string communeId)
        => fixture.QueryAsync(async db =>
        {
            using (db.EnterUnscopedSystemWriteBackdoor())
            {
                var feeder = new Feeder { FeederName = "pole write probe", CommuneId = communeId };
                db.Set<Feeder>().Add(feeder);
                await db.SaveChangesAsync();
                return feeder.FeederId;
            }
        });

    private Task AddLuxReadingAsync(string poleId)
        => fixture.QueryAsync(async db =>
        {
            using (db.EnterUnscopedSystemWriteBackdoor())
            {
                var measuredBy = await db.Set<Modules.Identity.Entities.AppUser>()
                    .Where(user => user.Username == "admin").Select(user => user.UserId).SingleAsync();

                var reading = new LuxReading
                {
                    ClientOpId = Guid.NewGuid().ToString(),
                    PoleId = poleId,
                    CommuneId = fixture.CommuneId,
                    MeasuredAt = DateTime.UtcNow,
                    MeasuredBy = measuredBy,
                    LuxValue = 12.4,
                    DataSource = DataSource.CalibrationRig,
                };

                db.Set<LuxReading>().Add(reading);

                await db.SaveChangesAsync();
                luxIds.Add(reading.LuxId);
                return 0;
            }
        });

    private Task AddFaultAsync(string? poleId, string? fixtureId)
        => fixture.QueryAsync(async db =>
        {
            using (db.EnterUnscopedSystemWriteBackdoor())
            {
                var fault = new Fault
                {
                    PoleId = poleId,
                    FixtureId = fixtureId,

                    // ck_fault_pole_or_location: without a pole the row must carry coordinates.
                    Lat = poleId is null ? 10.97 : null,
                    Lng = poleId is null ? 106.49 : null,
                    CommuneId = fixture.CommuneId,
                    FaultType = FaultType.LampOut,
                    FaultStatus = FaultStatus.Detected,
                    Severity = Severity.High,
                    SourceChannel = SourceChannel.Cv,
                    DataSource = DataSource.PublicImagery,
                    DetectedAt = DateTime.UtcNow,
                };

                db.Set<Fault>().Add(fault);
                await db.SaveChangesAsync();
                faultIds.Add(fault.FaultId);
                return 0;
            }
        });
}
