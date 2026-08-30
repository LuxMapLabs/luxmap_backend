using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LuxMap.Modules.Identity.Entities;
using LuxMap.Persistence;
using LuxMap.Shared.Contracts.Errors;
using Microsoft.EntityFrameworkCore;
using Xunit.Abstractions;

namespace LuxMap.Api.Tests;

[Collection(nameof(AuthCollection))]
public class AuthEndpointTests(AuthTestFactory factory, ITestOutputHelper output)
{
    private HttpClient Client => factory.CreateClient();

    private static JsonElement DecodePayload(string jwt)
    {
        var part = jwt.Split('.')[1];
        part = part.PadRight(part.Length + ((4 - (part.Length % 4)) % 4), '=');
        return JsonDocument.Parse(Convert.FromBase64String(part.Replace('-', '+').Replace('_', '/'))).RootElement;
    }

    [Fact]
    public async Task Login_issues_a_token_whose_claims_match_the_locked_names()
    {
        var tokens = await Client.LoginAsync("engineer", "SEED_ENGINEER_PASSWORD");
        var payload = DecodePayload(tokens.AccessToken);

        output.WriteLine("── decoded payload (the raw token is NEVER printed) ──");
        output.WriteLine(JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));

        Assert.Equal("USR-003", payload.GetProperty("sub").GetString());
        Assert.Equal("maintenance_engineer", payload.GetProperty("role").GetString());
        Assert.Equal(JsonValueKind.String, payload.GetProperty("role").ValueKind);

        var communes = payload.GetProperty("commune_ids");
        Assert.Equal(JsonValueKind.Array, communes.ValueKind);
        Assert.Equal(["COM-001"], communes.EnumerateArray().Select(v => v.GetString()));

