using System.ComponentModel.DataAnnotations;

namespace LuxMap.Modules.Identity.Auth;

/// <summary>
/// Login CHỈ kiểm tra có mặt và độ dài tối đa. KHÔNG áp password policy ở đây — mật khẩu hợp lệ
/// đặt từ trước sẽ bị chặn trước khi kịp so với DB.
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
/// Hình dạng response của login và refresh. ĐÚNG bốn trường, không thêm gì.
/// Serialize snake_case theo quy ước BE-00.
/// </summary>
/// <param name="ExpiresIn">Lifetime của ACCESS token tính bằng giây, kể từ lúc phát response.</param>
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
