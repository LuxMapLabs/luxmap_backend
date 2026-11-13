using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LuxMap.Modules.Identity.Entities;
using LuxMap.Persistence;
using LuxMap.Shared.Contracts.Errors;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LuxMap.Api.Tests;

/// <summary>
/// COMMUNE-BOUNDARY — the system admin sets a commune's outline; <c>GET /map/communes</c> hands each user the outlines of
/// their own communes and no one else's, so the map can draw the area they work in.
/// </summary>
/// <remarks>
/// Own prefixes for communes and accounts, for the reason <c>CommuneAdminTests</c> gives: other classes create and delete
/// communes and accounts in parallel. Accounts go first in teardown — their commune assignments hold the communes.
/// </remarks>
public class CommuneBoundaryTests(AccountTestFactory factory) : IClassFixture<AccountTestFactory>, IAsyncLifetime
{
    private const string NamePrefix = "bnd-test-";
    private const string EmailPrefix = "bnd-";
    private const string AdminRoute = "/api/v1/admin/communes";
    private const string MapRoute = "/api/v1/map/communes";
    private const string Password = "a-long-enough-new-password";

    /// <summary>A small square near the study site, outer ring closed, <c>[lng, lat]</c>.</summary>
    private static readonly double[][][] Square =
        [[[107.20, 10.50], [107.21, 10.50], [107.21, 10.51], [107.20, 10.51], [107.20, 10.50]]];

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await factory.QueryAsync(async db =>
        {
            #pragma warning disable RS0030 // Test TEARDOWN: bulk delete of this class's own accounts, then the communes they held.
            await db.Set<AppUser>().Where(user => user.Email.StartsWith(EmailPrefix)).ExecuteDeleteAsync();
            return await db.Set<AdministrativeUnit>().Where(unit => unit.Name.StartsWith(NamePrefix)).ExecuteDeleteAsync();
            #pragma warning restore RS0030
        });
    }

    /// <summary>The real OSM outline of Long Phước goes in as a Polygon and comes back out of the map layer unchanged.</summary>
    [Fact]
    public async Task The_admin_sets_an_osm_outline_and_the_map_draws_it_as_a_multipolygon()
    {
        var admin = await AdminAsync();
        var id = await CreateAsync(admin);
        var osm = OsmFeature("COM-001").GetProperty("geometry");

        var response = await admin.PutAsJsonAsync($"{AdminRoute}/{id}/boundary", new { boundary = osm });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True((await Json(response)).GetProperty("has_boundary").GetBoolean());

        var layer = await Json(await admin.GetAsync($"{MapRoute}?commune_id={id}"));
        Assert.Equal("FeatureCollection", layer.GetProperty("type").GetString());
        var feature = Assert.Single(layer.GetProperty("features").EnumerateArray());
        Assert.False(feature.TryGetProperty("id", out _));
        Assert.Equal(id, feature.GetProperty("properties").GetProperty("commune_id").GetString());
        var geometry = feature.GetProperty("geometry");
        Assert.Equal("MultiPolygon", geometry.GetProperty("type").GetString());

        var sent = osm.GetProperty("coordinates")[0];
        var drawn = geometry.GetProperty("coordinates")[0][0];
        Assert.Equal(sent.GetArrayLength(), drawn.GetArrayLength());
        // [lng, lat] in, [lng, lat] out — a swap would put the first vertex at latitude 106.
        Assert.Equal(sent[0][0].GetDouble(), drawn[0][0].GetDouble());
        Assert.Equal(sent[0][1].GetDouble(), drawn[0][1].GetDouble());
    }

    /// <summary>The point of the feature: a manager sees the outline of their own commune and not of the one next door.</summary>
    [Fact]
    public async Task A_manager_gets_only_the_outlines_of_their_own_communes()
    {
        var admin = await AdminAsync();
        var mine = await CreateAsync(admin);
        var theirs = await CreateAsync(admin);
        foreach (var id in new[] { mine, theirs })
        {
            Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync($"{AdminRoute}/{id}/boundary", new { boundary = Polygon(Square) })).StatusCode);
        }

        var manager = await ManagerAsync(admin, mine);

        var layer = await Json(await manager.GetAsync(MapRoute));
        var ids = layer.GetProperty("features").EnumerateArray()
            .Select(feature => feature.GetProperty("properties").GetProperty("commune_id").GetString()).ToList();
        Assert.Equal([mine], ids);

        var asked = await manager.GetAsync($"{MapRoute}?commune_id={theirs}");
        Assert.Equal(HttpStatusCode.Forbidden, asked.StatusCode);
        Assert.Contains(ErrorCodes.CommuneForbidden, await asked.Content.ReadAsStringAsync());
    }

    public static TheoryData<string, string> BadOutlines() => new()
    {
        { "unclosed ring", """{"type":"Polygon","coordinates":[[[107.20,10.50],[107.21,10.50],[107.21,10.51],[107.20,10.51]]]}""" },
        { "self-intersecting", """{"type":"Polygon","coordinates":[[[107.20,10.50],[107.21,10.51],[107.21,10.50],[107.20,10.51],[107.20,10.50]]]}""" },
        { "lat / lng swapped", """{"type":"Polygon","coordinates":[[[10.50,107.20],[10.50,107.21],[10.51,107.21],[10.51,107.20],[10.50,107.20]]]}""" },
        { "not a polygon", """{"type":"Point","coordinates":[107.20,10.50]}""" },
        { "no rings", """{"type":"Polygon","coordinates":[]}""" },
        { "not a number", """{"type":"Polygon","coordinates":[[["a",10.50],[107.21,10.50],[107.21,10.51],["a",10.50]]]}""" },
    };

    [Theory]
    [MemberData(nameof(BadOutlines))]
    public async Task A_bad_outline_is_400_and_stores_nothing(string why, string geometry)
    {
        var admin = await AdminAsync();
        var id = await CreateAsync(admin);

        var response = await admin.PutAsync($"{AdminRoute}/{id}/boundary",
            JsonContent.Create(JsonDocument.Parse($$"""{"boundary":{{geometry}}}""").RootElement));

        Assert.True(response.StatusCode == HttpStatusCode.BadRequest, why);
        Assert.Contains(ErrorCodes.ValidationFailed, await response.Content.ReadAsStringAsync());
        Assert.False(await factory.QueryAsync(db => db.Set<AdministrativeUnit>().AnyAsync(unit => unit.CommuneId == id && unit.Boundary != null)));
    }

    /// <summary>A form that forgets the key must not clear the outline; an explicit null does.</summary>
    [Fact]
    public async Task The_key_is_required_and_an_explicit_null_clears_the_outline()
    {
        var admin = await AdminAsync();
        var id = await CreateAsync(admin);
        await admin.PutAsJsonAsync($"{AdminRoute}/{id}/boundary", new { boundary = Polygon(Square) });

        var withoutKey = await admin.PutAsJsonAsync($"{AdminRoute}/{id}/boundary", new { });
        Assert.Equal(HttpStatusCode.BadRequest, withoutKey.StatusCode);
        Assert.True((await Json(await admin.GetAsync($"{AdminRoute}/{id}"))).GetProperty("has_boundary").GetBoolean());

        var cleared = await Json(await admin.PutAsJsonAsync($"{AdminRoute}/{id}/boundary", new { boundary = (object?)null }));
        Assert.False(cleared.GetProperty("has_boundary").GetBoolean());
        Assert.Empty((await Json(await admin.GetAsync($"{MapRoute}?commune_id={id}"))).GetProperty("features").EnumerateArray());
    }

    [Fact]
    public async Task Sending_the_same_outline_again_leaves_updated_at_alone()
    {
        var admin = await AdminAsync();
        var id = await CreateAsync(admin);
        var first = await Json(await admin.PutAsJsonAsync($"{AdminRoute}/{id}/boundary", new { boundary = Polygon(Square) }));

        var again = await Json(await admin.PutAsJsonAsync($"{AdminRoute}/{id}/boundary", new { boundary = Polygon(Square) }));

        Assert.Equal(first.GetProperty("updated_at").GetString(), again.GetProperty("updated_at").GetString());
    }

    [Fact]
    public async Task An_unknown_commune_is_404()
    {
        var response = await (await AdminAsync()).PutAsJsonAsync($"{AdminRoute}/COM-99999999/boundary", new { boundary = Polygon(Square) });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains(ErrorCodes.CommuneNotFound, await response.Content.ReadAsStringAsync());
    }

    /// <summary>A writer that skips the service still cannot store a bow-tie.</summary>
    [Fact]
    public async Task The_database_refuses_an_invalid_outline()
    {
        var id = await CreateAsync(await AdminAsync());

        var error = await Assert.ThrowsAsync<PostgresException>(() => factory.QueryAsync(db => db.Database.ExecuteSqlRawAsync(
            "UPDATE administrative_unit SET boundary = ST_GeomFromText("
            + "'MULTIPOLYGON(((107.20 10.50, 107.21 10.51, 107.21 10.50, 107.20 10.51, 107.20 10.50)))', 4326) "
            + "WHERE commune_id = {0}", id)));

        Assert.Equal("23514", error.SqlState);
    }

    private static object Polygon(double[][][] rings) => new { type = "Polygon", coordinates = rings };

    /// <summary>The feature for <paramref name="communeId"/> in the OSM file the loader script sends.</summary>
    private static JsonElement OsmFeature(string communeId)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "LuxMap.slnx")))
        {
            directory = directory.Parent;
        }

        var file = Path.Combine(directory!.FullName, "scripts", "data", "commune-boundaries.geojson");
        return JsonDocument.Parse(File.ReadAllText(file)).RootElement.GetProperty("features").EnumerateArray()
            .Single(feature => feature.GetProperty("properties").GetProperty("commune_id").GetString() == communeId);
    }

    private static async Task<string> CreateAsync(HttpClient admin)
    {
        var response = await admin.PostAsJsonAsync(AdminRoute, new { name = $"{NamePrefix}{Guid.NewGuid():N}" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await Json(response)).GetProperty("commune_id").GetString()!;
    }

    /// <summary>A throwaway manager of <paramref name="commune"/> (ACCT-1: one per account), invited and signed in.</summary>
    private async Task<HttpClient> ManagerAsync(HttpClient admin, string commune)
    {
        var email = $"{EmailPrefix}{Guid.NewGuid():N}@example.invalid";
        var created = await admin.PostAsJsonAsync("/api/v1/admin/users",
            new { email, full_name = "Quản lý thử", role = "manager", commune_ids = new[] { commune } });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var username = (await Json(created)).GetProperty("user").GetProperty("username").GetString()!;
        var token = factory.Mail.LatestTokenFor(email);
        var set = await factory.CreateClient().PostAsJsonAsync("/api/v1/auth/password/set", new { token, new_password = Password });
        Assert.Equal(HttpStatusCode.NoContent, set.StatusCode);

        var tokens = await (await factory.CreateClient().PostLoginAsync(username, Password)).ReadTokensAsync();
        return Bearer(tokens.AccessToken);
    }

    private async Task<HttpClient> AdminAsync()
        => Bearer((await factory.CreateClient().LoginAsync("admin", "SEED_ADMIN_PASSWORD")).AccessToken);

    private HttpClient Bearer(string accessToken)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", accessToken);
        return client;
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response)
        => JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
}
