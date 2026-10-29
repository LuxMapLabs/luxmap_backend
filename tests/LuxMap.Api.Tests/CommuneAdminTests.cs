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
/// COMMUNE-BBOX — the system admin lists, creates and edits communes with a map frame; <c>GET /auth/me</c> hands each user
/// the frames of their communes and the box around them, so the map opens there and stays there.
/// </summary>
/// <remarks>
/// Every commune here is named under <see cref="NamePrefix"/> and every account under <see cref="UsernamePrefix"/> — NOT
/// <see cref="AccountTestFactory.UsernamePrefix"/>: <c>AccountAdminTests</c> runs in parallel and deletes every account under
/// that one after each of its tests. Both go after each test, accounts first (their commune assignments hold the communes).
/// Nothing here counts communes: other classes create them at the same time. The seeded communes and accounts are only read.
/// </remarks>
public class CommuneAdminTests(AccountTestFactory factory) : IClassFixture<AccountTestFactory>, IAsyncLifetime
{
    private const string NamePrefix = "bbox-test-";
    private const string UsernamePrefix = "cbx-";
    private const string Route = "/api/v1/admin/communes";
    private const string Password = "a-long-enough-new-password";

    private static readonly object LongPhuoc = new { min_lng = 107.20, min_lat = 10.48, max_lng = 107.28, max_lat = 10.56 };
    private static readonly object LongBinh = new { min_lng = 107.26, min_lat = 10.52, max_lng = 107.36, max_lat = 10.60 };

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await factory.QueryAsync(async db =>
        {
            #pragma warning disable RS0030 // Test TEARDOWN: bulk delete of this class's own accounts, then the communes they held.
            await db.Set<AppUser>().Where(user => user.Username.StartsWith(UsernamePrefix)).ExecuteDeleteAsync();
            return await db.Set<AdministrativeUnit>().Where(unit => unit.Name.StartsWith(NamePrefix)).ExecuteDeleteAsync();
            #pragma warning restore RS0030
        });
    }

    [Fact]
    public async Task The_admin_creates_a_commune_with_a_frame_and_reads_it_back_with_its_centre()
    {
        var admin = await AdminAsync();
        var name = NewName();

        var response = await admin.PostAsJsonAsync(Route, new { name = $"  {name}  ", bbox = LongPhuoc });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await Json(response);
        var id = created.GetProperty("commune_id").GetString()!;
        Assert.Matches("^COM-[0-9]{3,}$", id);
        Assert.Equal(name, created.GetProperty("name").GetString());
        Assert.Equal(107.20, created.GetProperty("bbox").GetProperty("min_lng").GetDouble());
        Assert.Equal(10.56, created.GetProperty("bbox").GetProperty("max_lat").GetDouble());
        Assert.Equal(10.52, created.GetProperty("center").GetProperty("lat").GetDouble(), 9);
        Assert.Equal(107.24, created.GetProperty("center").GetProperty("lng").GetDouble(), 9);

        var detail = await Json(await admin.GetAsync($"{Route}/{id}"));
        Assert.Equal(created.GetRawText(), detail.GetRawText());

        var list = await Json(await admin.GetAsync($"{Route}?page_size=200"));
        Assert.Contains(list.GetProperty("items").EnumerateArray(), item => item.GetProperty("commune_id").GetString() == id);
    }

    [Fact]
    public async Task A_commune_may_be_created_without_a_frame()
    {
        var created = await Json(await (await AdminAsync()).PostAsJsonAsync(Route, new { name = NewName() }));

        Assert.Equal(JsonValueKind.Null, created.GetProperty("bbox").ValueKind);
        Assert.Equal(JsonValueKind.Null, created.GetProperty("center").ValueKind);
    }

    /// <summary>(name — null for a fresh valid one; bbox as min_lng, min_lat, max_lng, max_lat — null for none).</summary>
    public static TheoryData<string?, double[]?> BadBodies() => new()
    {
        { "   ", null },
        { NamePrefix + new string('x', 201), null },
        { null, [107.3, 10.5, 107.2, 10.6] },   // west edge east of the east edge
        { null, [107.2, 10.6, 107.3, 10.6] },   // no height
        { null, [10.5, 107.2, 10.6, 107.3] },   // lat / lng swapped
        { null, [107.2, 10.5, 181.0, 10.6] },
    };

    [Theory]
    [MemberData(nameof(BadBodies))]
    public async Task A_bad_name_or_frame_is_400_and_creates_nothing(string? name, double[]? frame)
    {
        name ??= NewName();
        var bbox = frame is null ? null : new { min_lng = frame[0], min_lat = frame[1], max_lng = frame[2], max_lat = frame[3] };

        var response = await (await AdminAsync()).PostAsJsonAsync(Route, new { name, bbox });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(ErrorCodes.ValidationFailed, await response.Content.ReadAsStringAsync());
        var trimmed = name.Trim();
        Assert.False(await factory.QueryAsync(db => db.Set<AdministrativeUnit>().AnyAsync(unit => unit.Name == trimmed)));
    }

    /// <summary>A full replacement whose keys are both required: a form that leaves bbox out must not clear it.</summary>
    [Fact]
    public async Task Put_needs_both_keys_and_an_explicit_null_clears_the_frame()
    {
        var admin = await AdminAsync();
        var id = (await Json(await admin.PostAsJsonAsync(Route, new { name = NewName(), bbox = LongPhuoc }))).GetProperty("commune_id").GetString()!;
        var renamed = NewName();

        var withoutBbox = await admin.PutAsJsonAsync($"{Route}/{id}", new { name = renamed });
        Assert.Equal(HttpStatusCode.BadRequest, withoutBbox.StatusCode);
        Assert.NotEqual(JsonValueKind.Null, (await Json(await admin.GetAsync($"{Route}/{id}"))).GetProperty("bbox").ValueKind);

        var moved = await Json(await admin.PutAsJsonAsync($"{Route}/{id}", new { name = renamed, bbox = LongBinh }));
        Assert.Equal(renamed, moved.GetProperty("name").GetString());
        Assert.Equal(107.36, moved.GetProperty("bbox").GetProperty("max_lng").GetDouble());

        var cleared = await Json(await admin.PutAsJsonAsync($"{Route}/{id}", new { name = renamed, bbox = (object?)null }));
        Assert.Equal(JsonValueKind.Null, cleared.GetProperty("bbox").ValueKind);
        Assert.Equal(JsonValueKind.Null, cleared.GetProperty("center").ValueKind);
    }

    [Fact]
    public async Task Saving_the_form_unchanged_leaves_updated_at_alone()
    {
        var admin = await AdminAsync();
        var name = NewName();
        var created = await Json(await admin.PostAsJsonAsync(Route, new { name, bbox = LongPhuoc }));
        var id = created.GetProperty("commune_id").GetString()!;

        var again = await Json(await admin.PutAsJsonAsync($"{Route}/{id}", new { name, bbox = LongPhuoc }));

        Assert.Equal(created.GetProperty("updated_at").GetString(), again.GetProperty("updated_at").GetString());
    }

    [Fact]
    public async Task A_name_another_commune_has_is_409_on_create_and_on_rename()
    {
        var admin = await AdminAsync();
        var taken = NewName();
        await admin.PostAsJsonAsync(Route, new { name = taken });
        var other = (await Json(await admin.PostAsJsonAsync(Route, new { name = NewName() }))).GetProperty("commune_id").GetString()!;

        var create = await admin.PostAsJsonAsync(Route, new { name = taken });
        var rename = await admin.PutAsJsonAsync($"{Route}/{other}", new { name = taken, bbox = (object?)null });

        foreach (var response in new[] { create, rename })
        {
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Contains(ErrorCodes.IdentifierTaken, await response.Content.ReadAsStringAsync());
        }
    }

    [Fact]
    public async Task An_unknown_commune_is_404()
    {
        var admin = await AdminAsync();

        var read = await admin.GetAsync($"{Route}/COM-99999999");
        var write = await admin.PutAsJsonAsync($"{Route}/COM-99999999", new { name = NewName(), bbox = (object?)null });

        foreach (var response in new[] { read, write })
        {
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Contains(ErrorCodes.CommuneNotFound, await response.Content.ReadAsStringAsync());
        }
    }

    /// <summary>
    /// The map opens on the assigned commune's frame. System-wide aggregation is tested separately.
    /// </summary>
    [Fact]
    public async Task Auth_me_lists_the_users_communes_with_frames_and_the_box_around_them()
    {
        var admin = await AdminAsync();
        var phuoc = await CreateAsync(admin, LongPhuoc);
        var manager = await ManagerAsync(admin, phuoc);
        var me = await Json(await manager.GetAsync("/api/v1/auth/me"));
        var commune = Assert.Single(me.GetProperty("communes").EnumerateArray());
        Assert.Equal(phuoc, commune.GetProperty("commune_id").GetString());
        Assert.Equal([phuoc], me.GetProperty("commune_ids").EnumerateArray().Select(c => c.GetString()));
        Assert.Equal(10.52, commune.GetProperty("center").GetProperty("lat").GetDouble(), 9);
        Assert.Equal(commune.GetProperty("bbox").GetRawText(), me.GetProperty("map_bounds").GetProperty("bbox").GetRawText());
    }

    [Fact]
    public async Task With_no_framed_commune_map_bounds_is_null()
    {
        var admin = await AdminAsync();
        var manager = await ManagerAsync(admin, await CreateAsync(admin, null));

        var me = await Json(await manager.GetAsync("/api/v1/auth/me"));

        Assert.Equal(JsonValueKind.Null, me.GetProperty("map_bounds").ValueKind);
        Assert.Single(me.GetProperty("communes").EnumerateArray());
    }

    /// <summary>A system-wide account sees every commune, so its box covers every framed commune.</summary>
    [Fact]
    public async Task The_system_admin_gets_every_commune()
    {
        var admin = await AdminAsync();
        var id = await CreateAsync(admin, LongPhuoc);

        var me = await Json(await admin.GetAsync("/api/v1/auth/me"));

        Assert.Equal(["*"], me.GetProperty("commune_ids").EnumerateArray().Select(c => c.GetString()));
        var seeded = await factory.SeededCommuneIdAsync();
        var ids = me.GetProperty("communes").EnumerateArray().Select(c => c.GetProperty("commune_id").GetString()).ToList();
        Assert.Contains(id, ids);
        Assert.Contains(seeded, ids);
        Assert.NotEqual(JsonValueKind.Null, me.GetProperty("map_bounds").ValueKind);
    }

    public static TheoryData<string> BadFrames() => new()
    {
        "bbox_min_lng = 107.2, bbox_min_lat = NULL, bbox_max_lng = 107.3, bbox_max_lat = 10.6",
        "bbox_min_lng = 'NaN', bbox_min_lat = 10.5, bbox_max_lng = 107.3, bbox_max_lat = 10.6",
        "bbox_min_lng = 107.2, bbox_min_lat = 10.5, bbox_max_lng = 'NaN', bbox_max_lat = 10.6",
        "bbox_min_lng = '-Infinity', bbox_min_lat = 10.5, bbox_max_lng = 107.3, bbox_max_lat = 10.6",
        "bbox_min_lng = 107.2, bbox_min_lat = 10.5, bbox_max_lng = 'Infinity', bbox_max_lat = 10.6",
        "bbox_min_lng = 107.3, bbox_min_lat = 10.5, bbox_max_lng = 107.2, bbox_max_lat = 10.6",
    };

    /// <summary>The database refuses what the API refuses — and NaN / ±Infinity, which no JSON body can carry.</summary>
    [Theory]
    [MemberData(nameof(BadFrames))]
    public async Task The_database_refuses_a_half_set_non_finite_or_inverted_frame(string assignment)
    {
        var id = await CreateAsync(await AdminAsync(), null);

        // The assignment is a literal from BadFrames, never input; only the id is a parameter.
        var sql = "UPDATE administrative_unit SET " + assignment + " WHERE commune_id = {0}";
        var error = await Assert.ThrowsAsync<PostgresException>(() => factory.QueryAsync(db =>
            db.Database.ExecuteSqlRawAsync(sql, id)));

        Assert.Equal("23514", error.SqlState);
    }

    private static string NewName() => $"{NamePrefix}{Guid.NewGuid():N}";

    private static async Task<string> CreateAsync(HttpClient admin, object? bbox)
    {
        var response = await admin.PostAsJsonAsync(Route, new { name = NewName(), bbox });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await Json(response)).GetProperty("commune_id").GetString()!;
    }

    /// <summary>A throwaway manager of <paramref name="communes"/>, invited and signed in — never a seeded account.</summary>
    private async Task<HttpClient> ManagerAsync(HttpClient admin, params string[] communes)
    {
        var username = $"{UsernamePrefix}{Guid.NewGuid():N}"[..24];
        var email = $"{username}@example.invalid";
        var created = await admin.PostAsJsonAsync("/api/v1/admin/users",
            new { username, email, full_name = "Quản lý thử", role = "manager", commune_ids = communes });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

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
