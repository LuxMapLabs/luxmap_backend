using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Net.Http.Headers;

namespace LuxMap.Api.Tests;

/// <summary>
/// Drives the web group (Contract section 2.10.2) the way a browser on the allowed origin would:
/// https, an <c>Origin</c> header, and the cookie sent back by hand.
/// </summary>
public static class WebAuthTestExtensions
{
    public const string CookieName = "__Secure-luxmap_rt";
    public const string CookiePath = "/api/v1/auth/web";

    /// <summary>
    /// https because the cookie is <c>Secure</c>; cookie handling OFF so every test states exactly
    /// which cookie it sends instead of inheriting one from an earlier request.
    /// </summary>
    public static HttpClient CreateWebClient(this AuthTestFactory factory)
        => factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = false,
            AllowAutoRedirect = false,
        });

    /// <param name="origin"><c>null</c> sends no <c>Origin</c> header at all.</param>
    public static Task<HttpResponseMessage> PostWebAsync(
        this HttpClient client,
        string endpoint,
        object? body = null,
        string? cookie = null,
        string? origin = TestHostSettings.WebOrigin)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/auth/web/{endpoint}");

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        if (origin is not null)
        {
            request.Headers.Add("Origin", origin);
        }

        if (cookie is not null)
        {
            request.Headers.Add("Cookie", $"{CookieName}={cookie}");
        }

        return client.SendAsync(request);
    }

    /// <param name="rememberMe"><c>null</c> leaves <c>remember_me</c> out of the body entirely.</param>
    public static Task<HttpResponseMessage> PostWebLoginAsync(
        this HttpClient client, string user, string password, bool? rememberMe, string? cookie = null)
    {
        object body = rememberMe is null
            ? new { username = user, password }
            : new { username = user, password, remember_me = rememberMe.Value };

        return client.PostWebAsync("login", body, cookie);
    }

    /// <summary>The auth cookie among the response's <c>Set-Cookie</c> headers, or <c>null</c> if none.</summary>
    public static SetCookieHeaderValue? AuthCookie(this HttpResponseMessage response)
        => response.Headers.TryGetValues(HeaderNames.SetCookie, out var values)
            ? SetCookieHeaderValue.ParseList(values.ToList())
                .SingleOrDefault(cookie => cookie.Name.Equals(CookieName, StringComparison.Ordinal))
            : null;

    public static async Task<string[]> BodyKeysAsync(this HttpResponseMessage response)
        => JsonDocument.Parse(await response.Content.ReadAsStringAsync())
            .RootElement.EnumerateObject().Select(property => property.Name).ToArray();

    public static async Task<string> ErrorCodeAsync(this HttpResponseMessage response)
        => JsonDocument.Parse(await response.Content.ReadAsStringAsync())
            .RootElement.GetProperty("error").GetProperty("code").GetString()!;
}
