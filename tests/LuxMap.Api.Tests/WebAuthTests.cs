using System.Net;
using LuxMap.Modules.Identity.Entities;
using LuxMap.Shared.Contracts.Errors;
using Microsoft.Net.Http.Headers;
using Xunit.Abstractions;

namespace LuxMap.Api.Tests;

[CollectionDefinition(nameof(WebAuthCollection))]
public sealed class WebAuthCollection : ICollectionFixture<AuthTestFactory>;

/// <summary>
/// The web group of Contract section 2.10. Every assertion on stored state reads the REAL row.
/// </summary>
/// <remarks>
/// Its own collection, so its own host and clock: these tests wind the clock forward by hours and
/// days, which must not leak into the BE-07 tests sharing <see cref="AuthCollection"/>.
/// </remarks>
[Collection(nameof(WebAuthCollection))]
public class WebAuthTests(AuthTestFactory factory, ITestOutputHelper output)
{
    private const string User = "engineer";
    private const string PasswordVariable = "SEED_ENGINEER_PASSWORD";

    private static string Password => AuthTestExtensions.SeedPassword(PasswordVariable);

    private HttpClient Web => factory.CreateWebClient();

    private HttpClient Mobile => factory.CreateClient();

    private DateTime Now => factory.Clock.GetUtcNow().UtcDateTime;

    private async Task<string> WebSignInAsync(bool? rememberMe)
    {
        var response = await Web.PostWebLoginAsync(User, Password, rememberMe);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return response.AuthCookie()!.Value.ToString();
    }

    private Task<RefreshToken?> RowAsync(string rawToken) => factory.FindTokenAsync(rawToken);

    /// <summary>Cookie dates carry whole seconds and the database microseconds; one second is exact enough.</summary>
    private static void AssertSameInstant(DateTime expected, DateTime? actual)
    {
        Assert.NotNull(actual);
        Assert.InRange(Math.Abs((actual.Value - expected).TotalSeconds), 0, 1);
    }

    private void DumpSetCookie(string label, HttpResponseMessage response)
        => output.WriteLine(response.Headers.TryGetValues(HeaderNames.SetCookie, out var values)
            ? $"  {label}: {(int)response.StatusCode} Set-Cookie: {string.Join(" || ", values)}"
            : $"  {label}: {(int)response.StatusCode} (no Set-Cookie)");

    // ── T-CORS-2 (gate) ─────────────────────────────────────────────────────────

    [Fact]
    public async Task A_401_built_by_the_error_handler_still_carries_the_cors_headers()
    {
        var response = await Web.PostWebAsync("refresh");

        output.WriteLine($"  status {(int)response.StatusCode}");
        foreach (var header in response.Headers.Where(h => h.Key.StartsWith("Access-Control", StringComparison.Ordinal)))
        {
            output.WriteLine($"  {header.Key}: {string.Join(", ", header.Value)}");
        }

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(ErrorCodes.InvalidRefreshToken, await response.ErrorCodeAsync());
        Assert.Equal([TestHostSettings.WebOrigin], response.Headers.GetValues("Access-Control-Allow-Origin"));
        Assert.Equal(["true"], response.Headers.GetValues("Access-Control-Allow-Credentials"));
    }

    // ── T-WEB-LOGIN ─────────────────────────────────────────────────────────────

