using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using LuxMap.Modules.Identity.Auth;
using LuxMap.Modules.Identity.Entities;
using LuxMap.Persistence;
using LuxMap.Persistence.Conventions;
using LuxMap.Shared.Contracts.Errors;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace LuxMap.Api.Tests;

/// <summary>
/// Open registration. The security claim being tested is: registration creates an IDENTITY, never a
/// PERMISSION. Every assertion about role and scope is checked against the DATABASE and against a
/// decoded token, not against the endpoint's own response.
/// </summary>
[Collection(nameof(ScopeCollection))]
public class RegistrationTests(ScopeTestFixture factory, ITestOutputHelper output) : IAsyncLifetime
{
    private HttpClient Client => factory.CreateClient();

    private const string StrongPassword = "a-perfectly-fine-passphrase";

    /// <summary>Every account this test class registered, so <see cref="DisposeAsync"/> can remove them.</summary>
    private readonly List<string> registered = [];

    /// <summary>
    /// A fresh account name, recorded so it can be deleted afterwards.
    /// </summary>
    /// <remarks>
    /// Registration goes through the real endpoint, so the row is created by the application and no
    /// fixture owns it. Nothing removed these: 857 of them had accumulated on the shared development
    /// database before anyone counted. Recording the name here is what makes teardown EXACT — matching
    /// on a <c>newcomer%</c> prefix would also catch a run happening beside this one.
    /// </remarks>
    private string UniqueName()
    {
        var name = $"newcomer{Guid.NewGuid():N}"[..20];
        registered.Add(name);
        return name;
    }

    public Task InitializeAsync() => Task.CompletedTask;

    /// <summary>
    /// Deletes the accounts this class registered, newest first.
    /// </summary>
    /// <remarks>
    /// xUnit builds one instance per test, so this runs per test and each one cleans up only its own
    /// rows. <c>refresh_token</c> would cascade with the user, but the tests that sign in leave rows
    /// behind that are easier to reason about deleted explicitly, in foreign-key order.
    /// </remarks>
    public async Task DisposeAsync()
    {
        if (registered.Count == 0)
        {
            return;
        }

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LuxMapDbContext>();

        var names = registered.ToArray();
        var ids = await db.Set<AppUser>()
            .Where(user => names.Contains(user.Username))
            .Select(user => user.UserId)
            .ToArrayAsync();

        #pragma warning disable RS0030 // Test TEARDOWN: bulk delete is the only way to clean up under an empty scope. BE-36 removes the need entirely — a fresh database per run.
        await db.Set<RefreshToken>().Where(t => ids.Contains(t.UserId)).ExecuteDeleteAsync();
        await db.Set<AppUserCommune>().Where(a => ids.Contains(a.UserId)).ExecuteDeleteAsync();
        await db.Set<AppUser>().Where(u => ids.Contains(u.UserId)).ExecuteDeleteAsync();
        #pragma warning restore RS0030
    }

    private static JsonElement DecodePayload(string jwt)
    {
        var part = jwt.Split('.')[1];
        part = part.PadRight(part.Length + ((4 - (part.Length % 4)) % 4), '=');
        return JsonDocument.Parse(Convert.FromBase64String(part.Replace('-', '+').Replace('_', '/'))).RootElement;
    }

    private Task<HttpResponseMessage> RegisterAsync(object body)
        => Client.PostAsJsonAsync("/api/v1/auth/register", body);

    private static object ValidBody(string name) => new
    {
        username = name,
        email = $"{name}@luxmap.local",
        full_name = "Newly Registered Person",
        password = StrongPassword,
    };

    private async Task<HttpClient> SignInAsync(string username)
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync(
            "/api/v1/auth/login", new { username, password = StrongPassword });
        response.EnsureSuccessStatusCode();

        var token = JsonDocument.Parse(await response.Content.ReadAsStringAsync())
            .RootElement.GetProperty("access_token").GetString()!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private Task<AppUser?> LoadAsync(string username)
        => factory.QueryAsync(db => db.Set<AppUser>().AsNoTracking()
            .FirstOrDefaultAsync(u => u.Username == username));

