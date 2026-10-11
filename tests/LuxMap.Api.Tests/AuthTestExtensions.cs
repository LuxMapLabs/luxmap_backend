using System.Net.Http.Json;
using System.Text.Json;

namespace LuxMap.Api.Tests;

public sealed record TokenPair(string AccessToken, string RefreshToken, int ExpiresIn);

public static class AuthTestExtensions
{
    /// <summary>
    /// Seed passwords come from .env exactly as BE-06 reads them — never hardcoded in tests. Read from the
    /// process environment, the same source <c>SeedCredentials</c> seeds from, so the two cannot disagree.
    /// </summary>
    public static string SeedPassword(string variable)
    {
        TestDatabase.LoadEnvironment();
        var value = Environment.GetEnvironmentVariable(variable);
        return string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException($"{variable} is missing from .env")
            : value;
    }

    public static async Task<HttpResponseMessage> PostLoginAsync(this HttpClient client, string user, string password)
        => await client.PostAsJsonAsync("/api/v1/auth/login", new { username = user, password });

    public static async Task<HttpResponseMessage> PostRefreshAsync(this HttpClient client, string refreshToken)
        => await client.PostAsJsonAsync("/api/v1/auth/refresh", new { refresh_token = refreshToken });

    public static async Task<HttpResponseMessage> PostLogoutAsync(this HttpClient client, string refreshToken)
        => await client.PostAsJsonAsync("/api/v1/auth/logout", new { refresh_token = refreshToken });

    public static async Task<TokenPair> ReadTokensAsync(this HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        var root = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        return new TokenPair(
            root.GetProperty("access_token").GetString()!,
            root.GetProperty("refresh_token").GetString()!,
            root.GetProperty("expires_in").GetInt32());
    }

    public static async Task<TokenPair> LoginAsync(this HttpClient client, string user, string variable)
        => await (await client.PostLoginAsync(user, SeedPassword(variable))).ReadTokensAsync();
}
