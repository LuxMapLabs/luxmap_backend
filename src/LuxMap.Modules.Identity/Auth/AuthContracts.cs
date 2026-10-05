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

/// <summary>
/// The signed-in user, as <c>GET /api/v1/auth/me</c> returns it (Contract section 4.7).
/// </summary>
/// <remarks>
/// <b>Read from the DATABASE, never from the token's claims.</b> That is the whole reason this
/// endpoint exists rather than letting the front end decode the JWT: an access token lives 60
/// minutes, so a commune an administrator assigns is invisible in the claims until the user signs in
/// again, while this answers with what is true now. Two of the fields are not in the token at all
/// (<c>full_name</c>, <c>email</c>), and a display name is the thing a front end needs first.
/// </remarks>
/// <param name="Role">A Contract section 3.1 <c>user_role</c> string, e.g. <c>manager</c>.</param>
/// <param name="CommuneIds">The communes the account may reach, or <c>["*"]</c> for an administrator. May be empty.</param>
public sealed record CurrentUserResponse(
    string UserId,
    string Username,
    string Email,
    string FullName,
    string Role,
    IReadOnlyList<string> CommuneIds);
