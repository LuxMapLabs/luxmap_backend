namespace LuxMap.Modules.Identity.Entities;

/// <summary>
/// Which kind of sign-in a refresh token belongs to (Contract section 2.10.4). Decided ONCE, at login,
/// and copied unchanged onto every token the chain rotates into.
/// </summary>
/// <remarks>
/// Not a Contract section 1 enum and never part of a response body, but stored the same way: its
/// snake_case string plus a CHECK constraint, like <see cref="RefreshTokenRevocationReason"/>.
/// </remarks>
public enum RefreshTokenSessionKind
{
    /// <summary><c>POST /auth/login</c>: token in the body, 30-day sliding, 90-day ceiling.</summary>
    Mobile,

    /// <summary><c>POST /auth/web/login</c> with <c>remember_me = true</c>: persistent cookie, 14-day sliding, 90-day ceiling.</summary>
    WebPersistent,

    /// <summary><c>POST /auth/web/login</c> without <c>remember_me</c>: session cookie, 12 hours absolute.</summary>
    WebSession,
}
