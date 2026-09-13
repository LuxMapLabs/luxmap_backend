using System.Linq.Expressions;
using LuxMap.Modules.Identity.Entities;

namespace LuxMap.Modules.Identity.Auth;

/// <summary>
/// The two endpoint groups of Contract section 2.10. A refresh token is honoured ONLY by the group
/// that issued it: a web token presented to <c>/auth/refresh</c>, or a mobile token presented to
/// <c>/auth/web/refresh</c>, is treated exactly like an unknown token.
/// </summary>
public enum AuthEndpointGroup
{
    /// <summary><c>/api/v1/auth/{login,refresh,logout}</c> — token in the body.</summary>
    Mobile,

    /// <summary><c>/api/v1/auth/web/{login,refresh,logout}</c> — token in the HttpOnly cookie.</summary>
    Web,
}

internal static class AuthEndpointGroupExtensions
{
    /// <summary>
    /// The ONE place that says which session kinds a group owns. An expression rather than a method,
    /// so refresh, logout and the login-time revocation all run the same predicate inside SQL: a
    /// token of the other group is never even loaded, so it cannot be revoked or trip reuse detection.
    /// </summary>
    public static Expression<Func<RefreshToken, bool>> Owns(this AuthEndpointGroup group) => group switch
    {
        AuthEndpointGroup.Mobile => token => token.SessionKind == RefreshTokenSessionKind.Mobile,
        AuthEndpointGroup.Web => token => token.SessionKind == RefreshTokenSessionKind.WebPersistent
                                          || token.SessionKind == RefreshTokenSessionKind.WebSession,
        _ => throw new ArgumentOutOfRangeException(nameof(group), group, "Unknown auth endpoint group."),
    };
}
