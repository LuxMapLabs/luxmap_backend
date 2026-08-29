using LuxMap.Shared.Contracts.Enums;

namespace LuxMap.Modules.Identity.Seeding;

/// <summary>
/// Mật khẩu của tài khoản seed. Đọc từ biến môi trường (<c>.env</c>), KHÔNG hard-code trong mã —
/// mã nguồn là public trong nhóm và sẽ lên GitHub.
/// </summary>
public sealed class SeedCredentials
{
    private readonly IReadOnlyDictionary<UserRole, string> passwords;

    private SeedCredentials(IReadOnlyDictionary<UserRole, string> passwords)
        => this.passwords = passwords;

    public static string EnvironmentVariableFor(UserRole role) => role switch
    {
        UserRole.Administrator => "SEED_ADMIN_PASSWORD",
        UserRole.ManagementAgency => "SEED_AGENCY_PASSWORD",
        UserRole.MaintenanceEngineer => "SEED_ENGINEER_PASSWORD",
        UserRole.FieldCrew => "SEED_CREW_PASSWORD",
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, "Vai trò chưa có biến mật khẩu seed."),
    };

    /// <summary>Thiếu biến nào thì dừng hẳn với thông điệp rõ, không lặng lẽ đặt mật khẩu mặc định.</summary>
    public static SeedCredentials FromEnvironment()
    {
        var resolved = new Dictionary<UserRole, string>();
        var missing = new List<string>();

        foreach (var role in Enum.GetValues<UserRole>())
        {
            var variable = EnvironmentVariableFor(role);
            var value = Environment.GetEnvironmentVariable(variable);

            if (string.IsNullOrWhiteSpace(value))
            {
                missing.Add(variable);
                continue;
            }

            resolved[role] = value;
        }

        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                $"Thiếu biến môi trường cho mật khẩu seed: {string.Join(", ", missing)}. "
                + "Chạy `cp .env.example .env` ở thư mục gốc repo rồi đặt giá trị thật.");
        }

        return new SeedCredentials(resolved);
    }

    public string PasswordFor(UserRole role) => passwords[role];
}
