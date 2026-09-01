using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LuxMap.Shared.Contracts.Errors;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Xunit.Abstractions;

namespace LuxMap.Api.Tests;

/// <summary>Group 1 — authentication.</summary>
[Collection(nameof(ScopeCollection))]
public class AuthenticationTests(ScopeTestFixture factory, ITestOutputHelper output)
{
    private HttpClient Client => factory.CreateClient();

    private async Task<HttpClient> AuthenticatedAsync(string username, string passwordVariable)
    {
        var client = factory.CreateClient();
        var tokens = await client.LoginAsync(username, passwordVariable);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        return client;
    }

    private static async Task AssertContractErrorAsync(HttpResponseMessage response, string expectedCode)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.False(string.IsNullOrWhiteSpace(body), "401/403 must NOT return an empty body");

        var root = JsonDocument.Parse(body).RootElement;
        Assert.Equal(["error"], root.EnumerateObject().Select(p => p.Name));
        var error = root.GetProperty("error");
        Assert.Equal(["code", "message", "details"], error.EnumerateObject().Select(p => p.Name));
        Assert.Equal(expectedCode, error.GetProperty("code").GetString());
    }

    private static string ForgeToken(string issuer, string audience, string key, DateTime expires)
    {
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            Expires = expires,
            NotBefore = expires.AddHours(-2),
            IssuedAt = expires.AddHours(-2),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)), SecurityAlgorithms.HmacSha256),
            Claims = new Dictionary<string, object>
            {
                ["sub"] = "USR-003",
                ["role"] = "maintenance_engineer",
                ["commune_ids"] = new[] { "COM-001" },
            },
        };

        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    private static string RealKey => AuthTestExtensions.SeedPassword("JWT_SIGNING_KEY");

    [Fact]
    public async Task Token_issued_by_BE07_is_accepted()
    {
        var client = await AuthenticatedAsync("engineer", "SEED_ENGINEER_PASSWORD");
        var response = await client.GetAsync("/api/v1/_scope/whoami");

        output.WriteLine($"  token issued by BE-07 → HTTP {(int)response.StatusCode}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Claims_are_readable_under_their_original_names()
    {
        var client = await AuthenticatedAsync("engineer", "SEED_ENGINEER_PASSWORD");
        var root = JsonDocument.Parse(await client.GetStringAsync("/api/v1/_scope/whoami")).RootElement;

        output.WriteLine($"  read from ClaimsPrincipal: {root.GetRawText()}");

        // With MapInboundClaims still on, "sub" would be null here.
        Assert.Equal("USR-003", root.GetProperty("sub").GetString());
        Assert.Equal("maintenance_engineer", root.GetProperty("role").GetString());
        Assert.NotEmpty(root.GetProperty("commune_ids").EnumerateArray());
    }

    [Fact]
    public async Task Multi_valued_commune_ids_are_all_readable()
    {
        await factory.AssignCommuneAsync("agency", factory.SecondCommune);
        try
        {
            var client = await AuthenticatedAsync("agency", "SEED_AGENCY_PASSWORD");
            var root = JsonDocument.Parse(await client.GetStringAsync("/api/v1/_scope/whoami")).RootElement;

            var communes = root.GetProperty("commune_ids").EnumerateArray().Select(v => v.GetString()).ToArray();
            output.WriteLine($"  commune_ids read back: [{string.Join(", ", communes)}]");

            // FindAll, not FindFirst — FindFirst would only see the first element.
            Assert.Equal(2, communes.Length);
            Assert.Contains(ScopeTestFixture.InScopeCommune, communes);
            Assert.Contains(factory.SecondCommune, communes);
        }
        finally
        {
            await factory.RemoveAllCommunesAsync("agency");
            await factory.AssignCommuneAsync("agency", ScopeTestFixture.InScopeCommune);
        }
    }

    [Fact]
    public async Task No_token_returns_401_in_contract_error_shape_not_an_empty_body()
    {
        var response = await Client.GetAsync("/api/v1/_scope/probes");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        await AssertContractErrorAsync(response, ErrorCodes.Unauthenticated);
        output.WriteLine($"  no token → {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
    }

    [Theory]
    [InlineData("expired")]
    [InlineData("wrong signing key")]
    [InlineData("wrong issuer")]
    [InlineData("wrong audience")]
    public async Task Invalid_tokens_all_return_the_same_401(string reason)
    {
        var token = reason switch
        {
            "expired" => ForgeToken("luxmap-api", "luxmap-clients", RealKey, DateTime.UtcNow.AddMinutes(-10)),
            "wrong signing key" => ForgeToken("luxmap-api", "luxmap-clients",
                "a-completely-different-key-that-is-at-least-32-bytes", DateTime.UtcNow.AddHours(1)),
            "wrong issuer" => ForgeToken("an-impostor", "luxmap-clients", RealKey, DateTime.UtcNow.AddHours(1)),
            _ => ForgeToken("luxmap-api", "a-different-audience", RealKey, DateTime.UtcNow.AddHours(1)),
        };

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/api/v1/_scope/probes");
        output.WriteLine($"  {reason,-14} → HTTP {(int)response.StatusCode}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        await AssertContractErrorAsync(response, ErrorCodes.Unauthenticated);
    }

    [Fact]
    public async Task Wrong_role_returns_403_in_contract_error_shape()
    {
        var client = await AuthenticatedAsync("crew", "SEED_CREW_PASSWORD");
        var response = await client.GetAsync("/api/v1/_scope/engineer-only");

        output.WriteLine($"  field_crew calling an engineer-only endpoint → HTTP {(int)response.StatusCode}");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await AssertContractErrorAsync(response, ErrorCodes.CommuneForbidden);
    }

    [Fact]
    public async Task Correct_role_passes_the_policy()
    {
        var client = await AuthenticatedAsync("engineer", "SEED_ENGINEER_PASSWORD");
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/_scope/engineer-only")).StatusCode);
    }

    [Fact]
    public async Task Auth_endpoints_stay_reachable_without_a_token()
    {
        // Signing in must work while unauthenticated — otherwise nobody can ever get in.
        var login = await Client.PostLoginAsync("engineer", AuthTestExtensions.SeedPassword("SEED_ENGINEER_PASSWORD"));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        var anonymous = await Client.GetAsync("/api/v1/_scope/open");
        Assert.Equal(HttpStatusCode.OK, anonymous.StatusCode);

        output.WriteLine("  /auth/login and the [AllowAnonymous] endpoint are both reachable without a token");
    }
}
