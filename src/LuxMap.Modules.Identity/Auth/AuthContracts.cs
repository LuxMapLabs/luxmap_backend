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
