using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using LuxMap.Modules.Assets.Entities;
using LuxMap.Modules.Identity.Auth;
using LuxMap.Modules.Survey.Entities;
using LuxMap.Persistence;
using LuxMap.Persistence.Conventions;
using LuxMap.Shared.Contracts.Enums;
using LuxMap.Shared.Contracts.Errors;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NetTopologySuite.Geometries;

namespace LuxMap.Api.Tests;

/// <summary>
/// BE-42 — Contract section 2.9, lux readings.
/// </summary>
/// <remarks>
/// Driven through the real service with a real <see cref="ClaimsPrincipal"/> on the ambient
/// <see cref="IHttpContextAccessor"/>, which is what <c>CommuneScopeAccessor</c> reads — the same
/// approach as <c>CommuneWriteScopeTests</c>. Writing with an empty scope is a REFUSAL since the
/// BE-08 hotfix, so anything that builds fixture data says so through the explicit backdoor.
/// </remarks>
[Collection(nameof(AssetSchemaCollection))]
public class LuxReadingTests(AssetSchemaFixture fixture) : IAsyncLifetime
{
    private const int Srid = 4326;

    /// <summary>The seeded administrator. Its claim is <c>["*"]</c>, so it can write any commune.</summary>
    private const string AdminUser = "admin";

    private const string AdminUserId = "USR-001";

    /// <summary>
    /// The seeded engineer, scoped to <c>COM-001</c> only.
    /// </summary>
    /// <remarks>
    /// Used for the out-of-scope test: the fixture creates a FRESH commune per run, so every fixture
    /// pole is outside this account's claim without any extra setup.
    /// </remarks>
    private const string EngineerUser = "engineer";

    private readonly List<string> createdLuxIds = [];
    private readonly List<string> createdPoleIds = [];

    public Task InitializeAsync() => Task.CompletedTask;

    /// <summary>Removes every row this class created — the fixture only cleans its own commune.</summary>
    public async Task DisposeAsync()
        => await fixture.WriteAsSystemAsync(async db =>
        {
            #pragma warning disable RS0030 // Test TEARDOWN: bulk delete is the only way to clean up under an empty scope. BE-36 removes the need entirely — a fresh database per run.
            await db.Set<LuxReading>().IgnoreQueryFilters()
                .Where(reading => createdLuxIds.Contains(reading.LuxId)).ExecuteDeleteAsync();

            return await db.Set<Pole>().IgnoreQueryFilters()
                .Where(pole => createdPoleIds.Contains(pole.PoleId)).ExecuteDeleteAsync();
            #pragma warning restore RS0030
        });

    /// <summary>
    /// A client carrying a REAL access token.
    /// </summary>
    /// <remarks>
    /// The scope has to come from a signed JWT: an HTTP request runs its own authentication, so
    /// setting an ambient ClaimsPrincipal in the test process would leave the request unauthenticated.
    /// </remarks>
    private async Task<HttpClient> ClientAsync(string username)
    {
        var client = fixture.CreateClient();
        // LoginAsync takes the ENVIRONMENT VARIABLE NAME, not the password — seed passwords are read
        // from .env and never appear in test source.
        var tokens = await client.LoginAsync(
            username,
            username == AdminUser ? "SEED_ADMIN_PASSWORD" : "SEED_ENGINEER_PASSWORD");

        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", tokens.AccessToken);

        return client;
    }

    /// <summary>A pole in the fixture's commune, created as the system.</summary>
    private async Task<string> NewPoleAsync()
    {
        var poleId = await fixture.WriteAsSystemAsync(async db =>
        {
            var pole = new Pole
            {
                SegmentId = fixture.SegmentId,
                CommuneId = fixture.CommuneId,
                Geom = new Point(106.49, 10.97) { SRID = Srid },
                DataSource = DataSource.CalibrationRig,
            };
            db.Set<Pole>().Add(pole);
            await db.SaveChangesAsync();
            return pole.PoleId;
        });

        createdPoleIds.Add(poleId);
        return poleId;
    }

