namespace LuxMap.Modules.Identity.Entities;

/// <summary>
/// A refresh token for one sign-in session. Contract section 0.2 deliberately assigns no prefix to
/// this table because it never reaches the front end, so a plain <c>bigint identity</c> surrogate key
/// is used instead.
/// </summary>
/// <remarks>
/// The raw token is NEVER stored, only its hash. A database leak still leaves an attacker unable to
/// impersonate the session.
/// </remarks>
public class RefreshToken
{
    public long Id { get; set; }

    public required string UserId { get; set; }

    /// <summary>
    /// Groups every token produced by ONE sign-in. Each sign-in opens its own chain, so revoking this
    /// chain never disturbs a session running on the user's other device.
    /// </summary>
    public Guid ChainId { get; set; }

    /// <summary>The token hash, with a unique index so lookup is a single index read.</summary>
    public required string TokenHash { get; set; }

    public DateTime ExpiresAt { get; set; }

    /// <summary>
    /// The chain's absolute ceiling: FIRST sign-in time plus 90 days. Every token in the chain
    /// inherits this exact value; rotation NEVER pushes it further out.
    /// </summary>
    public DateTime ChainAbsoluteExpiry { get; set; }

    /// <summary>Null means not revoked.</summary>
    public DateTime? RevokedAt { get; set; }

    /// <summary>Null while not revoked. See <see cref="RefreshTokenRevocationReason"/>.</summary>
    public RefreshTokenRevocationReason? RevokedReason { get; set; }

    /// <summary>
    /// The token that replaced this one during rotation (BE-07). Having the replacement chain makes
    /// it possible to spot a rotated token being reused — a sign the token was stolen.
    /// </summary>
    public long? ReplacedByTokenId { get; set; }

    public DateTime CreatedAt { get; set; }

    public AppUser User { get; set; } = null!;

    public RefreshToken? ReplacedByToken { get; set; }
}
