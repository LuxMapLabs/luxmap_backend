using LuxMap.Shared.Contracts.Enums;

namespace LuxMap.Modules.Identity.Seeding;

/// <summary>
/// Passwords for the seeded accounts. Read from environment variables (<c>.env</c>), NEVER hardcoded —
/// this source is shared across the team and lives on GitHub.
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
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, "No seed password variable for this role."),
    };

    /// <summary>Any missing variable stops seeding with a clear message rather than silently defaulting.</summary>
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
                $"Missing environment variables for seed passwords: {string.Join(", ", missing)}. "
                + "Run `cp .env.example .env` at the repository root and set real values.");
        }

        return new SeedCredentials(resolved);
    }

    public string PasswordFor(UserRole role) => passwords[role];
}
