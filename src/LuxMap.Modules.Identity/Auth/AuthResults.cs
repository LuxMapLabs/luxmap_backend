namespace LuxMap.Modules.Identity.Auth;

/// <summary>Why an authentication operation failed. The controller maps these onto HTTP statuses.</summary>
public enum AuthFailure
{
    /// <summary>Wrong username OR wrong password — deliberately indistinguishable.</summary>
    InvalidCredentials,

    AccountLocked,

    /// <summary>Refresh token unknown, expired, revoked or replayed — deliberately indistinguishable.</summary>
    InvalidRefreshToken,
}

/// <param name="AccessToken">The JWT.</param>
/// <param name="RefreshToken">The raw string, returned THIS ONCE only; the database keeps just its hash.</param>
/// <param name="ExpiresInSeconds">Lifetime of the ACCESS token, in seconds.</param>
public sealed record AuthTokens(string AccessToken, string RefreshToken, int ExpiresInSeconds);

public sealed record AuthResult(AuthTokens? Tokens, AuthFailure? Failure)
{
    public static AuthResult Success(AuthTokens tokens) => new(tokens, null);

    public static AuthResult Fail(AuthFailure failure) => new(null, failure);

    public bool Succeeded => Tokens is not null;
}
