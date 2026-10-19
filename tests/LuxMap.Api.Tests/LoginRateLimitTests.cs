using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using LuxMap.Shared.Contracts.Errors;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LuxMap.Api.Tests;

/// <summary>
/// The two login endpoints share ONE limit per client address. Every request counts, right password or wrong: the limiter
/// runs before the action, so a guess past the limit is never checked at all. Each test uses its own addresses, so the
/// shared host's buckets never meet. Unknown usernames keep the seeded accounts untouched.
/// </summary>
public class LoginRateLimitTests(LoginRateLimitTestFactory factory) : IClassFixture<LoginRateLimitTestFactory>
{
    private const string Unknown = "no-such-user-login-limit";

    [Fact]
    public async Task Mobile_and_web_login_share_one_bucket_and_the_next_attempt_is_429_even_with_the_right_password()
    {
        var client = Client();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(MobileLogin("10.1.0.1", Unknown, "wrong-password"))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(WebLogin("10.1.0.1", Unknown, "wrong-password"))).StatusCode);

        var refused = await client.SendAsync(MobileLogin("10.1.0.1", "crew", AuthTestExtensions.SeedPassword("SEED_CREW_PASSWORD")));
        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
        Assert.True(int.Parse(refused.Headers.GetValues("Retry-After").Single(), CultureInfo.InvariantCulture) > 0);
        Assert.Contains(ErrorCodes.RateLimited, await refused.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.SendAsync(WebLogin("10.1.0.1", Unknown, "wrong-password"))).StatusCode);
    }

    [Fact]
    public async Task Another_address_keeps_its_own_bucket()
    {
        var client = Client();

        Assert.Equal(
            [HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized, HttpStatusCode.TooManyRequests],
            await WrongLoginsAsync(client, "10.2.0.1", 3));
        Assert.Equal([HttpStatusCode.Unauthorized], await WrongLoginsAsync(client, "10.2.0.2", 1));
    }

    /// <summary>Refresh is not limited: its credential is a random token, and a field app refreshes far more often than it logs in.</summary>
    [Fact]
    public async Task An_exhausted_address_can_still_refresh()
    {
        var client = Client();
        Assert.Equal(HttpStatusCode.TooManyRequests, (await WrongLoginsAsync(client, "10.3.0.1", 3))[^1]);

        using var refresh = From("10.3.0.1", new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/refresh")
        {
            Content = JsonContent.Create(new { refresh_token = "not-a-token" }),
        });
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(refresh)).StatusCode);
    }

    /// <summary>https and the allowed origin, as the web group requires; the mobile endpoint ignores both.</summary>
    private HttpClient Client()
        => factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = false });

    private static async Task<HttpStatusCode[]> WrongLoginsAsync(HttpClient client, string peer, int count)
    {
        var codes = new List<HttpStatusCode>();
        for (var i = 0; i < count; i++)
        {
            using var request = MobileLogin(peer, Unknown, "wrong-password");
            codes.Add((await client.SendAsync(request)).StatusCode);
        }

        return [.. codes];
    }

    private static HttpRequestMessage MobileLogin(string peer, string user, string password)
        => From(peer, new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login")
        {
            Content = JsonContent.Create(new { username = user, password }),
        });

    private static HttpRequestMessage WebLogin(string peer, string user, string password)
    {
        var request = From(peer, new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/web/login")
        {
            Content = JsonContent.Create(new { username = user, password, remember_me = false }),
        });
        request.Headers.Add("Origin", TestHostSettings.WebOrigin);
        return request;
    }

    private static HttpRequestMessage From(string peer, HttpRequestMessage request)
    {
        request.Headers.Add(TestPeerAddress.Header, peer);
        return request;
    }
}

/// <summary>A login limit of two per address, and client addresses taken from <see cref="TestPeerAddress.Header"/>.</summary>
public sealed class LoginRateLimitTestFactory : LuxMapTestHost
{
    protected override int LoginPermitLimit => 2;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Production");
        builder.UseTestCorsOrigin();
        builder.ConfigureServices(services => services.AddSingleton<IStartupFilter, TestPeerAddress>());
    }
}
