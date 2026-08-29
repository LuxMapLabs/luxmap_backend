using LuxMap.Modules.Identity.Entities;
using LuxMap.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
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

        // BE-06 không chuẩn hoá hoa thường khi lưu, nên tra cứu phải tự hạ chữ.
        var user = await dbContext.Set<AppUser>()
            .FirstOrDefaultAsync(u => u.Username.ToLower() == normalized.ToLower(), ct);

        if (user is null)
        {
            logger.LogWarning("Đăng nhập thất bại: không có tài khoản khớp.");
            return AuthResult.Fail(AuthFailure.InvalidCredentials);
        }

        var verification = passwordHasher.VerifyHashedPassword(user, user.PasswordHash, password ?? string.Empty);
        if (verification == PasswordVerificationResult.Failed)
        {
            logger.LogWarning("Đăng nhập thất bại cho {UserId}: sai mật khẩu.", user.UserId);
            return AuthResult.Fail(AuthFailure.InvalidCredentials);
        }

        // Kiểm khoá SAU khi xác minh mật khẩu: trả 403 trước đó sẽ tiết lộ tài khoản tồn tại.
        if (user.IsLocked)
        {
            logger.LogWarning("Đăng nhập bị chặn cho {UserId}: tài khoản đang khoá.", user.UserId);
            return AuthResult.Fail(AuthFailure.AccountLocked);
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;

        // Mỗi lần đăng nhập mở MỘT chuỗi mới, nên thu hồi chuỗi này không đụng thiết bị khác.
        var chainId = Guid.NewGuid();
        var chainAbsoluteExpiry = now.AddDays(options.RefreshAbsoluteDays);

        var refresh = await IssueRefreshTokenAsync(user.UserId, chainId, chainAbsoluteExpiry, now, ct);
        await dbContext.SaveChangesAsync(ct);

        logger.LogInformation("Đăng nhập thành công {UserId}, mở chuỗi {ChainId}.", user.UserId, chainId);
        return AuthResult.Success(await BuildTokensAsync(user, refresh.RawToken, ct));
    }

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
        // Idempotent: token không tồn tại hoặc đã thu hồi cũng không phải lỗi, và KHÔNG bao giờ
        // kích hoạt phát hiện đánh cắp — client cũ retry là chuyện bình thường.
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
    /// Xoay vòng trong MỘT transaction. Chống hai request đồng thời bằng UPDATE có điều kiện
    /// rồi đếm số dòng: ở READ COMMITTED, request thứ hai bị chặn tới khi request đầu commit,
    /// sau đó đánh giá lại điều kiện trên bản ghi MỚI và khớp 0 dòng.
    /// Request thua trả 401 và KHÔNG đụng gì tới token mà request thắng vừa phát.
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
            // Request khác đã giành được token này. Không phát token mới, không thu hồi thêm gì.
            await transaction.RollbackAsync(ct);
            logger.LogInformation(
                "Refresh đồng thời trên token {TokenId}: request này thua, không phát token mới.",
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
            // Logout: client cũ retry, không phải tấn công, dù bao lâu đi nữa.
            // ReuseDetected: chuỗi đã chết rồi, không cần thu hồi thêm lần nữa.
            return;
        }

        var sinceRevoked = now - token.RevokedAt!.Value;
        if (sinceRevoked <= options.ReuseGraceWindow)
        {
            // Retry lành tính ở vùng sóng yếu. Thu hồi chuỗi lúc này sẽ giết luôn phiên hợp lệ
            // mà request thắng vừa tạo ra.
            logger.LogInformation(
                "Dùng lại token {TokenId} sau {Seconds:0.0}s — trong cửa sổ ân hạn, bỏ qua.",
                token.Id, sinceRevoked.TotalSeconds);
            return;
        }

        logger.LogWarning(
            "Dùng lại token {TokenId} sau {Seconds:0.0}s — thu hồi chuỗi {ChainId}.",
            token.Id, sinceRevoked.TotalSeconds, token.ChainId);

        // Chỉ chuỗi chứa token này. Chuỗi khác của cùng người dùng không bị đụng.
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

        // Trượt 30 ngày nhưng không bao giờ vượt trần tuyệt đối của chuỗi.
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