    // ── Functional ────────────────────────────────────────────────────────

    [Fact]
    public async Task Registration_creates_the_account_and_returns_201()
    {
        var name = UniqueName();
        var response = await RegisterAsync(ValidBody(name));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        output.WriteLine($"  201 body: {body.GetRawText()}");

        Assert.Equal(name, body.GetProperty("username").GetString());
        Assert.Equal("field_crew", body.GetProperty("role").GetString());
        Assert.Empty(body.GetProperty("commune_ids").EnumerateArray());

        // No token is handed out — POST /auth/login stays the single token-issuing path.
        Assert.False(body.TryGetProperty("access_token", out _));
        Assert.False(body.TryGetProperty("refresh_token", out _));

        var stored = await LoadAsync(name);
        Assert.NotNull(stored);
        Assert.Equal("field_crew", UserRoleOf(stored));
        Assert.False(stored.IsLocked);
        Assert.False(stored.HasSystemWideScope);
        Assert.DoesNotContain(StrongPassword, stored.PasswordHash, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Freshly_registered_account_can_sign_in_and_its_token_carries_no_communes()
    {
        var name = UniqueName();
        (await RegisterAsync(ValidBody(name))).EnsureSuccessStatusCode();

        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { username = name, password = StrongPassword });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        var token = JsonDocument.Parse(await login.Content.ReadAsStringAsync())
            .RootElement.GetProperty("access_token").GetString()!;
        var payload = DecodePayload(token);

        output.WriteLine("── decoded payload of the newly registered account ──");
        output.WriteLine(JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));

        Assert.Equal("field_crew", payload.GetProperty("role").GetString());
        Assert.Equal(JsonValueKind.Array, payload.GetProperty("commune_ids").ValueKind);
        Assert.Empty(payload.GetProperty("commune_ids").EnumerateArray());
    }

    // ── Security: the core claim of the whole design ──────────────────────

    [Fact]
    public async Task Freshly_registered_account_sees_no_records_at_all()
    {
        var name = UniqueName();
        (await RegisterAsync(ValidBody(name))).EnsureSuccessStatusCode();

        var client = await SignInAsync(name);
        var listed = await client.GetStringAsync("/api/v1/_scope/probes");
        var rows = JsonDocument.Parse(listed).RootElement.EnumerateArray().Count();

        output.WriteLine($"  records visible to a brand-new account: {rows}");
        Assert.Equal(0, rows);

        // And a direct lookup of a real row is 404, not 403 — existence is not confirmed.
        var lookup = await client.GetAsync($"/api/v1/_scope/probes/{factory.InScopeProbeId}");
        output.WriteLine($"  direct lookup of an existing row → HTTP {(int)lookup.StatusCode}");
        Assert.Equal(HttpStatusCode.NotFound, lookup.StatusCode);
    }

    // ── Security: privilege escalation attempts ───────────────────────────

    [Fact]
    public async Task Sending_an_administrator_role_in_the_body_is_ignored()
    {
        var name = UniqueName();
        var response = await RegisterAsync(new
        {
            username = name,
            email = $"{name}@luxmap.local",
            full_name = "Would-be admin",
            password = StrongPassword,
            role = "administrator",
        });

        response.EnsureSuccessStatusCode();
        var stored = await LoadAsync(name);

        output.WriteLine($"  body said role=administrator → stored role is {UserRoleOf(stored!)}");
        Assert.Equal("field_crew", UserRoleOf(stored!));
        Assert.False(stored!.HasSystemWideScope);
    }

