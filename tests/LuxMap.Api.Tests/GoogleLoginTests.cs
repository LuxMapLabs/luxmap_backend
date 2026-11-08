using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LuxMap.Modules.Identity.Auth;
using LuxMap.Modules.Identity.Auth.Web;
using LuxMap.Modules.Identity.Entities;
using LuxMap.Modules.Identity.Seeding;
using LuxMap.Persistence;
using LuxMap.Shared.Contracts.Enums;
using LuxMap.Shared.Http;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.JsonWebTokens;

namespace LuxMap.Api.Tests;

// The no-registration assertion counts the whole table, so no other collection may write during it.
[CollectionDefinition(nameof(GoogleLoginCollection), DisableParallelization = true)]
public sealed class GoogleLoginCollection : ICollectionFixture<GoogleLoginFactory>;

public sealed class RecordingGoogleVerifier : IGoogleIdTokenVerifier
{
    private readonly ConcurrentDictionary<string, GoogleIdentity> identities = new();
    private int calls;
    public int Calls => Volatile.Read(ref calls);

    public string TokenFor(string email, bool verified = true)
    {
        var token = $"fake-google-{Guid.NewGuid():N}";
        identities[token] = new GoogleIdentity("fake-subject", email, verified);
        return token;
    }

    public Task<GoogleIdentity?> VerifyAsync(string idToken, CancellationToken ct)
    {
        Interlocked.Increment(ref calls);
        return Task.FromResult(identities.GetValueOrDefault(idToken));
    }
}

public sealed class GoogleLoginFactory : LuxMapTestHost
{
    public RecordingGoogleVerifier Verifier { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Production");
        builder.UseTestCorsOrigin();
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IGoogleIdTokenVerifier>();
            services.AddSingleton<IGoogleIdTokenVerifier>(Verifier);
            services.RemoveAll<GoogleLoginOptions>();
            services.AddSingleton(new GoogleLoginOptions { ClientIds = ["test-client"] });
        });
    }

    public async Task<T> QueryAsync<T>(Func<LuxMapDbContext, Task<T>> query)
    {
        await using var scope = Services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<LuxMapDbContext>());
    }
}

[Collection(nameof(GoogleLoginCollection))]
public sealed class GoogleLoginTests(GoogleLoginFactory factory) : IAsyncLifetime
{
    private const string Prefix = "google-login-";
    private const string Password = "Google-login-password-42!";

