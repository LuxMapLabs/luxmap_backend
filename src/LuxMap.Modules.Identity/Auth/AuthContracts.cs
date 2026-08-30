using System.ComponentModel.DataAnnotations;

namespace LuxMap.Modules.Identity.Auth;

/// <summary>
/// Login validates presence and a sane maximum length ONLY. No password policy here — an old but
/// valid password would be rejected before it ever reached the database.
/// </summary>
public sealed class LoginRequest
{
    [Required]
    [MaxLength(256)]
    public string? Username { get; init; }

    [Required]
    [MaxLength(1024)]
    public string? Password { get; init; }
}

public sealed class RefreshRequest
{
    [Required]
    [MaxLength(1024)]
    public string? RefreshToken { get; init; }
}

public sealed class LogoutRequest
{
    [Required]
    [MaxLength(1024)]
    public string? RefreshToken { get; init; }
}

/// <summary>
/// Response shape for login and refresh. EXACTLY four fields, nothing more.
/// Serialised as snake_case per the BE-00 conventions.
/// </summary>
/// <param name="ExpiresIn">Lifetime of the ACCESS token in seconds, measured from when the response is issued.</param>
public sealed record AuthTokenResponse(
    string AccessToken,
    string RefreshToken,
    string TokenType,
    int ExpiresIn)
{
    public const string BearerTokenType = "Bearer";

    public static AuthTokenResponse From(AuthTokens tokens)
        => new(tokens.AccessToken, tokens.RefreshToken, BearerTokenType, tokens.ExpiresInSeconds);
}
