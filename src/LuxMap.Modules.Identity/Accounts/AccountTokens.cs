using LuxMap.Modules.Identity.Auth;
using LuxMap.Modules.Identity.Entities;
using LuxMap.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LuxMap.Modules.Identity.Accounts;

/// <summary>Issues invite and reset links. Only the hash is stored; the raw value goes into the mail.</summary>
internal static class AccountTokens
{
    /// <summary>
    /// Adds a fresh link for <paramref name="user"/> and drops that account's unused links of the same
    /// purpose, so only the newest mail works (D-1). Nothing is saved here.
    /// </summary>
    public static async Task<(string RawToken, DateTime ExpiresAt)> IssueAsync(
        LuxMapDbContext db, AppUser user, AccountTokenPurpose purpose, DateTime now, CancellationToken ct)
    {
        var superseded = await db.Set<AccountToken>()
            .Where(token => token.UserId == user.UserId && token.Purpose == purpose && token.UsedAt == null)
            .ToListAsync(ct);
        db.Set<AccountToken>().RemoveRange(superseded);

        var raw = RefreshTokenGenerator.CreateRawToken();
        var expiresAt = now.Add(AccountTokenLifetimes.For(purpose));
        user.AccountTokens.Add(new AccountToken
        {
            UserId = user.UserId,
            Purpose = purpose,
            TokenHash = RefreshTokenGenerator.Hash(raw),
            ExpiresAt = expiresAt,
            CreatedAt = now,
        });
        return (raw, expiresAt);
    }

    /// <summary>Ends every live session of the account, e.g. when it is locked or its password is reset.</summary>
    public static async Task RevokeSessionsAsync(
        LuxMapDbContext db, string userId, RefreshTokenRevocationReason reason, DateTime now, CancellationToken ct)
    {
        var live = await db.Set<RefreshToken>()
            .Where(token => token.UserId == userId && token.RevokedAt == null)
            .ToListAsync(ct);
        foreach (var token in live)
        {
            token.RevokedAt = now;
            token.RevokedReason = reason;
        }
    }
}
