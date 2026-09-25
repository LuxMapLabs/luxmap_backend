using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using LuxMap.Modules.Identity.Entities;
using LuxMap.Shared.Contracts.Errors;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Xunit.Abstractions;

namespace LuxMap.Api.Tests;

/// <summary>
/// <c>GET /api/v1/auth/me</c> — Contract section 4.7.
/// </summary>
/// <remarks>
/// The endpoint exists because a front end needs a display name and a CURRENT scope, and the access
/// token gives neither: it carries no <c>full_name</c> at all, and its <c>commune_ids</c> claim is
/// frozen for the token's 60-minute lifetime. Both of those are pinned below, because both are
/// exactly what a "simplification" would quietly undo by reading the claims instead of the row.
/// </remarks>
[Collection(nameof(ScopeCollection))]
public class CurrentUserTests(ScopeTestFixture factory, ITestOutputHelper output)
{
    private const string Route = "/api/v1/auth/me";

    [Fact]
    public async Task It_returns_the_account_including_the_two_fields_the_token_never_carried()
    {
        var client = await AuthenticatedAsync("engineer", "SEED_ENGINEER_PASSWORD");

        var me = JsonDocument.Parse(await client.GetStringAsync(Route)).RootElement;
        output.WriteLine($"  GET /auth/me → {me.GetRawText()}");

        Assert.Equal(
            ["user_id", "username", "email", "full_name", "role", "commune_ids"],
            me.EnumerateObject().Select(property => property.Name));

        Assert.Equal("engineer", me.GetProperty("username").GetString());
        Assert.Equal("manager", me.GetProperty("role").GetString());

        // Compared against the ROW, not against a literal. The seeded display names are exactly the
        // kind of value that differs between a fresh seed and a long-lived development database — the
        // same trap that once made a hardcoded "COM-001" take 31 tests with it (see ScopeTestFixture).
        // What this endpoint promises is "what the row says", and that is what gets asserted.
        var row = await factory.QueryAsync(db => db.Set<AppUser>().AsNoTracking()
            .Where(user => user.Username == "engineer")
            .Select(user => new { user.UserId, user.Email, user.FullName })
            .SingleAsync());

        Assert.Equal(row.UserId, me.GetProperty("user_id").GetString());

        // The two the JWT never held. Without them the front end cannot show who is signed in.
        Assert.Equal(row.FullName, me.GetProperty("full_name").GetString());
        Assert.Equal(row.Email, me.GetProperty("email").GetString());
        Assert.NotEmpty(row.FullName);

        // And nothing that should never leave the server.
        var raw = me.GetRawText();
        Assert.DoesNotContain("password", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("is_locked", raw, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The reason the endpoint reads the DATABASE and not the token's claims.
    /// </summary>
    /// <remarks>
    /// ⚠️ This is the test that fails the day somebody reimplements <c>/auth/me</c> from
    /// <c>ClaimsPrincipal</c>, which looks simpler and passes every other test in this file. A commune
    /// assigned after the token was issued must show up immediately; the claim inside that same token
    /// must still show the old scope, because the token is not reissued. Both halves are asserted, so
    /// the test cannot be satisfied by an implementation that returns either source alone.
    /// </remarks>
    [Fact]
    public async Task A_commune_assigned_after_sign_in_shows_up_without_signing_in_again()
    {
        await factory.RemoveAllCommunesAsync("agency");
        await factory.AssignCommuneAsync("agency", factory.InScopeCommune);

        try
        {
            var client = await AuthenticatedAsync("agency", "SEED_AGENCY_PASSWORD");
            var token = client.DefaultRequestHeaders.Authorization!.Parameter!;

            var before = await CommunesAsync(client);
            Assert.Equal([factory.InScopeCommune], before);

            // An administrator grants a second commune. The token in hand is untouched.
            await factory.AssignCommuneAsync("agency", factory.SecondCommune);

            var after = await CommunesAsync(client);
            output.WriteLine($"  trước: [{string.Join(", ", before)}] · sau: [{string.Join(", ", after)}]");

            Assert.Equal(2, after.Length);
            Assert.Contains(factory.SecondCommune, after);

            // The other half: the claim inside that SAME token is still stale, which is precisely why
            // reading it would have been wrong.
            Assert.Equal([factory.InScopeCommune], CommuneClaimsOf(token));
        }
        finally
        {
            await factory.RemoveAllCommunesAsync("agency");
            await factory.AssignCommuneAsync("agency", factory.InScopeCommune);
        }
    }

    [Fact]
    public async Task An_administrator_reports_the_system_wide_scope()
    {
        var client = await AuthenticatedAsync("admin", "SEED_ADMIN_PASSWORD");

        Assert.Equal(["*"], await CommunesAsync(client));
    }

    /// <summary>
    /// An account with no commune yet answers 200 with an EMPTY list, never 403.
    /// </summary>
    /// <remarks>
    /// That is the state every self-registered account starts in, and the front end has to be able to
    /// read it to show "waiting for an administrator". Refusing the call would leave it guessing.
    /// </remarks>
    [Fact]
    public async Task An_account_with_no_commune_still_gets_its_profile_with_an_empty_list()
    {
        await factory.RemoveAllCommunesAsync("crew");

        try
        {
            var client = await AuthenticatedAsync("crew", "SEED_CREW_PASSWORD");
            var response = await client.GetAsync(Route);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Empty(await CommunesAsync(client));
        }
        finally
        {
            await factory.AssignCommuneAsync("crew", factory.InScopeCommune);
        }
    }

    [Fact]
    public async Task Without_a_token_it_is_401()
    {
        var response = await factory.CreateClient().GetAsync(Route);
        var body = await response.Content.ReadAsStringAsync();

        output.WriteLine($"  no token → HTTP {(int)response.StatusCode}");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains(ErrorCodes.Unauthenticated, body);
    }

    /// <summary>
    /// The endpoint carries NO <see cref="IAllowAnonymous"/> metadata.
    /// </summary>
    /// <remarks>
    /// ⚠️ A behavioural test cannot say this. <c>AuthController</c> used to carry
    /// <c>[AllowAnonymous]</c> on the CLASS, which BEATS an <c>[Authorize]</c> on a method (the
    /// compiler reports ASP0026) — and with it back in place the anonymous call STILL answers 401,
    /// because the action runs and finds no subject claim. Same status, entirely different reason:
    /// the endpoint is reachable, and the next endpoint added to this controller would inherit the
    /// opt-out silently. Checked by SABOTAGE: restoring the class attribute leaves
    /// <see cref="Without_a_token_it_is_401"/> green and turns THIS red.
    /// </remarks>
    [Fact]
    public void It_is_not_declared_anonymous_even_though_its_four_siblings_are()
    {
        var endpoints = factory.Services
            .GetRequiredService<Microsoft.AspNetCore.Routing.EndpointDataSource>().Endpoints;

        var me = endpoints.OfType<Microsoft.AspNetCore.Routing.RouteEndpoint>().Single(endpoint =>
            endpoint.RoutePattern.RawText!.EndsWith("/auth/me", StringComparison.Ordinal));

        Assert.Null(me.Metadata.GetMetadata<IAllowAnonymous>());

        // The other half: the four token-issuing endpoints DO opt out, so this is not asserting that
        // nothing in the controller is anonymous.
        var login = endpoints.OfType<Microsoft.AspNetCore.Routing.RouteEndpoint>().Single(endpoint =>
            endpoint.RoutePattern.RawText!.EndsWith("/auth/login", StringComparison.Ordinal));

        Assert.NotNull(login.Metadata.GetMetadata<IAllowAnonymous>());
    }

    /// <summary>
    /// A still-valid token whose account has been deleted is 401, not 500 and not 404.
    /// </summary>
    /// <remarks>
    /// The credential has stopped identifying anyone, which is an authentication failure rather than a
    /// missing resource — and "me" is not a resource the caller could ask about by id anyway.
    /// </remarks>
    [Fact]
    public async Task A_token_whose_account_no_longer_exists_is_401()
    {
        var (username, password) = await NewThrowawayUserAsync();
        var client = await AuthenticatedAsync(username, password);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(Route)).StatusCode);

        await DeleteUserAsync(username);

        var response = await client.GetAsync(Route);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains(ErrorCodes.Unauthenticated, body);
    }

    // ── helpers ────────────────────────────────────────────────────────────────────────────

    private async Task<HttpClient> AuthenticatedAsync(string username, string passwordVariable)
    {
        var client = factory.CreateClient();
        var tokens = passwordVariable.StartsWith("SEED_", StringComparison.Ordinal)
            ? await client.LoginAsync(username, passwordVariable)
            : await (await client.PostLoginAsync(username, passwordVariable)).ReadTokensAsync();

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        return client;
    }

    private static async Task<string[]> CommunesAsync(HttpClient client)
    {
        var me = JsonDocument.Parse(await client.GetStringAsync(Route)).RootElement;
        return [.. me.GetProperty("commune_ids").EnumerateArray().Select(value => value.GetString()!)];
    }

    /// <summary>Reads <c>commune_ids</c> out of the JWT payload, with no key and no validation.</summary>
    /// <remarks>Exactly what a front end would do if it decoded the token instead of calling this endpoint.</remarks>
    private static string[] CommuneClaimsOf(string accessToken)
    {
        var payload = accessToken.Split('.')[1];
        var padded = payload.Replace('-', '+').Replace('_', '/').PadRight((payload.Length + 3) / 4 * 4, '=');
        var claims = JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(padded))).RootElement;

        var communes = claims.GetProperty("commune_ids");
        return communes.ValueKind == JsonValueKind.Array
            ? [.. communes.EnumerateArray().Select(value => value.GetString()!)]
            : [communes.GetString()!];
    }

    /// <summary>An account created just to be deleted while its token is still valid.</summary>
    private async Task<(string Username, string Password)> NewThrowawayUserAsync()
    {
        var username = $"gone-{Guid.NewGuid():N}"[..20];
        const string password = "a-throwaway-password-long-enough";

        var response = await factory.CreateClient().PostAsJsonAsync("/api/v1/auth/register", new
        {
            username,
            email = $"{username}@luxmap.local",
            full_name = "Account about to vanish",
            password,
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (username, password);
    }

    private Task DeleteUserAsync(string username)
        => factory.QueryAsync(async db =>
        {
            #pragma warning disable RS0030 // Test TEARDOWN: bulk delete is the only way to clean up under an empty scope. BE-36 removes the need entirely — a fresh database per run.
            await db.Set<RefreshToken>().Where(token => token.User.Username == username).ExecuteDeleteAsync();
            return await db.Set<AppUser>().Where(user => user.Username == username).ExecuteDeleteAsync();
            #pragma warning restore RS0030
        });
}