    /// <summary>T-WEB-LOGIN-1 and T-WEB-LOGIN-3's body half.</summary>
    [Fact]
    public async Task Remember_me_sets_a_persistent_httponly_cookie_and_returns_exactly_three_fields()
    {
        var signedInAt = Now;
        var response = await Web.PostWebLoginAsync(User, Password, rememberMe: true);
        DumpSetCookie("web/login remember_me=true", response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(["access_token", "token_type", "expires_in"], await response.BodyKeysAsync());

        var cookie = response.AuthCookie();
        Assert.NotNull(cookie);
        Assert.True(cookie.HttpOnly);
        Assert.True(cookie.Secure);
        Assert.Equal(SameSiteMode.Lax, cookie.SameSite);
        Assert.Equal(WebAuthTestExtensions.CookiePath, cookie.Path.ToString());
        Assert.False(cookie.Domain.HasValue);
        AssertSameInstant(signedInAt.AddDays(14), cookie.Expires?.UtcDateTime);

        var row = await RowAsync(cookie.Value.ToString());
        Assert.Equal(RefreshTokenSessionKind.WebPersistent, row!.SessionKind);
        AssertSameInstant(row.ExpiresAt, cookie.Expires?.UtcDateTime);
    }

    /// <summary>T-WEB-LOGIN-2 — <c>null</c> sends no <c>remember_me</c> field at all.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(null)]
    public async Task Without_remember_me_the_cookie_is_a_session_cookie_and_the_session_ends_12_hours_after_sign_in(
        bool? rememberMe)
    {
        var signedInAt = Now;
        var response = await Web.PostWebLoginAsync(User, Password, rememberMe);
        DumpSetCookie($"web/login remember_me={rememberMe?.ToString() ?? "(absent)"}", response);

        var cookie = response.AuthCookie();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(cookie);
        Assert.Null(cookie.Expires);
        Assert.Null(cookie.MaxAge);

        var row = await RowAsync(cookie.Value.ToString());
        Assert.Equal(RefreshTokenSessionKind.WebSession, row!.SessionKind);
        AssertSameInstant(signedInAt.AddHours(12), row.ExpiresAt);
        Assert.Equal(row.ChainAbsoluteExpiry, row.ExpiresAt);
    }

    /// <summary>T-WEB-LOGIN-3.</summary>
    [Fact]
    public async Task Signing_in_again_on_the_same_browser_revokes_the_live_web_session_and_opens_a_new_chain()
    {
        var sessionCookie = await WebSignInAsync(rememberMe: false);

        var response = await Web.PostWebLoginAsync(User, Password, rememberMe: true, cookie: sessionCookie);
        var old = await RowAsync(sessionCookie);
        var fresh = await RowAsync(response.AuthCookie()!.Value.ToString());
        output.WriteLine($"  old: kind={old!.SessionKind} reason={old.RevokedReason} chain={old.ChainId}");
        output.WriteLine($"  new: kind={fresh!.SessionKind} revoked_at={fresh.RevokedAt?.ToString("O") ?? "null"} chain={fresh.ChainId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(old.RevokedAt);
        Assert.Equal(RefreshTokenRevocationReason.Logout, old.RevokedReason);
        Assert.Equal(RefreshTokenSessionKind.WebPersistent, fresh.SessionKind);
        Assert.NotEqual(old.ChainId, fresh.ChainId);
        Assert.Null(fresh.RevokedAt);
    }

    /// <summary>T-WEB-LOGIN-3, the ignored cases.</summary>
    [Fact]
    public async Task Signing_in_with_a_garbage_or_mobile_cookie_still_succeeds_and_revokes_nothing()
    {
        var mobile = await Mobile.LoginAsync(User, PasswordVariable);

        var withGarbage = await Web.PostWebLoginAsync(User, Password, rememberMe: true, cookie: "not-a-real-token");
        var withMobileToken = await Web.PostWebLoginAsync(User, Password, rememberMe: true, cookie: mobile.RefreshToken);
        var mobileRow = await RowAsync(mobile.RefreshToken);

        Assert.Equal(HttpStatusCode.OK, withGarbage.StatusCode);
        Assert.Equal(HttpStatusCode.OK, withMobileToken.StatusCode);
        Assert.Equal(RefreshTokenSessionKind.Mobile, mobileRow!.SessionKind);
        Assert.Null(mobileRow.RevokedAt);
    }

    [Fact]
    public async Task A_failed_sign_in_revokes_nothing_even_when_it_carries_a_live_web_cookie()
    {
        var cookie = await WebSignInAsync(rememberMe: true);

        var response = await Web.PostWebLoginAsync(User, "definitely-not-the-password", rememberMe: true, cookie: cookie);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(ErrorCodes.InvalidCredentials, await response.ErrorCodeAsync());
        Assert.Null(response.AuthCookie());
        Assert.Null((await RowAsync(cookie))!.RevokedAt);
    }

    // ── T-WEB-REFRESH ───────────────────────────────────────────────────────────

    /// <summary>T-WEB-REFRESH-1, persistent.</summary>
    [Fact]
    public async Task Refreshing_a_persistent_session_rotates_the_cookie_and_slides_its_expiry()
    {
        var first = await WebSignInAsync(rememberMe: true);
        factory.Clock.Advance(TimeSpan.FromDays(1));
        var refreshedAt = Now;

        var response = await Web.PostWebAsync("refresh", cookie: first);
        DumpSetCookie("web/refresh (persistent, +1 day)", response);
        var cookie = response.AuthCookie();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(["access_token", "token_type", "expires_in"], await response.BodyKeysAsync());
        Assert.NotNull(cookie);
        Assert.NotEqual(first, cookie.Value.ToString());
        AssertSameInstant(refreshedAt.AddDays(14), cookie.Expires?.UtcDateTime);

        Assert.Equal(RefreshTokenRevocationReason.Rotation, (await RowAsync(first))!.RevokedReason);
        Assert.Equal(RefreshTokenSessionKind.WebPersistent, (await RowAsync(cookie.Value.ToString()))!.SessionKind);
    }

    /// <summary>T-WEB-REFRESH-1, session.</summary>
    [Fact]
    public async Task Refreshing_a_session_rotates_the_cookie_but_never_extends_the_12_hour_limit()
    {
        var first = await WebSignInAsync(rememberMe: false);
        var original = await RowAsync(first);
        factory.Clock.Advance(TimeSpan.FromHours(1));

        var response = await Web.PostWebAsync("refresh", cookie: first);
        DumpSetCookie("web/refresh (session, +1 hour)", response);
        var cookie = response.AuthCookie();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(cookie);
        Assert.NotEqual(first, cookie.Value.ToString());
        Assert.Null(cookie.Expires);
        Assert.Null(cookie.MaxAge);

        var fresh = await RowAsync(cookie.Value.ToString());
        Assert.Equal(RefreshTokenRevocationReason.Rotation, (await RowAsync(first))!.RevokedReason);
        Assert.Equal(RefreshTokenSessionKind.WebSession, fresh!.SessionKind);
        Assert.Equal(original!.ExpiresAt, fresh.ExpiresAt);
    }

    /// <summary>T-WEB-REFRESH-2.</summary>
    [Fact]
    public async Task A_session_dies_12_hours_after_sign_in_however_recently_it_was_refreshed()
    {
        var first = await WebSignInAsync(rememberMe: false);

        factory.Clock.Advance(TimeSpan.FromHours(11));
        var inside = await Web.PostWebAsync("refresh", cookie: first);
        Assert.Equal(HttpStatusCode.OK, inside.StatusCode);

        factory.Clock.Advance(TimeSpan.FromHours(1) + TimeSpan.FromSeconds(1));
        var after = await Web.PostWebAsync("refresh", cookie: inside.AuthCookie()!.Value.ToString());

        Assert.Equal(HttpStatusCode.Unauthorized, after.StatusCode);
        Assert.Equal(ErrorCodes.InvalidRefreshToken, await after.ErrorCodeAsync());
    }

    /// <summary>T-WEB-REFRESH-3.</summary>
    [Fact]
    public async Task Refresh_reads_the_cookie_and_never_the_body()
    {
        var webToken = await WebSignInAsync(rememberMe: true);

        var noCookie = await Web.PostWebAsync("refresh");
        var bodyOnly = await Web.PostWebAsync("refresh", body: new { refresh_token = webToken });

        Assert.Equal(HttpStatusCode.Unauthorized, noCookie.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, bodyOnly.StatusCode);
        Assert.Null((await RowAsync(webToken))!.RevokedAt);
    }

    /// <summary>T-WEB-REFRESH-4.</summary>
    [Fact]
    public async Task A_failed_refresh_never_touches_the_cookie()
    {
        var first = await WebSignInAsync(rememberMe: true);
        var winner = await Web.PostWebAsync("refresh", cookie: first);
        Assert.Equal(HttpStatusCode.OK, winner.StatusCode);

        (string Label, HttpResponseMessage Response)[] failures =
        [
            ("no cookie", await Web.PostWebAsync("refresh")),
            ("unknown token", await Web.PostWebAsync("refresh", cookie: "garbage")),
            ("loser replaying the rotated token inside the grace window", await Web.PostWebAsync("refresh", cookie: first)),
        ];

        foreach (var (label, response) in failures)
        {
            DumpSetCookie(label, response);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.False(response.Headers.Contains(HeaderNames.SetCookie), $"{label}: a failed refresh sent Set-Cookie");
        }

        Assert.Null((await RowAsync(winner.AuthCookie()!.Value.ToString()))!.RevokedAt);
    }

    /// <summary>T-WEB-REFRESH-5.</summary>
    [Fact]
    public async Task Replaying_a_rotated_cookie_after_the_grace_window_revokes_the_whole_chain()
    {
        var first = await WebSignInAsync(rememberMe: true);
        var second = (await Web.PostWebAsync("refresh", cookie: first)).AuthCookie()!.Value.ToString();

        factory.Clock.Advance(TimeSpan.FromSeconds(31));
        var replay = await Web.PostWebAsync("refresh", cookie: first);
        var chain = await factory.ChainAsync((await RowAsync(first))!.ChainId);

        foreach (var row in chain)
        {
            output.WriteLine($"  id={row.Id} kind={row.SessionKind} reason={row.RevokedReason?.ToString() ?? "null"}");
        }

        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
        Assert.All(chain, row => Assert.NotNull(row.RevokedAt));
        Assert.Equal(RefreshTokenRevocationReason.ReuseDetected, (await RowAsync(second))!.RevokedReason);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Web.PostWebAsync("refresh", cookie: second)).StatusCode);
    }