    private object Body(
        string poleId,
        double luxValue = 12.4,
        string? clientOpId = null,
        string dataSource = "calibration_rig",
        string? luxId = null,
        string? communeId = null)
    {
        var body = new Dictionary<string, object?>
        {
            ["client_op_id"] = clientOpId ?? Guid.NewGuid().ToString(),
            ["pole_id"] = poleId,
            ["measured_at"] = "2026-10-02T19:42:00Z",
            ["lux_value"] = luxValue,
            ["meter_model"] = "UNI-T UT383",
            ["data_source"] = dataSource,
            ["note"] = "Mức suy giảm 60%",
        };

        if (luxId is not null) { body["lux_id"] = luxId; }
        if (communeId is not null) { body["commune_id"] = communeId; }

        return body;
    }

    private static async Task<(HttpStatusCode Status, JsonElement Body)> PostAsync(
        HttpClient client, object body)
    {
        var response = await client.PostAsJsonAsync("/api/v1/lux-readings", body);
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return (response.StatusCode, json.RootElement.Clone());
    }

    // ── (i) create ───────────────────────────────────────────────────────────

    [Fact]
    public async Task A_reading_is_created_and_the_database_assigns_the_lux_id()
    {
        using var client = await ClientAsync(AdminUser);
        var poleId = await NewPoleAsync();

        var (status, body) = await PostAsync(client, Body(poleId));

        Assert.Equal(HttpStatusCode.Created, status);

        var luxId = body.GetProperty("lux_id").GetString()!;
        createdLuxIds.Add(luxId);

        Assert.Matches(@"^LUX-\d{4,}$", luxId);
        Assert.Equal(poleId, body.GetProperty("pole_id").GetString());
        Assert.Equal("calibration_rig", body.GetProperty("data_source").GetString());
    }

    // ── (ii) and (iii) server-owned fields ───────────────────────────────────

