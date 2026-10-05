using System.Net;
using LuxMap.Modules.Identity.Auth;
using LuxMap.Modules.Identity.Entities;
using LuxMap.Modules.Identity.Seeding;
using LuxMap.Persistence;
using LuxMap.Shared.Http;
using LuxMap.Shared.Serialization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LuxMap.Modules.Identity.Accounts;

/// <summary>
/// The two anonymous password endpoints (BE-33a): redeem an emailed link, and ask for one (D-2).
/// </summary>
public sealed class PasswordService(
    LuxMapDbContext db,
    AccountMailer mailer,
    TimeProvider clock,
    ILogger<PasswordService> logger)
{
    /// <summary>The SAME hasher BE-06 seeded with and BE-07 verifies with. Never a second algorithm.</summary>
    private readonly PasswordHasher<AppUser> hasher = new();

    /// <summary>
    /// Sets the password through an invite or reset link. The link works once; every other open link of
    /// the account and every live session end with it.
    /// </summary>
    public async Task SetPasswordAsync(SetPasswordRequest request, CancellationToken ct)
    {
        var hash = RefreshTokenGenerator.Hash(request.Token!);
        var now = UtcMicrosecondClock.UtcNow(clock);

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        // Two requests redeeming the same link must not both win: lock the row, THEN read its state.
        var tokenId = await db.Set<AccountToken>().Where(token => token.TokenHash == hash)
            .Select(token => (long?)token.Id).SingleOrDefaultAsync(ct);
        if (tokenId is not null)
        {
            await db.Database.ExecuteSqlRawAsync("SELECT 1 FROM account_token WHERE id = {0} FOR UPDATE", [tokenId.Value], ct);
        }

        var link = tokenId is null ? null : await db.Set<AccountToken>().Include(token => token.User)
            .SingleAsync(token => token.Id == tokenId, ct);
        if (link is null || link.UsedAt is not null || link.ExpiresAt <= now)
        {
            throw new LuxMapException(AccountErrors.InvalidAccountToken, HttpStatusCode.BadRequest,
                "This link is unknown, expired or already used. Ask for a new one.");
        }

        var user = link.User;
        user.PasswordHash = hasher.HashPassword(user, request.NewPassword!);
        user.PasswordAlgorithm = IdentitySeeder.PasswordAlgorithm;
        user.PasswordSetAt = now;
        user.UpdatedAt = now;
        link.UsedAt = now;

        var otherLinks = await db.Set<AccountToken>()
            .Where(token => token.UserId == user.UserId && token.Id != link.Id && token.UsedAt == null)
            .ToListAsync(ct);
        db.Set<AccountToken>().RemoveRange(otherLinks);
        await AccountTokens.RevokeSessionsAsync(db, user.UserId, RefreshTokenRevocationReason.PasswordReset, now, ct);

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        logger.LogInformation("Account {UserId} set its password through a {Purpose} link.", user.UserId, link.Purpose);
    }

    /// <summary>
    /// Mails a link when the address belongs to an account that can use one, and reveals nothing either
    /// way: the caller always gets the same answer (D-2). An account that never set a password gets a
    /// fresh invitation; a locked account gets nothing.
    /// </summary>
    public async Task RequestResetAsync(ForgotPasswordRequest request, CancellationToken ct)
    {
        var email = request.Email!.Trim();
        var user = await db.Set<AppUser>().SingleOrDefaultAsync(candidate => candidate.Email.ToLower() == email.ToLower(), ct);
        if (user is null || user.IsLocked)
        {
            logger.LogInformation("Password reset requested for an address with no usable account; nothing sent.");
            return;
        }

        var purpose = user.PasswordSetAt is null ? AccountTokenPurpose.Invite : AccountTokenPurpose.Reset;
        var now = UtcMicrosecondClock.UtcNow(clock);
        var (raw, expiresAt) = await AccountTokens.IssueAsync(db, user, purpose, now, ct);
        await db.SaveChangesAsync(ct);
        await mailer.TrySendAsync(user, purpose, raw, expiresAt, ct);
    }
}
