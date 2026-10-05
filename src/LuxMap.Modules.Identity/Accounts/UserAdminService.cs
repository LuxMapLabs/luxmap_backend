using System.Linq.Expressions;
using System.Net;
using LuxMap.Modules.Identity.Auth;
using LuxMap.Modules.Identity.Entities;
using LuxMap.Modules.Identity.Seeding;
using LuxMap.Persistence;
using LuxMap.Persistence.Conventions;
using LuxMap.Shared.Authorization;
using LuxMap.Shared.Contracts;
using LuxMap.Shared.Contracts.Enums;
using LuxMap.Shared.Contracts.Errors;
using LuxMap.Shared.Contracts.Paging;
using LuxMap.Shared.Http;
using LuxMap.Shared.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace LuxMap.Modules.Identity.Accounts;

/// <summary>
/// Accounts as a system admin manages them (BE-33a, D-R11): create with an emailed invitation, read,
/// change role / communes / contact details, lock and unlock, resend the invitation.
/// </summary>
/// <remarks>
/// Not written to <c>audit_event</c>: that table requires a commune and an account belongs to none
/// (D-12). Every change is a structured log line naming the admin who made it.
/// </remarks>
public sealed class UserAdminService(
    LuxMapDbContext db,
    AccountMailer mailer,
    ICurrentActorAccessor actor,
    TimeProvider clock,
    ILogger<UserAdminService> logger)
{
    public async Task<CreateUserResponse> CreateAsync(CreateUserRequest request, CancellationToken ct)
    {
        var role = request.Role!.Value;
        var username = request.Username!.Trim();
        var email = request.Email!.Trim();
        var communes = await ValidCommunesAsync(role, request.CommuneIds, ct);
        await RequireFreeAsync(username, email, exceptUserId: null, ct);

        var now = UtcMicrosecondClock.UtcNow(clock);

        // The id is drawn before inserting, the same draw the column DEFAULT makes, so the commune
        // assignments and the invitation can name the account in the same SaveChanges.
        var nextId = $"SELECT {PrefixedIds.AppUser.DefaultValueSql} AS \"Value\"";
        var userId = await db.Database.SqlQueryRaw<string>(nextId).SingleAsync(ct);

        var user = new AppUser
        {
            UserId = userId,
            Username = username,
            Email = email,
            FullName = request.FullName!.Trim(),
            Role = role,
            HasSystemWideScope = role == UserRole.SystemAdmin,
            PasswordAlgorithm = IdentitySeeder.PasswordAlgorithm,
            CreatedAt = now,
            UpdatedAt = now,
            CommuneAssignments = communes
                .Select(commune => new AppUserCommune { UserId = userId, CommuneId = commune, AssignedAt = now })
                .ToList(),
        };
        db.Set<AppUser>().Add(user);
        var (raw, expiresAt) = await AccountTokens.IssueAsync(db, user, AccountTokenPurpose.Invite, now, ct);
        await SaveAsync(ct);

        var sent = await mailer.TrySendAsync(user, AccountTokenPurpose.Invite, raw, expiresAt, ct);
        logger.LogInformation(
            "Account {UserId} ({Username}) created by {AdminId}: role {Role}, communes {Communes}; invitation sent: {Sent}.",
            user.UserId, user.Username, actor.UserId, ContractEnum.ToDbValue(role), communes, sent);
        return new CreateUserResponse(Item(user), sent);
    }

    public async Task<PagedResult<UserAccountItem>> ListAsync(string? roles, string? status, PageRequest page, CancellationToken ct)
    {
        var query = db.Set<AppUser>().AsNoTracking().Include(user => user.CommuneAssignments).AsQueryable();

        var roleFilter = WireEnum.ParseCsv<UserRole>(roles, "role")?.ToArray();
        if (roleFilter is { Length: > 0 })
        {
            query = query.Where(user => roleFilter.Contains(user.Role));
        }

        if (status is not null)
        {
            query = query.Where(StatusIs(status));
        }

        var total = await query.CountAsync(ct);

        // Prefixed ids are at least N digits, not exactly N: order by length before text (CLAUDE.md, section 0).
        var users = await query
            .OrderBy(user => user.CreatedAt).ThenBy(user => user.UserId.Length).ThenBy(user => user.UserId)
            .Skip(page.Skip).Take(page.PageSize)
            .ToListAsync(ct);
        return PagedResult<UserAccountItem>.From(page, total, users.Select(Item).ToList());
    }

    public async Task<UserAccountItem> GetAsync(string userId, CancellationToken ct)
        => Item(await FindAsync(userId, tracked: false, ct));

    public async Task<UserAccountItem> UpdateAsync(string userId, UpdateUserRequest request, CancellationToken ct)
    {
        var user = await FindAsync(userId, tracked: true, ct);
        var now = UtcMicrosecondClock.UtcNow(clock);

        if (request.Email is { } requestedEmail)
        {
            var email = requestedEmail.Trim();
            await RequireFreeAsync(username: null, email, exceptUserId: user.UserId, ct);
            user.Email = email;
        }

        if (request.FullName is { } fullName)
        {
            user.FullName = fullName.Trim();
        }

        var role = request.Role ?? user.Role;
        var demotesAdmin = user.Role == UserRole.SystemAdmin && role != UserRole.SystemAdmin;
        if (demotesAdmin)
        {
            await RequireAnotherActiveAdminAsync(user.UserId, ct);
        }

        // Without a list in the body: becoming system_admin drops the communes, moving between the other
        // roles keeps them, and leaving system_admin is refused — the account would sign in seeing nothing.
        if (request.CommuneIds is not null || role != user.Role)
        {
            var requested = request.CommuneIds
                ?? (role == UserRole.SystemAdmin || demotesAdmin ? null : CurrentCommunes(user));
            ReplaceCommunes(user, await ValidCommunesAsync(role, requested, ct), now);
        }

        user.Role = role;
        user.HasSystemWideScope = role == UserRole.SystemAdmin;
        user.UpdatedAt = now;
        await SaveAsync(ct);

        logger.LogInformation(
            "Account {UserId} updated by {AdminId}: role {Role}, communes {Communes}.",
            user.UserId, actor.UserId, ContractEnum.ToDbValue(user.Role), CurrentCommunes(user));
        return Item(user);
    }

    /// <summary>
    /// Locks the account and ends its sessions (D-9). An access token already issued still works until
    /// it expires, at most 60 minutes, as for every other permission change.
    /// </summary>
    public async Task<UserAccountItem> LockAsync(string userId, CancellationToken ct)
    {
        var user = await FindAsync(userId, tracked: true, ct);
        if (user.UserId == actor.UserId)
        {
            throw Error(AccountErrors.CannotLockSelf, HttpStatusCode.Conflict, "You cannot lock your own account.");
        }

        if (user.IsLocked)
        {
            return Item(user);
        }

        if (user.Role == UserRole.SystemAdmin)
        {
            await RequireAnotherActiveAdminAsync(user.UserId, ct);
        }

        var now = UtcMicrosecondClock.UtcNow(clock);
        user.IsLocked = true;
        user.UpdatedAt = now;
        await AccountTokens.RevokeSessionsAsync(db, user.UserId, RefreshTokenRevocationReason.AccountLocked, now, ct);
        await SaveAsync(ct);

        logger.LogInformation("Account {UserId} locked by {AdminId}; its sessions were revoked.", user.UserId, actor.UserId);
        return Item(user);
    }

    public async Task<UserAccountItem> UnlockAsync(string userId, CancellationToken ct)
    {
        var user = await FindAsync(userId, tracked: true, ct);
        if (!user.IsLocked)
        {
            return Item(user);
        }

        user.IsLocked = false;
        user.UpdatedAt = UtcMicrosecondClock.UtcNow(clock);
        await SaveAsync(ct);

        logger.LogInformation("Account {UserId} unlocked by {AdminId}.", user.UserId, actor.UserId);
        return Item(user);
    }

    /// <summary>A new invitation link; any earlier one stops working (D-1).</summary>
    public async Task<InvitationResponse> InviteAsync(string userId, CancellationToken ct)
    {
        var user = await FindAsync(userId, tracked: true, ct);
        if (user.PasswordSetAt is not null)
        {
            throw Error(AccountErrors.AccountAlreadyActive, HttpStatusCode.Conflict,
                "This account has already set its password. It can ask for a reset link instead.");
        }

        var now = UtcMicrosecondClock.UtcNow(clock);
        var (raw, expiresAt) = await AccountTokens.IssueAsync(db, user, AccountTokenPurpose.Invite, now, ct);
        await SaveAsync(ct);

        var sent = await mailer.TrySendAsync(user, AccountTokenPurpose.Invite, raw, expiresAt, ct);
        logger.LogInformation("Invitation for {UserId} resent by {AdminId}; sent: {Sent}.", user.UserId, actor.UserId, sent);
        return new InvitationResponse(sent, expiresAt);
    }

    internal static UserAccountItem Item(AppUser user)
        => new(
            user.UserId,
            user.Username,
            user.Email,
            user.FullName,
            ContractEnum.ToDbValue(user.Role),
            AuthClaims.CommuneIdsFor(user.HasSystemWideScope, CurrentCommunes(user)),
            user.IsLocked ? AccountStatus.Locked : user.PasswordSetAt is null ? AccountStatus.Invited : AccountStatus.Active,
            user.CreatedAt,
            user.PasswordSetAt);

    private static Expression<Func<AppUser, bool>> StatusIs(string status) => status switch
    {
        AccountStatus.Locked => user => user.IsLocked,
        AccountStatus.Invited => user => !user.IsLocked && user.PasswordSetAt == null,
        AccountStatus.Active => user => !user.IsLocked && user.PasswordSetAt != null,
        _ => throw Invalid("status", $"status must be one of: {string.Join(", ", AccountStatus.All)}."),
    };

    private static IReadOnlyList<string> CurrentCommunes(AppUser user)
        => user.CommuneAssignments.Select(assignment => assignment.CommuneId).ToList();

    private static void ReplaceCommunes(AppUser user, IReadOnlyList<string> communes, DateTime now)
    {
        // A diff, not clear-and-add: removing and re-adding the same composite key in one SaveChanges
        // conflicts in the change tracker.
        foreach (var gone in user.CommuneAssignments.Where(assignment => !communes.Contains(assignment.CommuneId)).ToList())
        {
            user.CommuneAssignments.Remove(gone);
        }

        foreach (var added in communes.Except(CurrentCommunes(user), StringComparer.Ordinal))
        {
            user.CommuneAssignments.Add(new AppUserCommune { UserId = user.UserId, CommuneId = added, AssignedAt = now });
        }
    }

    /// <summary>D-8: a system admin covers every commune and takes none; every other role needs at least one that exists.</summary>
    private async Task<IReadOnlyList<string>> ValidCommunesAsync(UserRole role, IReadOnlyList<string>? requested, CancellationToken ct)
    {
        var communes = (requested ?? []).Select(commune => commune.Trim()).Distinct(StringComparer.Ordinal).ToList();
        if (role == UserRole.SystemAdmin)
        {
            return communes.Count == 0
                ? []
                : throw Invalid("commune_ids", "A system admin covers every commune; send no commune_ids.");
        }

        if (communes.Count == 0)
        {
            throw Invalid("commune_ids", "This role needs at least one commune.");
        }

        var known = await db.Set<AdministrativeUnit>()
            .Where(unit => communes.Contains(unit.CommuneId))
            .Select(unit => unit.CommuneId)
            .ToListAsync(ct);
        var unknown = communes.Except(known, StringComparer.Ordinal).ToList();
        return unknown.Count == 0
            ? communes
            : throw Invalid("commune_ids", $"Unknown commune(s): {string.Join(", ", unknown)}.");
    }

    /// <summary>
    /// D-9 / D-10: someone must still be able to manage accounts. "Active" = can sign in: not locked
    /// and a password set.
    /// </summary>
    private async Task RequireAnotherActiveAdminAsync(string userId, CancellationToken ct)
    {
        var others = await db.Set<AppUser>().AnyAsync(
            user => user.UserId != userId && user.Role == UserRole.SystemAdmin && !user.IsLocked && user.PasswordSetAt != null, ct);
        if (!others)
        {
            throw Error(AccountErrors.LastSystemAdmin, HttpStatusCode.Conflict,
                "This is the last active system admin; another one must exist first.");
        }
    }

    /// <summary>
    /// Friendly, specific answer first; the case-insensitive unique indexes are what really guarantee
    /// it, and <see cref="SaveAsync"/> turns a lost race into the same 409.
    /// </summary>
    private async Task RequireFreeAsync(string? username, string email, string? exceptUserId, CancellationToken ct)
    {
        var taken = new Dictionary<string, object?>();
        if (username is not null && await db.Set<AppUser>().AnyAsync(user => user.Username.ToLower() == username.ToLower(), ct))
        {
            taken["username"] = new[] { "This username is already taken." };
        }

        if (await db.Set<AppUser>().AnyAsync(user => user.UserId != exceptUserId && user.Email.ToLower() == email.ToLower(), ct))
        {
            taken["email"] = new[] { "This email address is already used by another account." };
        }

        if (taken.Count > 0)
        {
            throw new LuxMapException(KnownErrors.IdentifierTaken.Code, KnownErrors.IdentifierTaken.StatusCode,
                "That username or email address is already used by another account.", taken);
        }
    }

    private async Task<AppUser> FindAsync(string userId, bool tracked, CancellationToken ct)
    {
        var query = db.Set<AppUser>().Include(user => user.CommuneAssignments).AsQueryable();
        return await (tracked ? query : query.AsNoTracking()).SingleOrDefaultAsync(user => user.UserId == userId, ct)
            ?? throw Error(AccountErrors.UserNotFound, HttpStatusCode.NotFound, "No account has that id.");
    }

    private async Task SaveAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: "23505" })
        {
            // Lost a race against a concurrent create or email change; the unique index caught it.
            throw new LuxMapException(KnownErrors.IdentifierTaken.Code, KnownErrors.IdentifierTaken.StatusCode,
                "That username or email address is already used by another account.");
        }
    }

    private static LuxMapException Invalid(string field, string message)
        => new(ErrorCodes.ValidationFailed, HttpStatusCode.BadRequest, message, new Dictionary<string, object?> { ["field"] = field });

    private static LuxMapException Error(string code, HttpStatusCode status, string message)
        => new(code, status, message);
}