        Assert.Equal("luxmap-api", payload.GetProperty("iss").GetString());
        Assert.Equal("luxmap-clients", payload.GetProperty("aud").GetString());
        Assert.Equal(3600, tokens.ExpiresIn);
    }

    [Fact]
    public async Task Administrator_gets_a_single_element_wildcard_array_not_a_bare_string()
    {
        var tokens = await Client.LoginAsync("admin", "SEED_ADMIN_PASSWORD");
        var communes = DecodePayload(tokens.AccessToken).GetProperty("commune_ids");

        output.WriteLine($"  administrator commune_ids = {communes.GetRawText()}");

        Assert.Equal(JsonValueKind.Array, communes.ValueKind);
        Assert.Equal(["*"], communes.EnumerateArray().Select(v => v.GetString()));
    }

    [Fact]
    public async Task Response_has_exactly_the_four_locked_fields()
    {
        var response = await Client.PostLoginAsync("engineer", AuthTestExtensions.SeedPassword("SEED_ENGINEER_PASSWORD"));
        var root = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

        Assert.Equal(
            ["access_token", "refresh_token", "token_type", "expires_in"],
            root.EnumerateObject().Select(p => p.Name));
        Assert.Equal("Bearer", root.GetProperty("token_type").GetString());
    }

    [Fact]
    public async Task Unknown_user_and_wrong_password_produce_byte_identical_bodies()
    {
        var unknown = await Client.PostLoginAsync("khong_ton_tai_dau", "mat-khau-bat-ky");
        var wrongPassword = await Client.PostLoginAsync("engineer", "mat-khau-sai-hoan-toan");

        Assert.Equal(HttpStatusCode.Unauthorized, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);

        static async Task<string> StripCorrelationAsync(HttpResponseMessage response)
        {
            var node = System.Text.Json.Nodes.JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
            node["error"]!["details"]!.AsObject().Remove("correlation_id");
            return node.ToJsonString();
        }

        var a = await StripCorrelationAsync(unknown);
        var b = await StripCorrelationAsync(wrongPassword);

        output.WriteLine($"  unknown user : {a}");
        output.WriteLine($"  wrong password: {b}");

        Assert.Equal(a, b);
        Assert.Contains(ErrorCodes.InvalidCredentials, a);

        // details must never point at a specific field — that is the leak. Inspect the details keys
        // themselves rather than scanning the whole body: the shared message legitimately reads
        // "Incorrect username or password", so a substring scan would measure the wrong thing.
        var details = JsonDocument.Parse(a).RootElement.GetProperty("error").GetProperty("details");
        Assert.Empty(details.EnumerateObject());
    }

    [Fact]
    public async Task Locked_account_is_blocked_on_both_login_and_refresh()
    {
        var tokens = await Client.LoginAsync("crew", "SEED_CREW_PASSWORD");
        await SetLockedAsync("crew", locked: true);
        try
        {
            var login = await Client.PostLoginAsync("crew", AuthTestExtensions.SeedPassword("SEED_CREW_PASSWORD"));
            var refresh = await Client.PostRefreshAsync(tokens.RefreshToken);

            output.WriteLine($"  login while locked   : HTTP {(int)login.StatusCode}");
            output.WriteLine($"  refresh while locked : HTTP {(int)refresh.StatusCode}");

            Assert.Equal(HttpStatusCode.Forbidden, login.StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, refresh.StatusCode);
            Assert.Contains(ErrorCodes.AccountLocked, await login.Content.ReadAsStringAsync());

            // Logout must still be allowed.
            Assert.Equal(HttpStatusCode.NoContent, (await Client.PostLogoutAsync(tokens.RefreshToken)).StatusCode);
        }
        finally
        {
            await SetLockedAsync("crew", locked: false);
        }
    }

    [Fact]
    public async Task Refresh_succeeds_even_while_the_access_token_is_still_valid()
    {
        var tokens = await Client.LoginAsync("agency", "SEED_AGENCY_PASSWORD");
        var payload = DecodePayload(tokens.AccessToken);
        var expiresAt = DateTimeOffset.FromUnixTimeSeconds(payload.GetProperty("exp").GetInt64());

        Assert.True(expiresAt > factory.Clock.GetUtcNow(), "the access token must still be valid");

        var response = await Client.PostRefreshAsync(tokens.RefreshToken);
        output.WriteLine($"  refresh while the access token is still valid: HTTP {(int)response.StatusCode}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Refresh_does_not_read_the_authorization_header()
    {
        var tokens = await Client.LoginAsync("agency", "SEED_AGENCY_PASSWORD");

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/refresh")
        {
            Content = JsonContent.Create(new { refresh_token = tokens.RefreshToken }),
        };
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer complete-garbage-not-a-token");

        var response = await Client.SendAsync(request);
        output.WriteLine($"  refresh with a junk Authorization header: HTTP {(int)response.StatusCode}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Logout_is_idempotent()
    {
        var tokens = await Client.LoginAsync("agency", "SEED_AGENCY_PASSWORD");

        var first = await Client.PostLogoutAsync(tokens.RefreshToken);
        var second = await Client.PostLogoutAsync(tokens.RefreshToken);
        var neverExisted = await Client.PostLogoutAsync("token-chua-bao-gio-ton-tai");

        output.WriteLine($"  logout #1: {(int)first.StatusCode} | #2: {(int)second.StatusCode} | unknown token: {(int)neverExisted.StatusCode}");

        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, second.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, neverExisted.StatusCode);
    }

    [Theory]
    [InlineData("""{"username":"engineer"}""")]
    [InlineData("""{"password":"chi-co-mat-khau"}""")]
    [InlineData("""{"username":"","password":""}""")]
    public async Task Malformed_login_body_goes_through_the_BE04_validation_pipeline(string body)
    {
        var response = await Client.PostAsync(
            "/api/v1/auth/login",
            new StringContent(body, System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var root = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(["error"], root.EnumerateObject().Select(p => p.Name));
        Assert.Equal(ErrorCodes.ValidationFailed, root.GetProperty("error").GetProperty("code").GetString());
    }

    [Theory]
    [InlineData("token-hoan-toan-khong-ton-tai")]
    [InlineData("")]
    public async Task Bad_refresh_tokens_all_return_the_same_401_shape(string token)
    {
        var response = await Client.PostRefreshAsync(token);

        // An empty string is stopped by validation first; an unknown token gives 401. Both use the error shape.
        Assert.Contains(response.StatusCode, new[] { HttpStatusCode.Unauthorized, HttpStatusCode.BadRequest });

        var root = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(["error"], root.EnumerateObject().Select(p => p.Name));
    }

    private Task SetLockedAsync(string username, bool locked)
        => factory.QueryAsync(async db =>
        {
            await db.Set<AppUser>()
                .Where(u => u.Username == username)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.IsLocked, locked));
            return 0;
        });
}