    // ── T-WEB-LOGOUT ────────────────────────────────────────────────────────────

    /// <summary>T-WEB-LOGOUT-1.</summary>
    [Fact]
    public async Task Logout_revokes_the_token_and_deletes_the_cookie_with_the_same_attributes()
    {
        var cookie = await WebSignInAsync(rememberMe: true);

        var response = await Web.PostWebAsync("logout", cookie: cookie);
        DumpSetCookie("web/logout", response);
        var deletion = response.AuthCookie();

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.NotNull(deletion);
        Assert.Equal(string.Empty, deletion.Value.ToString());
        Assert.True(deletion.Expires < DateTimeOffset.UnixEpoch.AddDays(1), "the deleting cookie must already be expired");
        Assert.Equal(WebAuthTestExtensions.CookiePath, deletion.Path.ToString());
        Assert.True(deletion.Secure);
        Assert.True(deletion.HttpOnly);
        Assert.Equal(SameSiteMode.Lax, deletion.SameSite);

        Assert.Equal(RefreshTokenRevocationReason.Logout, (await RowAsync(cookie))!.RevokedReason);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Web.PostWebAsync("refresh", cookie: cookie)).StatusCode);
    }

    /// <summary>T-WEB-LOGOUT-2.</summary>
    [Fact]
    public async Task Logout_without_a_cookie_is_still_204_and_still_clears_the_cookie()
    {
        var response = await Web.PostWebAsync("logout");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.NotNull(response.AuthCookie());
    }

    // ── T-ORIGIN-1 ──────────────────────────────────────────────────────────────

    public static TheoryData<string, string?> ForeignOrigins()
    {
        var data = new TheoryData<string, string?>();
        foreach (var endpoint in new[] { "login", "refresh", "logout" })
        {
            foreach (var origin in new string?[] { null, "https://evil.example", "null", "http://localhost:3000" })
            {
                data.Add(endpoint, origin);
            }
        }

        return data;
    }

    /// <summary><c>origin = null</c> sends no <c>Origin</c> header at all.</summary>
    [Theory]
    [MemberData(nameof(ForeignOrigins))]
    public async Task Every_web_endpoint_refuses_a_missing_or_foreign_origin_before_doing_anything(
        string endpoint, string? origin)
    {
        var cookie = await WebSignInAsync(rememberMe: true);
        object? body = endpoint == "login" ? new { username = User, password = Password } : null;

        var response = await Web.PostWebAsync(endpoint, body, cookie, origin);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(ErrorCodes.OriginNotAllowed, await response.ErrorCodeAsync());
        Assert.False(response.Headers.Contains(HeaderNames.SetCookie));
        Assert.Null((await RowAsync(cookie))!.RevokedAt);
    }

    // ── T-BIND ──────────────────────────────────────────────────────────────────

    /// <summary>T-BIND-1.</summary>
    [Fact]
    public async Task A_web_token_is_refused_by_the_mobile_endpoints_and_left_alive()
    {
        var webToken = await WebSignInAsync(rememberMe: true);

        var refresh = await Mobile.PostRefreshAsync(webToken);
        var logout = await Mobile.PostLogoutAsync(webToken);
        var row = await RowAsync(webToken);
        output.WriteLine($"  /auth/refresh: {(int)refresh.StatusCode} | /auth/logout: {(int)logout.StatusCode} | revoked_at: {row!.RevokedAt?.ToString("O") ?? "null"}");

        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.Null(row.RevokedAt);
        Assert.Equal(HttpStatusCode.OK, (await Web.PostWebAsync("refresh", cookie: webToken)).StatusCode);
    }

    /// <summary>T-BIND-2.</summary>
    [Fact]
    public async Task A_mobile_token_is_refused_by_the_web_endpoints_and_left_alive()
    {
        var tokens = await Mobile.LoginAsync(User, PasswordVariable);

        var refresh = await Web.PostWebAsync("refresh", cookie: tokens.RefreshToken);
        var logout = await Web.PostWebAsync("logout", cookie: tokens.RefreshToken);
        var row = await RowAsync(tokens.RefreshToken);
        output.WriteLine($"  web/refresh: {(int)refresh.StatusCode} | web/logout: {(int)logout.StatusCode} | revoked_at: {row!.RevokedAt?.ToString("O") ?? "null"}");

        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.Null(row.RevokedAt);
        Assert.Equal(HttpStatusCode.OK, (await Mobile.PostRefreshAsync(tokens.RefreshToken)).StatusCode);
    }

    /// <summary>T-BIND-3.</summary>
    [Theory]
    [InlineData(RefreshTokenSessionKind.WebSession)]
    [InlineData(RefreshTokenSessionKind.WebPersistent)]
    [InlineData(RefreshTokenSessionKind.Mobile)]
    public async Task Rotation_keeps_the_session_kind_through_consecutive_refreshes(RefreshTokenSessionKind kind)
    {
        var token = kind switch
        {
            RefreshTokenSessionKind.Mobile => (await Mobile.LoginAsync(User, PasswordVariable)).RefreshToken,
            RefreshTokenSessionKind.WebPersistent => await WebSignInAsync(rememberMe: true),
            _ => await WebSignInAsync(rememberMe: false),
        };

        for (var round = 1; round <= 3; round++)
        {
            factory.Clock.Advance(TimeSpan.FromMinutes(5));

            string next;
            if (kind == RefreshTokenSessionKind.Mobile)
            {
                next = (await (await Mobile.PostRefreshAsync(token)).ReadTokensAsync()).RefreshToken;
            }
            else
            {
                var response = await Web.PostWebAsync("refresh", cookie: token);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                var cookie = response.AuthCookie()!;
                next = cookie.Value.ToString();

                if (kind == RefreshTokenSessionKind.WebSession)
                {
                    Assert.Null(cookie.Expires);
                }
                else
                {
                    Assert.NotNull(cookie.Expires);
                }
            }

            var before = (await RowAsync(token))!.SessionKind;
            var after = (await RowAsync(next))!.SessionKind;
            output.WriteLine($"  round {round}: {before} -> {after}");

            Assert.Equal(kind, before);
            Assert.Equal(before, after);
            token = next;
        }
    }
}
