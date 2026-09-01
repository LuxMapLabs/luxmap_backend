using System.Security.Cryptography;
using System.Text;
using LuxMap.Modules.Identity.Entities;
using LuxMap.Persistence.Conventions;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace LuxMap.Modules.Identity.Auth;

public sealed record IssuedAccessToken(string Token, DateTime ExpiresAtUtc, int ExpiresInSeconds);

/// <summary>Issues HS256 access tokens. BE-07 only ISSUES; validation belongs to BE-08.</summary>
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

        // Use SecurityTokenDescriptor.Claims with an ARRAY value rather than several claims sharing a
        // name: the handler only merges same-named claims into an array when there are TWO OR MORE,
        // so a single commune would serialise as a string and break every reader.
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
/// Generates and hashes refresh tokens. The token is 32 random bytes, so it cannot be brute-forced —
/// a slow hash like PBKDF2 would add no security and would slow down every refresh. SHA-256 keeps
/// lookup to a single unique-index read.
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
