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
/// Open registration (BE-07 supplement).
/// </summary>
/// <remarks>
/// ⚠️ There is deliberately NO role, commune_id or commune_ids property here. Registration creates an
/// IDENTITY, never a PERMISSION. Any such field in the request body is ignored by the serializer
/// because it maps to nothing — that is the single most obvious privilege-escalation path on this
/// endpoint, and the shape of this DTO is what closes it.
/// <para>
/// Unlike login, THIS is where a password policy belongs. Login must not enforce one, or a valid
/// older password would be rejected before it ever reached the database.
/// </para>
/// </remarks>
public sealed class RegisterRequest
{
    [Required]
    [MinLength(3)]
    [MaxLength(256)]
    public string? Username { get; init; }

    [Required]
    [EmailAddress]
    [MaxLength(256)]
    public string? Email { get; init; }

    [Required]
    [MinLength(2)]
    [MaxLength(256)]
    public string? FullName { get; init; }

    /// <summary>
    /// Minimum 12 characters and no composition rules, following NIST SP 800-63B: length beats
    /// character-class requirements, which mostly push people towards predictable patterns.
    /// The 1024 ceiling stops a long password being used to hammer PBKDF2.
    /// </summary>
    [Required]
    [MinLength(MinimumPasswordLength)]
    [MaxLength(1024)]
    public string? Password { get; init; }

    public const int MinimumPasswordLength = 12;
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
/// <para>
/// The field list is deliberately the same as <see cref="RegisterResponse"/> minus its
/// <c>message</c>: the shape was already published there, so nothing new is being invented for
/// WP5 and WP6 to bind against.
/// </para>
/// </remarks>
/// <param name="Role">A Contract section 3.1 <c>user_role</c> string, e.g. <c>maintenance_engineer</c>.</param>
/// <param name="CommuneIds">The communes the account may reach, or <c>["*"]</c> for an administrator. May be empty.</param>
public sealed record CurrentUserResponse(
    string UserId,
    string Username,
    string Email,
    string FullName,
    string Role,
    IReadOnlyList<string> CommuneIds);

/// <summary>
/// What registration returns. NO token: the account signs in through POST /auth/login like everyone
/// else, so there stays exactly ONE code path that issues tokens and opens refresh chains.
/// </summary>
/// <param name="Role">Always the lowest role. The client cannot influence it.</param>
/// <param name="CommuneIds">Always empty. Reported back so the client can see that access is not granted yet.</param>
public sealed record RegisterResponse(
    string UserId,
    string Username,
    string Email,
    string FullName,
    string Role,
    IReadOnlyList<string> CommuneIds,
    string Message)
{
    public const string PendingAssignmentMessage =
        "Account created. An administrator must assign communes before any data becomes visible.";
}