    public Task InitializeAsync() => Task.CompletedTask;
    public Task DisposeAsync() => factory.QueryAsync(async db =>
    {
#pragma warning disable RS0030 // Test teardown: only this class's accounts; sessions/links/assignments cascade.
        return await db.Set<AppUser>().Where(user => user.Username.StartsWith(Prefix)).ExecuteDeleteAsync();
#pragma warning restore RS0030
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Verified_email_in_any_casing_signs_in_to_the_existing_account(bool web)
    {
        var user = await CreateAsync();
        foreach (var email in new[] { user.Email, user.Email.ToLowerInvariant(), user.Email.ToUpperInvariant() })
        {
            using var response = await GoogleAsync(web, factory.Verifier.TokenFor(email));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await BodyAsync(response);
            Assert.Equal(user.UserId, new JsonWebToken(body.GetProperty("access_token").GetString()!).Subject);
            Assert.Equal("Bearer", body.GetProperty("token_type").GetString());
            var cookie = response.AuthCookie();
            if (web)
            {
                Assert.False(body.TryGetProperty("refresh_token", out _));
                Assert.NotNull(cookie);
                Assert.True(cookie.HttpOnly);
                Assert.True(cookie.Secure);
                Assert.Equal("Lax", cookie.SameSite.ToString());
                Assert.Equal(WebAuthTestExtensions.CookiePath, cookie.Path.ToString());
                Assert.Null(cookie.Expires);
                Assert.Equal(RefreshTokenSessionKind.WebSession, (await FindTokenAsync(cookie.Value.ToString()))!.SessionKind);
            }
            else
            {
                Assert.Null(cookie);
                var raw = body.GetProperty("refresh_token").GetString()!;
                Assert.False(string.IsNullOrWhiteSpace(raw));
                Assert.Equal(RefreshTokenSessionKind.Mobile, (await FindTokenAsync(raw))!.SessionKind);
            }
        }
        var stored = await factory.QueryAsync(db => db.Set<AppUser>().AsNoTracking().SingleAsync(u => u.UserId == user.UserId));
        Assert.Equal(user.Email, stored.Email);
        Assert.Equal(user.FullName, stored.FullName);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Invalid_identity_unmatched_email_and_invitation_share_password_failure_and_never_register(bool web)
    {
        var active = await CreateAsync();
        var invited = await CreateAsync(invited: true);
        using var client = Client();
        using var wrongPassword = web
            ? await client.PostWebLoginAsync(active.Username, "wrong-password", false)
            : await client.PostAsJsonAsync("/api/v1/auth/login", new { username = active.Username, password = "wrong-password" });
        var expected = (await BodyAsync(wrongPassword)).GetProperty("error");
        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
        var before = await factory.QueryAsync(db => db.Set<AppUser>().CountAsync());
        string[] tokens =
        [
            factory.Verifier.TokenFor(active.Email, verified: false),
            "fake-invalid-token",
            factory.Verifier.TokenFor($"{Prefix}{Guid.NewGuid():N}@unknown.test"),
            factory.Verifier.TokenFor(invited.Email),
        ];
        foreach (var token in tokens)
        {
            using var response = await GoogleAsync(web, token);
            await AssertInvalidAsync(response);
            var error = (await BodyAsync(response)).GetProperty("error");
            Assert.Equal(expected.GetProperty("code").GetString(), error.GetProperty("code").GetString());
            Assert.Equal(expected.GetProperty("message").GetString(), error.GetProperty("message").GetString());
            Assert.Null(response.AuthCookie());
            Assert.Equal(before, await factory.QueryAsync(db => db.Set<AppUser>().CountAsync()));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_locked_account_with_a_verified_identity_is_forbidden(bool web)
    {
        var user = await CreateAsync(locked: true);
        using var response = await GoogleAsync(web, factory.Verifier.TokenFor(user.Email));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var error = (await BodyAsync(response)).GetProperty("error");
        Assert.Equal("ACCOUNT_LOCKED", error.GetProperty("code").GetString());
        Assert.Equal("This account is locked. Contact an administrator.", error.GetProperty("message").GetString());
        Assert.Null(response.AuthCookie());
        Assert.Equal(0, await factory.QueryAsync(db => db.Set<RefreshToken>().CountAsync(t => t.UserId == user.UserId)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Missing_empty_whitespace_and_oversized_tokens_are_bad_requests_without_verification(bool web)
    {
        var before = factory.Verifier.Calls;
        object[] bodies = [new { }, new { id_token = "" }, new { id_token = "   " }, new { id_token = new string('x', 8193) }];
        using var client = Client();
        foreach (var body in bodies)
        {
            using var response = web
                ? await client.PostWebAsync("google", body)
                : await client.PostAsJsonAsync("/api/v1/auth/google", body);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("VALIDATION_FAILED", (await BodyAsync(response)).GetProperty("error").GetProperty("code").GetString());
        }
        Assert.Equal(before, factory.Verifier.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Empty_client_ids_disable_sign_in_without_calling_the_verifier(bool web)
    {
        var user = await CreateAsync();
        var token = factory.Verifier.TokenFor(user.Email);
        using var disabled = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<GoogleLoginOptions>();
            services.AddSingleton(new GoogleLoginOptions());
        }));
        using var client = Client(disabled);
        var before = factory.Verifier.Calls;
        using var response = web
            ? await client.PostWebAsync("google", new { id_token = token })
            : await client.PostAsJsonAsync("/api/v1/auth/google", new { id_token = token });
        await AssertInvalidAsync(response);
        Assert.Equal(before, factory.Verifier.Calls);
        Assert.Null(response.AuthCookie());
    }

    [Fact]
    public async Task Google_web_sign_in_revokes_the_old_cookie_and_opens_a_new_chain()
    {
        var user = await CreateAsync();
        using var client = Client();
        using var oldResponse = await client.PostWebLoginAsync(user.Username, Password, true);
        Assert.Equal(HttpStatusCode.OK, oldResponse.StatusCode);
        var oldRaw = oldResponse.AuthCookie()!.Value.ToString();
        var token = factory.Verifier.TokenFor(user.Email);
        using var response = await client.PostWebAsync("google", new { id_token = token }, cookie: oldRaw);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var old = await FindTokenAsync(oldRaw);
        var fresh = await FindTokenAsync(response.AuthCookie()!.Value.ToString());
        Assert.NotNull(old!.RevokedAt);
        Assert.Equal(RefreshTokenRevocationReason.Logout, old.RevokedReason);
        Assert.Null(fresh!.RevokedAt);
        Assert.NotEqual(old.ChainId, fresh.ChainId);
        Assert.Equal(RefreshTokenSessionKind.WebSession, fresh.SessionKind);
        using var replay = await client.PostWebAsync("refresh", cookie: oldRaw);
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
    }

    [Fact]
    public async Task Failed_google_web_sign_in_keeps_the_existing_session_alive()
    {
        var user = await CreateAsync();
        using var client = Client();
        using var oldResponse = await client.PostWebLoginAsync(user.Username, Password, false);
        Assert.Equal(HttpStatusCode.OK, oldResponse.StatusCode);
        var oldRaw = oldResponse.AuthCookie()!.Value.ToString();
        using var failed = await client.PostWebAsync("google", new { id_token = "fake-invalid-token" }, cookie: oldRaw);
        await AssertInvalidAsync(failed);
        Assert.Null(failed.AuthCookie());
        Assert.Null((await FindTokenAsync(oldRaw))!.RevokedAt);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Google_actions_opt_out_individually_and_share_the_login_rate_limit(bool web)
    {
        var controller = web ? typeof(WebAuthController) : typeof(AuthController);
        Assert.Empty(controller.GetCustomAttributes(typeof(AllowAnonymousAttribute), false));
        var endpoint = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Single(e => e.RoutePattern.RawText!.EndsWith(web ? "/auth/web/google" : "/auth/google", StringComparison.Ordinal));
        Assert.NotNull(endpoint.Metadata.GetMetadata<IAllowAnonymous>());
        Assert.Equal(web ? ClientSurface.Web : ClientSurface.Mobile,
            endpoint.Metadata.GetMetadata<ClientSurfaceAttribute>()!.Surface);
        Assert.Equal(web, endpoint.Metadata.GetMetadata<RequireAllowedOriginAttribute>() is not null);
        Assert.Equal(LuxMapRateLimits.Login, endpoint.Metadata.GetMetadata<EnableRateLimitingAttribute>()!.PolicyName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("https://untrusted.example.test")]
    public async Task Web_google_requires_an_allowed_origin_before_verification(string? origin)
    {
        using var client = Client();
        var before = factory.Verifier.Calls;
        using var response = await client.PostWebAsync("google", new { id_token = "fake-invalid-token" }, origin: origin);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("ORIGIN_NOT_ALLOWED", (await BodyAsync(response)).GetProperty("error").GetProperty("code").GetString());
        Assert.Equal(before, factory.Verifier.Calls);
        Assert.Null(response.AuthCookie());
    }

    private Task<AppUser> CreateAsync(bool invited = false, bool locked = false) => factory.QueryAsync(async db =>
    {
        var user = new AppUser
        {
            Username = $"{Prefix}{Guid.NewGuid():N}",
            Email = $"Mixed.Case.{Guid.NewGuid():N}@Google-Login.test",
            FullName = "Google login probe",
            Role = UserRole.FieldEngineer,
            IsLocked = locked,
            PasswordAlgorithm = IdentitySeeder.PasswordAlgorithm,
            PasswordSetAt = invited ? null : DateTime.UtcNow,
        };
        if (!invited)
            user.PasswordHash = new PasswordHasher<AppUser>().HashPassword(user, Password);
        db.Set<AppUser>().Add(user);
        await db.SaveChangesAsync();
        return user;
    });

    private Task<RefreshToken?> FindTokenAsync(string raw)
    {
        var hash = RefreshTokenGenerator.Hash(raw);
        return factory.QueryAsync(db => db.Set<RefreshToken>().AsNoTracking().SingleOrDefaultAsync(t => t.TokenHash == hash));
    }

    private static HttpClient Client(WebApplicationFactory<Program> host) => host.CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"), HandleCookies = false, AllowAutoRedirect = false,
    });
    private HttpClient Client() => Client(factory);

    private async Task<HttpResponseMessage> GoogleAsync(bool web, string token)
    {
        using var client = Client();
        return web ? await client.PostWebAsync("google", new { id_token = token })
            : await client.PostAsJsonAsync("/api/v1/auth/google", new { id_token = token });
    }

    private static async Task AssertInvalidAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var error = (await BodyAsync(response)).GetProperty("error");
        Assert.Equal("INVALID_CREDENTIALS", error.GetProperty("code").GetString());
        Assert.Equal("Incorrect username or password.", error.GetProperty("message").GetString());
        Assert.Equal(["correlation_id"], error.GetProperty("details").EnumerateObject().Select(p => p.Name));
    }

    private static async Task<JsonElement> BodyAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }
}

public sealed class GoogleLoginOptionsTests
{
    [Fact]
    public void Csv_maps_to_trimmed_indexed_client_ids()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(
            [new("GOOGLE_CLIENT_IDS", "a, b,,c ")]).Build();
        Assert.Equal(["a", "b", "c"], GoogleLoginOptions.FromConfiguration(config).ClientIds);
    }

    [Fact]
    public void Indexed_settings_are_normalized_and_an_explicit_empty_csv_disables_them()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(
            [new("Google:ClientIds:0", " a "), new("Google:ClientIds:1", "  "), new("Google:ClientIds:2", "b")]).Build();
        Assert.Equal(["a", "b"], GoogleLoginOptions.FromConfiguration(config).ClientIds);
        config["GOOGLE_CLIENT_IDS"] = " , , ";
        Assert.Empty(GoogleLoginOptions.FromConfiguration(config).ClientIds);
        Assert.Empty(GoogleLoginOptions.FromConfiguration(new ConfigurationBuilder().Build()).ClientIds);
    }
}
