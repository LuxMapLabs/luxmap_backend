namespace LuxMap.Modules.Identity.Entities;

/// <summary>
/// A single-use link credential sent by email (BE-33a): an invitation to set the first password, or a
/// password reset. Only the SHA-256 hash is stored; the raw token exists in the email and nowhere else.
/// </summary>
public class AccountToken
{
    public long Id { get; set; }

    public required string UserId { get; set; }

    public AccountTokenPurpose Purpose { get; set; }

    /// <summary>Hex SHA-256 of the raw token, as <c>RefreshTokenGenerator.Hash</c> computes it.</summary>
    public required string TokenHash { get; set; }

    public DateTime ExpiresAt { get; set; }

    /// <summary>Set when the token is redeemed. A used token is never accepted again.</summary>
    public DateTime? UsedAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public AppUser User { get; set; } = null!;
}

/// <summary>Why the link was sent. Internal detail, never a Contract section 1 enum.</summary>
public enum AccountTokenPurpose
{
    /// <summary>Set the first password of an account a system admin created. Lives 72 hours (D-1).</summary>
    Invite,

    /// <summary>Set a new password after "forgot password". Lives 1 hour (D-1).</summary>
    Reset,
}
