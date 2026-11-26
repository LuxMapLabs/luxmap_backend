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
/// COMMUNE-BOUNDARY-GUARD — a point asset must stand inside the outline of its own commune; a commune with no outline refuses
/// a position (D-1/D-2). The shared fixture's communes already carry <see cref="AssetImportFixture.TestArea"/>, so the tests
/// that need a boundary edge, a missing outline, or a position just outside it make their own commune instead of editing the
/// one every other class uses.
/// </summary>
[Collection(nameof(AssetDatabaseCollection))]
public sealed class CommuneBoundaryGuardTests(AssetImportFixture fixture) : IAsyncLifetime
{
    private const string Poles = "/api/v1/assets/poles";

    private readonly List<string> owned = [];

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        if (owned.Count == 0)
        {
            return;
        }

        var ids = owned.ToArray();
        await fixture.QueryAsync(async db =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            await db.Database.ExecuteSqlRawAsync("SET LOCAL luxmap.audit_purge = 'on'");
            // The tests here write only poles; delete them before the anchor their FK rests on.
            await db.Database.ExecuteSqlRawAsync("DELETE FROM audit_event WHERE commune_id = ANY({0})", [ids]);
            await db.Database.ExecuteSqlRawAsync("DELETE FROM pole WHERE commune_id = ANY({0})", [ids]);
            await db.Database.ExecuteSqlRawAsync("DELETE FROM administrative_unit WHERE commune_id = ANY({0})", [ids]);
            await transaction.CommitAsync();
            return 0;
        });
    }

    [Fact]
    public async Task A_point_outside_the_outline_is_400_with_the_outside_reason()
    {
        using var client = await fixture.ManagerClientAsync();

        var response = await client.PostAsJsonAsync(Poles, new
        {
            commune_id = fixture.CommuneId, geom_wkt = "POINT(107 10)", data_source = "simulated",
        });

        var error = await Error(response);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ErrorCodes.ValidationFailed, error.GetProperty("code").GetString());
        var details = error.GetProperty("details");
        Assert.Equal("geom_wkt", details.GetProperty("field").GetString());
        Assert.Equal("OUTSIDE_COMMUNE_BOUNDARY", details.GetProperty("reason").GetString());
        Assert.Equal(fixture.CommuneId, details.GetProperty("commune_id").GetString());
    }

    [Fact]
    public async Task A_commune_with_no_outline_refuses_a_position_with_the_missing_reason()
    {
        var commune = await NewCommuneAsync(boundary: null);
        using var client = fixture.ManagerClientFor(commune);

        var response = await client.PostAsJsonAsync(Poles, new
        {
            commune_id = commune, geom_wkt = "POINT(106.05 10.05)", data_source = "simulated",
        });

        var error = await Error(response);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var details = error.GetProperty("details");
        Assert.Equal("geom_wkt", details.GetProperty("field").GetString());
        Assert.Equal("COMMUNE_BOUNDARY_MISSING", details.GetProperty("reason").GetString());
        Assert.Equal(commune, details.GetProperty("commune_id").GetString());
    }

    [Fact]
    public async Task A_pole_without_a_position_is_created_even_where_no_outline_exists()
    {
        var commune = await NewCommuneAsync(boundary: null);
        using var client = fixture.ManagerClientFor(commune);

        var response = await client.PostAsJsonAsync(Poles, new
        {
            commune_id = commune, geom_wkt = (string?)null, data_source = "simulated",
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task A_point_exactly_on_the_outline_counts_as_inside()
    {
        var commune = await NewCommuneAsync(AssetImportFixture.Rectangles((106.0, 10.0, 106.1, 10.1)));
        using var client = fixture.ManagerClientFor(commune);

        var response = await client.PostAsJsonAsync(Poles, new
        {
            commune_id = commune, geom_wkt = "POINT(106.0 10.0)", data_source = "simulated",
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    /// <summary>
    /// D-4/D-10: an outline can be drawn after a pole was already standing outside it. Sending that same X/Y back is not a
    /// move, so the pole's other fields stay editable; changing the position is what the rule refuses.
    /// </summary>
    [Fact]
    public async Task Editing_an_old_pole_outside_the_outline_without_moving_it_is_accepted_but_moving_it_is_refused()
    {
        var commune = await NewCommuneAsync(boundary: null);
        var poleId = await fixture.QueryAsync(async db =>
        {
            using var system = db.EnterUnscopedSystemWriteBackdoor();
            var pole = new Pole { CommuneId = commune, DataSource = DataSource.Simulated, Geom = new Point(107, 10) { SRID = 4326 } };
            db.Add(pole);
            await db.SaveChangesAsync();
            return pole.PoleId;
        });
        await fixture.SetBoundaryAsync(commune, AssetImportFixture.Rectangles((106.0, 10.0, 106.1, 10.1)));
        using var client = fixture.ManagerClientFor(commune);

        var unchanged = await client.PutAsJsonAsync($"{Poles}/{poleId}", new
        {
            segment_id = (string?)null, geom_wkt = "POINT(107 10)", data_source = "simulated", near_sensitive_poi = false,
        });
        Assert.Equal(HttpStatusCode.NoContent, unchanged.StatusCode);

        var moved = await client.PutAsJsonAsync($"{Poles}/{poleId}", new
        {
            segment_id = (string?)null, geom_wkt = "POINT(107.1 10)", data_source = "simulated", near_sensitive_poi = false,
        });
        var error = await Error(moved);
        Assert.Equal(HttpStatusCode.BadRequest, moved.StatusCode);
        Assert.Equal("OUTSIDE_COMMUNE_BOUNDARY", error.GetProperty("details").GetProperty("reason").GetString());
    }

    [Fact]
    public async Task Importing_a_row_outside_the_outline_fails_that_row_and_keeps_the_inside_rows()
    {
        using var client = await fixture.ManagerClientAsync();
        var tag = $"cbg{Guid.NewGuid():N}"[..12];

        var result = await AssetImportTests.ImportAsync(client, "poles", "poles.csv",
            "external_ref,commune_id,geom_wkt,data_source"
            + $"\n{tag}-IN,{fixture.CommuneId},POINT(106.490 10.970),public_imagery"
            + $"\n{tag}-OUT,{fixture.CommuneId},POINT(107 10),public_imagery");

        Assert.Equal(1, result.GetProperty("inserted").GetInt32());
        Assert.Equal(1, result.GetProperty("failed").GetInt32());
        var row = result.GetProperty("rows")[0];
        Assert.Equal(3, row.GetProperty("row").GetInt32());
        Assert.Equal("geom_wkt", row.GetProperty("column").GetString());
        Assert.Contains("outside the boundary", row.GetProperty("message").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Importing_into_a_commune_with_no_outline_is_a_commune_id_row_error()
    {
        var commune = await NewCommuneAsync(boundary: null);
        using var client = fixture.ManagerClientFor(commune);
        var tag = $"cbg{Guid.NewGuid():N}"[..12];

        var result = await AssetImportTests.ImportAsync(client, "poles", "poles.csv",
            "external_ref,commune_id,geom_wkt,data_source"
            + $"\n{tag}-1,{commune},POINT(106.05 10.05),public_imagery");

        Assert.Equal(0, result.GetProperty("inserted").GetInt32());
        Assert.Equal(1, result.GetProperty("failed").GetInt32());
        var row = result.GetProperty("rows")[0];
        Assert.Equal("commune_id", row.GetProperty("column").GetString());
        Assert.Contains("no boundary", row.GetProperty("message").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>D-12: the cross-commune feeder 409 is the more specific complaint and keeps its answer even when the point is also outside.</summary>
    [Fact]
    public async Task A_wrong_feeder_and_a_point_outside_the_outline_still_answers_409()
    {
        using var client = await fixture.LegacyTwoCommuneClientAsync();
        var foreignFeeder = await fixture.QueryAsync(async db =>
        {
            using var system = db.EnterUnscopedSystemWriteBackdoor();
            var feeder = new Feeder { FeederName = "boundary guard probe", CommuneId = fixture.ForeignCommuneId };
            db.Add(feeder);
            await db.SaveChangesAsync();
            return feeder.FeederId;
        });

        var response = await client.PostAsJsonAsync(Poles, new
        {
            commune_id = fixture.CommuneId, feeder_id = foreignFeeder, geom_wkt = "POINT(107 10)", data_source = "simulated",
        });

        var error = await Error(response);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(ErrorCodes.CrossCommuneReference, error.GetProperty("code").GetString());
    }

    /// <summary>
    /// D-5: an import holds each commune row <c>FOR SHARE</c> while it checks the outlines. A writer holding
    /// <c>FOR NO KEY UPDATE</c> must make the import wait — holding <c>FOR UPDATE</c> would also block a mere foreign-key
    /// <c>KEY SHARE</c> and prove nothing about the explicit lock.
    /// </summary>
    [Fact]
    public async Task An_import_waits_for_the_commune_row_lock_before_it_writes()
    {
        using var client = await fixture.ManagerClientAsync();
        var tag = $"cbg{Guid.NewGuid():N}"[..12];

        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LuxMapDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlAsync(
            $"SELECT 1 FROM administrative_unit WHERE commune_id = {fixture.CommuneId} FOR NO KEY UPDATE");

        var import = AssetImportTests.ImportAsync(client, "poles", "poles.csv",
            "external_ref,commune_id,geom_wkt,data_source"
            + $"\n{tag}-LOCK,{fixture.CommuneId},POINT(106.490 10.970),public_imagery");
        await Task.WhenAny(import, Task.Delay(TimeSpan.FromMilliseconds(500)));
        Assert.False(import.IsCompleted, "the import finished while the commune row was locked");

        await transaction.RollbackAsync();
        var result = await import;
        Assert.Equal(1, result.GetProperty("inserted").GetInt32());
    }

    /// <summary>
    /// D-5: rewriting an outline is an <c>UPDATE</c> of the commune row, whose <c>FOR NO KEY UPDATE</c> lock waits for a
    /// relocation or import holding <c>FOR SHARE</c> — so the check finishes against the shape it read, never against a
    /// shape replaced mid-check.
    /// </summary>
    [Fact]
    public async Task Setting_the_boundary_waits_for_a_share_lock_on_the_commune()
    {
        var commune = await NewCommuneAsync(boundary: null);
        using var admin = await fixture.SeededClientAsync("admin", "SEED_ADMIN_PASSWORD");

        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LuxMapDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlAsync(
            $"SELECT 1 FROM administrative_unit WHERE commune_id = {commune} FOR SHARE");

        var update = admin.PutAsJsonAsync($"/api/v1/admin/communes/{commune}/boundary", new
        {
            boundary = new
            {
                type = "Polygon",
                coordinates = new[]
                {
                    new[] { new[] { 106.0, 10.0 }, new[] { 106.1, 10.0 }, new[] { 106.1, 10.1 }, new[] { 106.0, 10.1 }, new[] { 106.0, 10.0 } },
                },
            },
        });
        await Task.WhenAny(update, Task.Delay(TimeSpan.FromMilliseconds(500)));
        Assert.False(update.IsCompleted, "the boundary update finished while a share lock was held");

        await transaction.RollbackAsync();
        var response = await update;
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task<string> NewCommuneAsync(MultiPolygon? boundary)
    {
        var commune = await fixture.QueryAsync(async db =>
        {
            var unit = new AdministrativeUnit { Name = $"cbg-{Guid.NewGuid():N}", Boundary = boundary };
            db.Add(unit);
            await db.SaveChangesAsync();
            return unit.CommuneId;
        });
        owned.Add(commune);
        return commune;
    }

    private static async Task<JsonElement> Error(HttpResponseMessage response)
        => JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("error");
}
