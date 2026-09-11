using LuxMap.Shared.Contracts;
using Microsoft.AspNetCore.Http;

namespace LuxMap.Modules.Identity.Auth.Web;

/// <summary>
/// The web refresh-token cookie (Contract section 2.10.3). Every attribute is a constant: the
/// Contract fixes them, so no deployment setting can loosen one.
/// </summary>
/// <remarks>
/// ONE builder for the attributes, shared by <see cref="Append"/> and <see cref="Delete"/>. A browser
/// only deletes a cookie when the deleting <c>Set-Cookie</c> repeats its <c>Path</c> — and, for a
/// <c>__Secure-</c> cookie, <c>Secure</c> — so two separately written option sets would drift and
/// leave the old cookie alive after logout.
/// </remarks>
public static class RefreshTokenCookie
{
    /// <summary>
    /// The <c>__Secure-</c> prefix makes the browser itself refuse the cookie unless it is
    /// <c>Secure</c> and was set over https.
    /// </summary>
    public const string Name = "__Secure-luxmap_rt";

    /// <summary>
    /// Only the three web endpoints ever receive the cookie. <c>/auth/login</c> and every data
    /// endpoint never see it.
    /// </summary>
    public const string Path = ApiRoutes.BasePath + "/auth/web";

    public static string? Read(HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return request.Cookies[Name];
    }

    /// <param name="expiresAt">The refresh token's expiry for a persistent session; <c>null</c> writes a
    /// session cookie (no <c>Expires</c>, no <c>Max-Age</c>).</param>
    public static void Append(HttpResponse response, string refreshToken, DateTime? expiresAt)
    {
        ArgumentNullException.ThrowIfNull(response);

        response.Cookies.Append(Name, refreshToken, Options(expiresAt));
    }

    public static void Delete(HttpResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        response.Cookies.Delete(Name, Options(expiresAt: null));
    }

    private static CookieOptions Options(DateTime? expiresAt) => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Lax,
        Path = Path,
        IsEssential = true,
        Expires = expiresAt is { } value ? new DateTimeOffset(value, TimeSpan.Zero) : null,
    };
}
