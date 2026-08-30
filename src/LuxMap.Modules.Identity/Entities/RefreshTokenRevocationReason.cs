namespace LuxMap.Modules.Identity.Entities;

/// <summary>
/// Why a refresh token was revoked. Not a Contract section 1 enum — this is internal detail and never
/// leaves the API.
/// </summary>
/// <remarks>
/// Knowing the reason is REQUIRED to handle token replay correctly: a benign retry after rotation
/// must stay silent, and a logout must never be treated as an attack.
/// </remarks>
public enum RefreshTokenRevocationReason
{
    /// <summary>Replaced by a new token during refresh. Reuse inside the grace window is a retry.</summary>
    Rotation,

    /// <summary>The user signed out deliberately. Reuse NEVER triggers chain revocation.</summary>
    Logout,

    /// <summary>Revoked because a rotated token was replayed after the grace window.</summary>
    ReuseDetected,
}