    [Fact]
    public async Task Sending_a_wildcard_commune_scope_in_the_body_is_ignored_and_stays_rejected()
    {
        var name = UniqueName();
        var response = await RegisterAsync(new
        {
            username = name,
            email = $"{name}@luxmap.local",
            full_name = "Would-be system wide",
            password = StrongPassword,
            commune_ids = new[] { "*" },
        });

        response.EnsureSuccessStatusCode();

        var stored = await LoadAsync(name);
        Assert.False(stored!.HasSystemWideScope);

        var communeCount = await factory.QueryAsync(db => db.Set<AppUserCommune>()
            .CountAsync(a => a.UserId == stored.UserId));

        output.WriteLine($"  body said commune_ids=[\"*\"] → stored assignments: {communeCount}, system-wide flag: {stored.HasSystemWideScope}");
        Assert.Equal(0, communeCount);

        // Signing in still yields an EMPTY scope, so the BE-08 wildcard cross-check never even applies.
        var client = await SignInAsync(name);
        var rows = JsonDocument.Parse(await client.GetStringAsync("/api/v1/_scope/probes"))
            .RootElement.EnumerateArray().Count();
        Assert.Equal(0, rows);
    }

    [Fact]
    public async Task Sending_a_concrete_commune_id_in_the_body_is_ignored()
    {
        var name = UniqueName();
        var response = await RegisterAsync(new
        {
            username = name,
            email = $"{name}@luxmap.local",
            full_name = "Would-be assigned",
            password = StrongPassword,
            commune_ids = new[] { factory.InScopeCommune },
            commune_id = factory.InScopeCommune,
        });

        response.EnsureSuccessStatusCode();
        var stored = await LoadAsync(name);

        var communeCount = await factory.QueryAsync(db => db.Set<AppUserCommune>()
            .CountAsync(a => a.UserId == stored!.UserId));

        output.WriteLine($"  body said commune_id=COM-001 → stored assignments: {communeCount}");
        Assert.Equal(0, communeCount);
    }

    // ── Duplicates and policy ─────────────────────────────────────────────

    [Fact]
    public async Task Duplicate_username_returns_409_naming_the_field()
    {
        var name = UniqueName();
        (await RegisterAsync(ValidBody(name))).EnsureSuccessStatusCode();

        var again = await RegisterAsync(new
        {
            username = name,
            email = $"other-{name}@luxmap.local",
            full_name = "Someone else",
            password = StrongPassword,
        });

        output.WriteLine($"  duplicate username → HTTP {(int)again.StatusCode}");
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);

        var error = JsonDocument.Parse(await again.Content.ReadAsStringAsync()).RootElement.GetProperty("error");
        Assert.Equal(ErrorCodes.IdentifierTaken, error.GetProperty("code").GetString());
        Assert.True(error.GetProperty("details").TryGetProperty("username", out _));
    }

    [Fact]
    public async Task Case_variant_of_an_existing_username_is_also_a_duplicate()
    {
        var again = await RegisterAsync(new
        {
            username = "ENGINEER",
            email = $"variant{Guid.NewGuid():N}"[..16] + "@luxmap.local",
            full_name = "Case variant",
            password = StrongPassword,
        });

        output.WriteLine($"  'ENGINEER' while 'engineer' exists → HTTP {(int)again.StatusCode}");
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }

    [Theory]
    [InlineData("short")]
    [InlineData("elevenchars")]
    public async Task Password_below_the_policy_is_rejected(string weak)
    {
        var name = UniqueName();
        var response = await RegisterAsync(new
        {
            username = name,
            email = $"{name}@luxmap.local",
            full_name = "Weak password",
            password = weak,
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var error = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("error");
        Assert.Equal(ErrorCodes.ValidationFailed, error.GetProperty("code").GetString());
        Assert.True(error.GetProperty("details").TryGetProperty("password", out _));

        Assert.Null(await LoadAsync(name));
    }

    [Fact]
    public async Task Registration_is_reachable_without_a_token()
    {
        // The whole point of open registration: no credentials needed to reach it.
        var response = await RegisterAsync(ValidBody(UniqueName()));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private static string UserRoleOf(AppUser user) => ContractEnum.ToDbValue(user.Role);
}
