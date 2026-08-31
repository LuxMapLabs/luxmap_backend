using LuxMap.Modules.Identity.Entities;
using LuxMap.Modules.Identity.Seeding;
using LuxMap.Persistence.Conventions;
using LuxMap.Shared.Contracts.Enums;
using LuxMap.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Microsoft.Extensions.Logging;

namespace LuxMap.Modules.Identity.Auth;

public sealed class AuthService(
    LuxMapDbContext dbContext,
    AccessTokenIssuer accessTokenIssuer,
    JwtOptions options,
    TimeProvider timeProvider,
    ILogger<AuthService> logger)
{
    private readonly PasswordHasher<AppUser> passwordHasher = new();

    public async Task<AuthResult> LoginAsync(string username, string password, CancellationToken ct = default)
    {
        var normalized = (username ?? string.Empty).Trim();

        // BE-06 stores usernames verbatim with no case normalisation, so the lookup lowercases itself.
        var user = await dbContext.Set<AppUser>()
            .FirstOrDefaultAsync(u => u.Username.ToLower() == normalized.ToLower(), ct);

        if (user is null)
        {
            logger.LogWarning("Sign-in failed: no matching account.");
            return AuthResult.Fail(AuthFailure.InvalidCredentials);
        }

        var verification = passwordHasher.VerifyHashedPassword(user, user.PasswordHash, password ?? string.Empty);
        if (verification == PasswordVerificationResult.Failed)
        {
            logger.LogWarning("Sign-in failed for {UserId}: wrong password.", user.UserId);
            return AuthResult.Fail(AuthFailure.InvalidCredentials);
        }

        // Check the lock AFTER verifying the password: returning 403 earlier reveals that the account exists.
        if (user.IsLocked)
        {
            logger.LogWarning("Sign-in blocked for {UserId}: the account is locked.", user.UserId);
            return AuthResult.Fail(AuthFailure.AccountLocked);
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;

        // Every sign-in opens ONE new chain, so revoking this chain never touches another device.
        var chainId = Guid.NewGuid();
        var chainAbsoluteExpiry = now.AddDays(options.RefreshAbsoluteDays);

        var refresh = await IssueRefreshTokenAsync(user.UserId, chainId, chainAbsoluteExpiry, now, ct);
        await dbContext.SaveChangesAsync(ct);

        logger.LogInformation("Sign-in succeeded for {UserId}, opened chain {ChainId}.", user.UserId, chainId);
        return AuthResult.Success(await BuildTokensAsync(user, refresh.RawToken, ct));
    }

    /// <summary>
    /// Open registration. Creates an IDENTITY, never a PERMISSION.
    /// </summary>
    /// <remarks>
    /// The role and the commune scope are decided HERE, on the server, from constants. Nothing the
    /// client sends can reach them — <see cref="RegisterRequest"/> has no property to carry them.
    /// <para>
    /// The new account can sign in immediately but sees NOTHING: with no rows in
    /// <c>app_user_commune</c>, BE-07 issues <c>commune_ids: []</c> and the BE-08 query filter admits
    /// no row at all. Granting access is an administrator action (BE-33).
    /// </para>
    /// </remarks>
    public async Task<RegisterOutcome> RegisterAsync(
        string username, string email, string fullName, string password, CancellationToken ct = default)
    {
        var normalizedUsername = (username ?? string.Empty).Trim();
        var normalizedEmail = (email ?? string.Empty).Trim();

        // Friendly, specific error first. The functional unique indexes added alongside this endpoint
        // are what actually guarantee it — two concurrent registrations would both pass this check.
        var takenFields = new Dictionary<string, object?>();

        if (await dbContext.Set<AppUser>()
                .AnyAsync(u => u.Username.ToLower() == normalizedUsername.ToLower(), ct))
        {
            takenFields["username"] = new[] { "This username is already taken." };
        }

        if (await dbContext.Set<AppUser>()
                .AnyAsync(u => u.Email.ToLower() == normalizedEmail.ToLower(), ct))
        {
            takenFields["email"] = new[] { "This email address is already registered." };
        }

        if (takenFields.Count > 0)
        {
            logger.LogInformation(
                "Registration rejected for {Username}: identifier already taken.", normalizedUsername);
            return RegisterOutcome.Taken(takenFields);
        }

        var user = new AppUser
        {
            Username = normalizedUsername,
            Email = normalizedEmail,
            FullName = (fullName ?? string.Empty).Trim(),

            // Server-assigned, always. Lowest role, no communes, not locked.
            Role = LowestRole,
            HasSystemWideScope = false,
            IsLocked = false,

            PasswordHash = string.Empty,
            PasswordAlgorithm = IdentitySeeder.PasswordAlgorithm,
        };

        // The SAME hasher BE-06 seeded with. Never introduce a second algorithm.
        user.PasswordHash = passwordHasher.HashPassword(user, password ?? string.Empty);

        dbContext.Set<AppUser>().Add(user);

        try
        {
            await dbContext.SaveChangesAsync(ct);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            // Lost a race against a concurrent registration; the unique index caught it. This is the
            // branch the application-level check above cannot cover.
            dbContext.ChangeTracker.Clear();
            logger.LogInformation(
                "Registration for {Username} lost a race to a concurrent request.", normalizedUsername);
            return RegisterOutcome.Taken(new Dictionary<string, object?>
            {
                ["username"] = new[] { "This username is already taken." },
            });
        }

        // NO commune assignment here. That is exactly the point of the design.
        logger.LogInformation(
            "Registered {UserId} ({Username}) with role {Role} and no commune assignment.",
            user.UserId, user.Username, ContractEnum.ToDbValue(user.Role));

        return RegisterOutcome.Created(user);
    }

    /// <summary>
    /// The role a self-registered account receives. Deliberately the narrowest of the four: a field
    /// crew member can only FILE faults, which an engineer still has to approve. An engineer could
    /// reject genuine faults and hide real outages; a managing authority sees across communes.
    /// </summary>
    public const UserRole LowestRole = UserRole.FieldCrew;

    /// <summary>PostgreSQL SQLSTATE 23505 — unique_violation.</summary>
    private static bool IsUniqueViolation(DbUpdateException exception)
        => exception.InnerException is PostgresException { SqlState: "23505" };

    public async Task<AuthResult> RefreshAsync(string refreshToken, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return AuthResult.Fail(AuthFailure.InvalidRefreshToken);
        }

        var hash = RefreshTokenGenerator.Hash(refreshToken);
        var now = timeProvider.GetUtcNow().UtcDateTime;

        var existing = await dbContext.Set<RefreshToken>()
            .Include(token => token.User)
            .FirstOrDefaultAsync(token => token.TokenHash == hash, ct);

        if (existing is null)
        {
            return AuthResult.Fail(AuthFailure.InvalidRefreshToken);
        }

        if (existing.RevokedAt is not null)
        {
            await HandleRevokedTokenAsync(existing, now, ct);
            return AuthResult.Fail(AuthFailure.InvalidRefreshToken);
        }

        if (existing.ExpiresAt <= now || existing.ChainAbsoluteExpiry <= now)
        {
            return AuthResult.Fail(AuthFailure.InvalidRefreshToken);
        }

        if (existing.User.IsLocked)
        {
            return AuthResult.Fail(AuthFailure.AccountLocked);
        }

        return await RotateAsync(existing, now, ct);
    }

    public async Task LogoutAsync(string? refreshToken, CancellationToken ct = default)
    {
        // Idempotent: an unknown or already-revoked token is not an error, and NEVER triggers theft
        // detection — an old client retrying is ordinary behaviour.
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return;
        }

        var hash = RefreshTokenGenerator.Hash(refreshToken);
        var now = timeProvider.GetUtcNow().UtcDateTime;

        await dbContext.Set<RefreshToken>()
            .Where(token => token.TokenHash == hash && token.RevokedAt == null)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(token => token.RevokedAt, now)
                    .SetProperty(token => token.RevokedReason, RefreshTokenRevocationReason.Logout),
                ct);
    }

    /// <summary>
    /// Rotates inside ONE transaction. Concurrent refreshes are settled by a conditional UPDATE plus a
    /// row count: under READ COMMITTED the second request blocks until the first commits, then
    /// re-evaluates the predicate against the NEW row and matches 0 rows.
    /// The loser returns 401 and NEVER touches the token the winner just issued.
    /// </summary>
    private async Task<AuthResult> RotateAsync(RefreshToken current, DateTime now, CancellationToken ct)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(ct);

        var claimed = await dbContext.Set<RefreshToken>()
            .Where(token => token.Id == current.Id && token.RevokedAt == null)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(token => token.RevokedAt, now)
                    .SetProperty(token => token.RevokedReason, RefreshTokenRevocationReason.Rotation),
                ct);

        if (claimed == 0)
        {
            // Another request already claimed this token. Issue nothing, revoke nothing.
            await transaction.RollbackAsync(ct);
            logger.LogInformation(
                "Concurrent refresh on token {TokenId}: this request lost, no new token issued.",
                current.Id);
            return AuthResult.Fail(AuthFailure.InvalidRefreshToken);
        }

        var issued = await IssueRefreshTokenAsync(
            current.UserId, current.ChainId, current.ChainAbsoluteExpiry, now, ct);
        await dbContext.SaveChangesAsync(ct);

        await dbContext.Set<RefreshToken>()
            .Where(token => token.Id == current.Id)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(token => token.ReplacedByTokenId, issued.Entity.Id),
                ct);

        await transaction.CommitAsync(ct);

        return AuthResult.Success(await BuildTokensAsync(current.User, issued.RawToken, ct));
    }

    private async Task HandleRevokedTokenAsync(RefreshToken token, DateTime now, CancellationToken ct)
    {
        if (token.RevokedReason != RefreshTokenRevocationReason.Rotation)
        {
            // Logout: an old client retrying, never an attack, no matter how long ago.
            // ReuseDetected: the chain is already dead, nothing left to revoke.
            return;
        }

        var sinceRevoked = now - token.RevokedAt!.Value;
        if (sinceRevoked <= options.ReuseGraceWindow)
        {
            // A benign retry on a weak connection. Revoking the chain here would kill the valid
            // session the winning request just created.
            logger.LogInformation(
                "Token {TokenId} replayed after {Seconds:0.0}s — inside the grace window, ignoring.",
                token.Id, sinceRevoked.TotalSeconds);
            return;
        }

        logger.LogWarning(
            "Token {TokenId} replayed after {Seconds:0.0}s — revoking chain {ChainId}.",
            token.Id, sinceRevoked.TotalSeconds, token.ChainId);

        // Only the chain containing this token. The user's other chains are untouched.
        await dbContext.Set<RefreshToken>()
            .Where(other => other.ChainId == token.ChainId && other.RevokedAt == null)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(other => other.RevokedAt, now)
                    .SetProperty(other => other.RevokedReason, RefreshTokenRevocationReason.ReuseDetected),
                ct);
    }

    private async Task<(RefreshToken Entity, string RawToken)> IssueRefreshTokenAsync(
        string userId, Guid chainId, DateTime chainAbsoluteExpiry, DateTime now, CancellationToken ct)
    {
        var raw = RefreshTokenGenerator.CreateRawToken();

        // Slides 30 days forward but never past the chain's absolute ceiling.
        var sliding = now.AddDays(options.RefreshSlidingDays);
        var expiresAt = sliding < chainAbsoluteExpiry ? sliding : chainAbsoluteExpiry;

        var entity = new RefreshToken
        {
            UserId = userId,
            ChainId = chainId,
            ChainAbsoluteExpiry = chainAbsoluteExpiry,
            TokenHash = RefreshTokenGenerator.Hash(raw),
            ExpiresAt = expiresAt,
            CreatedAt = now,
        };

        await dbContext.Set<RefreshToken>().AddAsync(entity, ct);
        return (entity, raw);
    }

    private async Task<AuthTokens> BuildTokensAsync(AppUser user, string rawRefreshToken, CancellationToken ct)
    {
        var communeIds = user.HasSystemWideScope
            ? [AuthClaims.AllCommunes]
            : await dbContext.Set<AppUserCommune>()
                .Where(assignment => assignment.UserId == user.UserId)
                .OrderBy(assignment => assignment.CommuneId)
                .Select(assignment => assignment.CommuneId)
                .ToArrayAsync(ct);

        var access = accessTokenIssuer.Issue(user, communeIds);
        return new AuthTokens(access.Token, rawRefreshToken, access.ExpiresInSeconds);
    }
}
