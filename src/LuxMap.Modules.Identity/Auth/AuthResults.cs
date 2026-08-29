namespace LuxMap.Modules.Identity.Auth;

/// <summary>Vì sao một thao tác xác thực thất bại. Controller ánh xạ sang HTTP status.</summary>
public enum AuthFailure
{
    /// <summary>Sai tài khoản HOẶC sai mật khẩu — cố ý không phân biệt.</summary>
    InvalidCredentials,

    AccountLocked,

    /// <summary>Refresh token sai, hết hạn, đã thu hồi, hoặc bị dùng lại — cố ý không phân biệt.</summary>
    InvalidRefreshToken,
}

/// <param name="AccessToken">JWT.</param>
/// <param name="RefreshToken">Chuỗi thô, CHỈ trả về đúng lần này; DB chỉ giữ hash.</param>
/// <param name="ExpiresInSeconds">Lifetime của ACCESS token, tính bằng giây.</param>
public sealed record AuthTokens(string AccessToken, string RefreshToken, int ExpiresInSeconds);

public sealed record AuthResult(AuthTokens? Tokens, AuthFailure? Failure)
{
    public static AuthResult Success(AuthTokens tokens) => new(tokens, null);

    public static AuthResult Fail(AuthFailure failure) => new(null, failure);

    public bool Succeeded => Tokens is not null;
}
