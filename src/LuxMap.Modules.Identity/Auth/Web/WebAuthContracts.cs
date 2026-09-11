using System.ComponentModel.DataAnnotations;

namespace LuxMap.Modules.Identity.Auth.Web;

/// <summary>
/// Web sign-in (Contract section 2.10.2). Same presence-and-length rules as the mobile
/// <see cref="LoginRequest"/>, plus <c>remember_me</c>.
/// </summary>
public sealed class WebLoginRequest
{
    [Required]
    [MaxLength(256)]
    public string? Username { get; init; }

    [Required]
    [MaxLength(1024)]
    public string? Password { get; init; }

    /// <summary>
    /// <c>true</c> opens a <c>web_persistent</c> session with a persistent cookie. Missing or
    /// <c>false</c> opens a <c>web_session</c> with a session cookie — the fail-safe default.
    /// </summary>
    public bool RememberMe { get; init; }
}

/// <summary>
/// What the web group returns: EXACTLY three fields. The refresh token travels only in the cookie,
/// never in a body.
/// </summary>
/// <param name="ExpiresIn">Lifetime of the ACCESS token in seconds, measured from when the response is issued.</param>
public sealed record WebAuthTokenResponse(string AccessToken, string TokenType, int ExpiresIn)
{
    public static WebAuthTokenResponse From(AuthTokens tokens)
    {
        ArgumentNullException.ThrowIfNull(tokens);

        return new(tokens.AccessToken, AuthTokenResponse.BearerTokenType, tokens.ExpiresInSeconds);
    }
}
