using System.Security.Cryptography;
using System.Text;
using LuxMap.Modules.Identity.Entities;
using LuxMap.Persistence.Conventions;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace LuxMap.Modules.Identity.Auth;

public sealed record IssuedAccessToken(string Token, DateTime ExpiresAtUtc, int ExpiresInSeconds);

/// <summary>Phát access token HS256. BE-07 chỉ PHÁT; việc kiểm token là BE-08.</summary>
public sealed class AccessTokenIssuer(JwtOptions options, TimeProvider timeProvider)
{
    private readonly SigningCredentials credentials = new(
        new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.SigningKey)),
        SecurityAlgorithms.HmacSha256);

    public IssuedAccessToken Issue(AppUser user, IReadOnlyList<string> communeIds)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(communeIds);

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var expires = now.Add(options.AccessTokenLifetime);

        // Dùng SecurityTokenDescriptor.Claims với giá trị là MẢNG thay vì nhiều Claim trùng
        // tên: handler cũ gộp claim trùng tên thành mảng CHỈ KHI có từ hai giá trị, nên một xã
        // sẽ ra chuỗi thay vì mảng và làm hỏng bên đọc.
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = options.Issuer,
            Audience = options.Audience,
            IssuedAt = now,
            NotBefore = now,
            Expires = expires,
            SigningCredentials = credentials,
            Claims = new Dictionary<string, object>
            {
                [AuthClaims.Subject] = user.UserId,
                [AuthClaims.Role] = ContractEnum.ToDbValue(user.Role),
                [AuthClaims.CommuneIds] = communeIds.ToArray(),
            },
        };

        return new IssuedAccessToken(
            new JsonWebTokenHandler().CreateToken(descriptor),
            expires,
            (int)options.AccessTokenLifetime.TotalSeconds);
    }
}

/// <summary>
/// Sinh và băm refresh token. Token là 32 byte ngẫu nhiên nên không brute-force được — băm
/// chậm kiểu PBKDF2 không thêm an toàn mà chỉ làm mỗi lần refresh chậm đi. SHA-256 cho phép
/// tra cứu bằng MỘT lần đọc unique index.
/// </summary>
public static class RefreshTokenGenerator
{
    public const int TokenBytes = 32;

    public static string CreateRawToken()
        => Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(TokenBytes));

    public static string Hash(string rawToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rawToken);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));
    }
}