    [Fact]
    public async Task Sending_lux_id_is_rejected_rather_than_silently_ignored()
    {
        using var client = await ClientAsync(AdminUser);
        var poleId = await NewPoleAsync();

        var (status, body) = await PostAsync(client, Body(poleId, luxId: "LUX-9999"));

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal(ErrorCodes.ServerOwnedField, body.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Sending_commune_id_is_rejected_rather_than_silently_ignored()
    {
        // Silence would be worse than a refusal here: the caller would believe it chose the commune
        // that owns the record, and it did not.
        using var client = await ClientAsync(AdminUser);
        var poleId = await NewPoleAsync();

        var (status, body) = await PostAsync(client, Body(poleId, communeId: fixture.CommuneId));

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal(ErrorCodes.ServerOwnedField, body.GetProperty("error").GetProperty("code").GetString());
    }

    // ── (iv) and (v) pole resolution ─────────────────────────────────────────

    [Fact]
    public async Task An_unknown_pole_is_404()
    {
        using var client = await ClientAsync(AdminUser);

        var (status, body) = await PostAsync(client, Body("POLE-999999"));

        Assert.Equal(HttpStatusCode.NotFound, status);
        Assert.Equal(ErrorCodes.PoleNotFound, body.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task A_pole_outside_the_scope_is_404_not_403_so_its_existence_stays_hidden()
    {
        // Contract section 7: direct access to an out-of-scope resource answers 404, because 403
        // would confirm the resource exists. The query filter delivers that for free — the pole is
        // simply not found.
        //
        // The seeded engineer is scoped to COM-001 while the fixture creates a fresh commune per
        // run, so any fixture pole is out of scope for them without further setup. The pole is REAL:
        // an invented id would be stopped by the foreign key before the scope check ran, and the
        // test would pass while proving the wrong thing.
        var poleId = await NewPoleAsync();
        using var client = await ClientAsync(EngineerUser);

        var (status, body) = await PostAsync(client, Body(poleId));

        Assert.Equal(HttpStatusCode.NotFound, status);
        Assert.Equal(ErrorCodes.PoleNotFound, body.GetProperty("error").GetProperty("code").GetString());
    }

    // ── (vi) commune comes from the pole ─────────────────────────────────────

    [Fact]
    public async Task The_stored_commune_is_the_poles_commune()
    {
        using var client = await ClientAsync(AdminUser);
        var poleId = await NewPoleAsync();

        var (_, body) = await PostAsync(client, Body(poleId));
        var luxId = body.GetProperty("lux_id").GetString()!;
        createdLuxIds.Add(luxId);

        var stored = await fixture.QueryAsync(db => db.Set<LuxReading>()
            .IgnoreQueryFilters()
            .SingleAsync(reading => reading.LuxId == luxId));

        Assert.Equal(fixture.CommuneId, stored.CommuneId);
    }

    // ── (vii) client_op_id de-duplication ────────────────────────────────────

    [Fact]
    public async Task A_repeated_client_op_id_returns_200_with_the_first_record_and_creates_nothing()
    {
        using var client = await ClientAsync(AdminUser);
        var poleId = await NewPoleAsync();
        var opId = Guid.NewGuid().ToString();

        var (firstStatus, first) = await PostAsync(client, Body(poleId, clientOpId: opId));
        var luxId = first.GetProperty("lux_id").GetString()!;
        createdLuxIds.Add(luxId);

        var (secondStatus, second) = await PostAsync(client, Body(poleId, luxValue: 99.9, clientOpId: opId));

        Assert.Equal(HttpStatusCode.Created, firstStatus);

        // 200, not 409: retrying on a weak connection is normal field behaviour (Contract 5.8).
        Assert.Equal(HttpStatusCode.OK, secondStatus);
        Assert.Equal(luxId, second.GetProperty("lux_id").GetString());

        // The second body's different lux_value must NOT have overwritten anything.
        Assert.Equal(12.4, second.GetProperty("lux_value").GetDouble(), precision: 3);

        var count = await fixture.QueryAsync(db => db.Set<LuxReading>()
            .IgnoreQueryFilters()
            .CountAsync(reading => reading.ClientOpId == opId));

        Assert.Equal(1, count);
    }

    // ── (viii) lux_value bounds ──────────────────────────────────────────────

    [Theory]
    [InlineData(-0.1, HttpStatusCode.BadRequest)]
    [InlineData(0, HttpStatusCode.Created)]
    [InlineData(99999, HttpStatusCode.Created)]
    public async Task Lux_value_is_refused_only_when_negative(double luxValue, HttpStatusCode expected)
    {
        // No upper bound, deliberately. This is ground truth for RQ1: FO-14 measures once in the
        // field, so refusing a real reading loses it for good, while an implausible one stays
        // visible and can be excluded during analysis. 99999 is logged as a warning and stored.
        using var client = await ClientAsync(AdminUser);
        var poleId = await NewPoleAsync();

        var (status, body) = await PostAsync(client, Body(poleId, luxValue: luxValue));

        Assert.Equal(expected, status);

        if (status == HttpStatusCode.Created)
        {
            createdLuxIds.Add(body.GetProperty("lux_id").GetString()!);
        }
    }

    // ── (ix) measured_by ─────────────────────────────────────────────────────

    [Fact]
    public async Task Measured_by_comes_from_the_token_is_stored_and_is_never_echoed_back()
    {
        using var client = await ClientAsync(AdminUser);
        var poleId = await NewPoleAsync();

        var (_, body) = await PostAsync(client, Body(poleId));
        var luxId = body.GetProperty("lux_id").GetString()!;
        createdLuxIds.Add(luxId);

        // Same shape as reported_by in section 2.8: the server sets it, the client cannot, and it is
        // not part of the published response.
        Assert.False(body.TryGetProperty("measured_by", out _));
        Assert.False(body.TryGetProperty("commune_id", out _));

        var stored = await fixture.QueryAsync(db => db.Set<LuxReading>()
            .IgnoreQueryFilters()
            .SingleAsync(reading => reading.LuxId == luxId));

        Assert.Equal(AdminUserId, stored.MeasuredBy);
    }

    // ── (x) data_source ──────────────────────────────────────────────────────

    [Fact]
    public async Task An_unknown_data_source_is_refused_by_model_binding_before_it_reaches_the_database()
    {
        // Two layers guard this: the enum converter refuses to bind, and ck_lux_reading_data_source
        // would refuse the row. The binder is the one that answers here, so the caller gets a 400
        // rather than a 500 wrapping a constraint violation.
        using var client = await ClientAsync(AdminUser);
        var poleId = await NewPoleAsync();

        var (status, _) = await PostAsync(client, Body(poleId, dataSource: "not_a_real_source"));

        Assert.Equal(HttpStatusCode.BadRequest, status);
    }

    // ── (xi) and (xii) the two GET shapes ────────────────────────────────────

    [Fact]
    public async Task The_bulk_endpoint_always_emits_the_nearest_luminance_key_even_though_it_is_null()
    {
        // CV-12 binds against the final shape today. A missing key and a null value are different
        // things to a client, and the Contract publishes the key.
        using var client = await ClientAsync(AdminUser);
        var poleId = await NewPoleAsync();

        var (_, created) = await PostAsync(client, Body(poleId));
        createdLuxIds.Add(created.GetProperty("lux_id").GetString()!);

        var json = JsonDocument.Parse(
            await client.GetStringAsync($"/api/v1/lux-readings?pole_id={poleId}"));

        var item = json.RootElement.GetProperty("items").EnumerateArray().First();

        Assert.True(item.TryGetProperty("nearest_luminance", out var nearest));
        Assert.Equal(JsonValueKind.Null, nearest.ValueKind);
    }

    [Fact]
    public async Task The_per_pole_endpoint_omits_nearest_luminance_and_sorts_oldest_first()
    {
        using var client = await ClientAsync(AdminUser);
        var poleId = await NewPoleAsync();

        foreach (var day in new[] { 3, 1, 2 })
        {
            var body = (Dictionary<string, object?>)Body(poleId);
            body["measured_at"] = $"2026-10-0{day}T19:42:00Z";
            var (_, created) = await PostAsync(client, body);
            createdLuxIds.Add(created.GetProperty("lux_id").GetString()!);
        }

        var json = JsonDocument.Parse(
            await client.GetStringAsync($"/api/v1/poles/{poleId}/lux-readings"));

        var items = json.RootElement.GetProperty("items").EnumerateArray().ToArray();

        Assert.Equal(3, items.Length);
        Assert.False(items[0].TryGetProperty("nearest_luminance", out _));

        var times = items.Select(item => item.GetProperty("measured_at").GetDateTime()).ToArray();
        Assert.Equal(times.OrderBy(time => time), times);
    }

    // ── (xiii) delete behaviour ──────────────────────────────────────────────

    [Fact]
    public async Task Deleting_a_pole_that_carries_readings_is_refused_by_the_foreign_key()
    {
        // RESTRICT, not cascade. A lux reading is the ground truth for RQ1; removing a pole must not
        // quietly remove research data. It also keeps this out of the cascade blind spot recorded in
        // CLAUDE.md 1c, where the SaveChanges guard cannot see deletions the database performs.
        using var client = await ClientAsync(AdminUser);
        var poleId = await NewPoleAsync();

        var (_, created) = await PostAsync(client, Body(poleId));
        createdLuxIds.Add(created.GetProperty("lux_id").GetString()!);

        var failure = await Assert.ThrowsAsync<DbUpdateException>(() =>
            fixture.WriteAsSystemAsync(async db =>
            {
                var pole = await db.Set<Pole>().IgnoreQueryFilters()
                    .SingleAsync(candidate => candidate.PoleId == poleId);
                db.Set<Pole>().Remove(pole);
                return await db.SaveChangesAsync();
            }));

        Assert.Contains("23503", failure.InnerException?.Message ?? string.Empty, StringComparison.Ordinal);
    }
}
