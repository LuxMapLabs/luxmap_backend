using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace LuxMap.Api.Tests;

/// <summary>
/// Credentialed CORS (Contract section 2.10.2) and the startup check on its allowlist.
/// </summary>
public class CorsTests(LuxMapApiFactory factory) : IClassFixture<LuxMapApiFactory>
{
    private const string WebRefreshPath = "/api/v1/auth/web/refresh";

    private static HttpRequestMessage Preflight(string origin)
    {
        var request = new HttpRequestMessage(HttpMethod.Options, WebRefreshPath);
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", "POST");
        request.Headers.Add("Access-Control-Request-Headers", "content-type");
        return request;
    }

    /// <summary>T-CORS-1.</summary>
    [Fact]
    public async Task Preflight_from_an_allowed_origin_echoes_that_origin_and_allows_credentials()
    {
        var response = await factory.CreateClient().SendAsync(Preflight(TestHostSettings.WebOrigin));

        Assert.Equal([TestHostSettings.WebOrigin], response.Headers.GetValues("Access-Control-Allow-Origin"));
        Assert.Equal(["true"], response.Headers.GetValues("Access-Control-Allow-Credentials"));
    }

    /// <summary>T-CORS-1, the other half.</summary>
    [Theory]
    [InlineData("https://evil.example")]
    [InlineData("http://localhost:3000")]
    [InlineData("null")]
    public async Task Preflight_from_any_other_origin_gets_no_allow_origin_header(string origin)
    {
        var response = await factory.CreateClient().SendAsync(Preflight(origin));

        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
        Assert.False(response.Headers.Contains("Access-Control-Allow-Credentials"));
    }

    [Fact]
    public async Task The_correlation_id_header_is_exposed_to_the_browser()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login");
        request.Headers.Add("Origin", TestHostSettings.WebOrigin);

        var response = await factory.CreateClient().SendAsync(request);

        Assert.Contains("X-Correlation-Id", response.Headers.GetValues("Access-Control-Expose-Headers"));
    }

    /// <summary>T-OPTIONS for <c>Cors:AllowedOrigins</c>. <c>null</c> means nothing is configured.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("*")]
    [InlineData("https://*.example.vn")]
    [InlineData("http://localhost:3000")]
    [InlineData("https://localhost:3000/")]
    [InlineData("https://localhost:3000/app")]
    [InlineData("https://example.vn:443")]
    public void Startup_stops_on_a_missing_or_malformed_allowlist(string? origin)
    {
        var settings = origin is null
            ? new Dictionary<string, string>()
            : new Dictionary<string, string> { ["Cors:AllowedOrigins:0"] = origin };

        var message = StartupFailure(settings);

        Assert.Contains("Cors:AllowedOrigins", message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Builds a host with exactly <paramref name="settings"/> and returns the startup failure's
    /// message chain. Fails the test if the host starts.
    /// </summary>
    internal static string StartupFailure(IReadOnlyDictionary<string, string> settings)
    {
        using var host = new SettingsFactory(settings);

        var exception = Record.Exception(() => host.CreateClient());

        Assert.NotNull(exception);
        var messages = new List<string>();
        for (var current = exception; current is not null; current = current.InnerException)
        {
            messages.Add(current.Message);
        }

        return string.Join(" | ", messages);
    }

    private sealed class SettingsFactory(IReadOnlyDictionary<string, string> settings) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");
            foreach (var (key, value) in settings)
            {
                builder.UseSetting(key, value);
            }
        }
    }
}
