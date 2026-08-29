using LuxMap.Modules.Identity.Entities;
using LuxMap.Persistence;
using LuxMap.Shared.Contracts.Enums;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LuxMap.Modules.Identity.Seeding;

/// <summary>
/// Dữ liệu nền tối thiểu để BE-07 và FE có tài khoản đăng nhập thử.
/// <para>
/// Chạy lại bao nhiêu lần cũng không tạo trùng: mỗi bản ghi được nhận diện bằng KHOÁ TỰ NHIÊN
/// (tên xã, username) chứ không phải ID. Nhờ vậy ID vẫn do sequence sinh đúng quy ước
/// Contract mục 0.4, không phải cấy ID cứng rồi lệch sequence.
/// </para>
/// </summary>
public sealed class IdentitySeeder(
    LuxMapDbContext dbContext,
    ILogger<IdentitySeeder> logger)
{
    /// <summary>Thuật toán ghi vào cột <c>password_algorithm</c>, xem <see cref="AppUser.PasswordAlgorithm"/>.</summary>
    public const string PasswordAlgorithm = "pbkdf2-aspnetcore-v3";

    /// <summary>
    /// Tên tạm. Không mock nào và Contract cũng không cho biết tên xã thật; BE-39 seed bộ mock
    /// FO-26 sẽ đặt tên đúng.
    /// </summary>
    private const string DefaultCommuneName = "Xã 01";

    public async Task SeedAsync(SeedCredentials credentials, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(credentials);

        var commune = await EnsureCommuneAsync(DefaultCommuneName, cancellationToken);
        await EnsureUsersAsync(credentials, commune, cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Seed Identity xong.");
    }

    private async Task<AdministrativeUnit> EnsureCommuneAsync(string name, CancellationToken cancellationToken)
    {
        var existing = await dbContext.Set<AdministrativeUnit>()
            .FirstOrDefaultAsync(unit => unit.Name == name, cancellationToken);

        if (existing is not null)
        {
            logger.LogInformation("Xã {Name} đã có ({CommuneId}), bỏ qua.", name, existing.CommuneId);
            return existing;
        }

        var commune = new AdministrativeUnit { Name = name };
        dbContext.Set<AdministrativeUnit>().Add(commune);

        // Lưu ngay để lấy commune_id do DB sinh, dùng cho bảng nối bên dưới.
        await dbContext.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Đã tạo xã {Name} với id {CommuneId}.", name, commune.CommuneId);

        return commune;
    }

    private async Task EnsureUsersAsync(
        SeedCredentials credentials,
        AdministrativeUnit commune,
        CancellationToken cancellationToken)
    {
        var hasher = new PasswordHasher<AppUser>();

        foreach (var template in SeedUsers.All)
        {
            var exists = await dbContext.Set<AppUser>()
                .AnyAsync(user => user.Username == template.Username, cancellationToken);

            if (exists)
            {
                logger.LogInformation("Tài khoản {Username} đã có, bỏ qua.", template.Username);
                continue;
            }

            var user = new AppUser
            {
                Username = template.Username,
                Email = template.Email,
                FullName = template.FullName,
                Role = template.Role,
                HasSystemWideScope = template.Role == UserRole.Administrator,
                PasswordHash = string.Empty,
                PasswordAlgorithm = PasswordAlgorithm,
            };

            user.PasswordHash = hasher.HashPassword(user, credentials.PasswordFor(template.Role));
            dbContext.Set<AppUser>().Add(user);
            await dbContext.SaveChangesAsync(cancellationToken);

            // Quản trị có phạm vi toàn hệ thống qua cờ HasSystemWideScope (Contract mục 7),
            // không gán từng xã — thêm xã mới sẽ không phải sửa gì.
            if (!user.HasSystemWideScope)
            {
                dbContext.Set<AppUserCommune>().Add(new AppUserCommune
                {
                    UserId = user.UserId,
                    CommuneId = commune.CommuneId,
                });
            }

            logger.LogInformation(
                "Đã tạo {Username} ({UserId}) vai trò {Role}.", user.Username, user.UserId, user.Role);
        }
    }
}

/// <param name="Username">Khoá tự nhiên để seed idempotent.</param>
public sealed record SeedUser(string Username, string Email, string FullName, UserRole Role);

public static class SeedUsers
{
    /// <summary>Một tài khoản cho mỗi vai trò. Thứ tự cố định để USR-001..USR-004 ổn định.</summary>
    public static IReadOnlyList<SeedUser> All { get; } =
    [
        new("admin", "admin@luxmap.local", "Quản trị hệ thống", UserRole.Administrator),
        new("agency", "agency@luxmap.local", "Cán bộ cơ quan quản lý", UserRole.ManagementAgency),
        new("engineer", "engineer@luxmap.local", "Kỹ sư bảo trì", UserRole.MaintenanceEngineer),
        new("crew", "crew@luxmap.local", "Tổ khảo sát và sửa chữa", UserRole.FieldCrew),
    ];
}
