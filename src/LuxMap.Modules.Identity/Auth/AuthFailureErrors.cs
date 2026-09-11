using LuxMap.Shared.Contracts.Errors;
using LuxMap.Shared.Http;

namespace LuxMap.Modules.Identity.Auth;

/// <summary>
/// The ONE mapping from an <see cref="AuthFailure"/> to its Contract error (section 2.10.6), shared by
/// both endpoint groups so the same failure can never answer with two different bodies.
/// </summary>
internal static class AuthFailureErrors
{
    /// <summary>
    /// Thrown rather than returned, so the BE-04 middleware builds the body — one error shape for
    /// every API.
    /// </summary>
    public static LuxMapException ToException(AuthFailure? failure) => failure switch
    {
        AuthFailure.AccountLocked => new LuxMapException(
            KnownErrors.AccountLocked.Code,
            KnownErrors.AccountLocked.StatusCode,
            "This account is locked. Contact an administrator."),

        AuthFailure.InvalidRefreshToken => new LuxMapException(
            KnownErrors.InvalidRefreshToken.Code,
            KnownErrors.InvalidRefreshToken.StatusCode,
            "The refresh token is not valid."),

        // Wrong username and wrong password SHARE one body: separating them reveals which
        // accounts exist. Never point details at a specific field.
        _ => new LuxMapException(
            KnownErrors.InvalidCredentials.Code,
            KnownErrors.InvalidCredentials.StatusCode,
            "Incorrect username or password."),
    };
}
