using System.Net;
using LuxMap.Modules.Identity.Auth;
using LuxMap.Modules.Identity.Entities;
using LuxMap.Modules.Identity.Seeding;
using LuxMap.Persistence;
using LuxMap.Shared.Http;
using LuxMap.Shared.Authorization;
using LuxMap.Shared.Contracts.Errors;
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
    ICurrentActorAccessor actor,
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

        // Both password flows lock the account before links, preventing cross-flow deadlocks.
        var candidate = await db.Set<AccountToken>().Where(token => token.TokenHash == hash)
            .Select(token => new { token.Id, token.UserId }).SingleOrDefaultAsync(ct);
        if (candidate is not null)
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM app_user WHERE user_id = {candidate.UserId} FOR UPDATE", ct);
            await db.Database.ExecuteSqlRawAsync("SELECT 1 FROM account_token WHERE id = {0} FOR UPDATE", [candidate.Id], ct);
        }

        var link = candidate is null ? null : await db.Set<AccountToken>().Include(token => token.User)
            .SingleOrDefaultAsync(token => token.Id == candidate.Id, ct);
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

    /// <summary>Requires a live account and its current password; the target always comes from the token.</summary>
    public async Task ChangePasswordAsync(ChangePasswordRequest request, CancellationToken ct)
    {
        var userId = actor.UserId ?? throw Unauthenticated();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        // Serialize two changes so both cannot verify against the same old hash and report success.
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM app_user WHERE user_id = {userId} FOR UPDATE", ct);
        var user = await db.Set<AppUser>().SingleOrDefaultAsync(candidate => candidate.UserId == userId, ct);
        if (user is null || user.IsLocked || user.PasswordHash is null || user.PasswordSetAt is null)
            throw Unauthenticated();

        if (hasher.VerifyHashedPassword(user, user.PasswordHash, request.OldPassword!) == PasswordVerificationResult.Failed)
            throw new LuxMapException(ErrorCodes.ValidationFailed, HttpStatusCode.BadRequest,
                "The current password is incorrect.", new Dictionary<string, object?> { ["field"] = "old_password" });

        var now = UtcMicrosecondClock.UtcNow(clock);
        user.PasswordHash = hasher.HashPassword(user, request.NewPassword!);
        user.PasswordAlgorithm = IdentitySeeder.PasswordAlgorithm;
        user.PasswordSetAt = now;
        user.UpdatedAt = now;
        var unusedLinks = await db.Set<AccountToken>()
            .Where(token => token.UserId == user.UserId && token.UsedAt == null).ToListAsync(ct);
        db.Set<AccountToken>().RemoveRange(unusedLinks);
        await AccountTokens.RevokeSessionsAsync(db, user.UserId, RefreshTokenRevocationReason.PasswordReset, now, ct);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        logger.LogInformation("Account {UserId} changed its own password.", user.UserId);
    }

    private static LuxMapException Unauthenticated() => new(ErrorCodes.Unauthenticated, HttpStatusCode.Unauthorized,
        "This access token no longer identifies a usable password account. Sign in again.");

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
