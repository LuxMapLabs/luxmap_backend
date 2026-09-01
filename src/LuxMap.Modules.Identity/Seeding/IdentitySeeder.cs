using LuxMap.Modules.Identity.Entities;
using LuxMap.Persistence;
using LuxMap.Shared.Contracts.Enums;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LuxMap.Modules.Identity.Seeding;

/// <summary>
/// The minimum baseline data so BE-07 and the front end have accounts to sign in with.
/// <para>
/// Re-running it never creates duplicates: every record is identified by its NATURAL KEY (commune
/// name, username) rather than by ID. That keeps IDs coming from the sequence exactly as Contract
/// section 0.4 requires, instead of hardcoding IDs and desynchronising the sequence.
/// </para>
/// </summary>
public sealed class IdentitySeeder(
    LuxMapDbContext dbContext,
    ILogger<IdentitySeeder> logger)
{
    /// <summary>Recorded in the <c>password_algorithm</c> column; see <see cref="AppUser.PasswordAlgorithm"/>.</summary>
    public const string PasswordAlgorithm = "pbkdf2-aspnetcore-v3";

    /// <summary>
    /// A placeholder. Neither the mocks nor the Contract give a real commune name; BE-39, which seeds
    /// the FO-26 mock set, will set the correct one.
    /// </summary>
    private const string DefaultCommuneName = "Commune 01";

    public async Task SeedAsync(SeedCredentials credentials, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(credentials);

        var commune = await EnsureCommuneAsync(DefaultCommuneName, cancellationToken);
        await EnsureUsersAsync(credentials, commune, cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Identity seeding complete.");
    }

    private async Task<AdministrativeUnit> EnsureCommuneAsync(string name, CancellationToken cancellationToken)
    {
        var existing = await dbContext.Set<AdministrativeUnit>()
            .FirstOrDefaultAsync(unit => unit.Name == name, cancellationToken);

        if (existing is not null)
        {
            logger.LogInformation("Commune {Name} already exists ({CommuneId}), skipping.", name, existing.CommuneId);
            return existing;
        }

        var commune = new AdministrativeUnit { Name = name };
        dbContext.Set<AdministrativeUnit>().Add(commune);

        // Save immediately to obtain the database-generated commune_id for the join table below.
        await dbContext.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Created commune {Name} with id {CommuneId}.", name, commune.CommuneId);

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
                logger.LogInformation("Account {Username} already exists, skipping.", template.Username);
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

            // Administrators get system-wide scope through the HasSystemWideScope flag (Contract
            // section 7) rather than per-commune rows — adding a commune then requires no change here.
            if (!user.HasSystemWideScope)
            {
                dbContext.Set<AppUserCommune>().Add(new AppUserCommune
                {
                    UserId = user.UserId,
                    CommuneId = commune.CommuneId,
                });
            }

            logger.LogInformation(
                "Created {Username} ({UserId}) with role {Role}.", user.Username, user.UserId, user.Role);
        }
    }
}

/// <param name="Username">The natural key that keeps seeding idempotent.</param>
public sealed record SeedUser(string Username, string Email, string FullName, UserRole Role);

public static class SeedUsers
{
    /// <summary>One account per role. The order is fixed so USR-001..USR-004 stay stable.</summary>
    public static IReadOnlyList<SeedUser> All { get; } =
    [
        new("admin", "admin@luxmap.local", "System Administrator", UserRole.Administrator),
        new("agency", "agency@luxmap.local", "Managing Authority Officer", UserRole.ManagementAgency),
        new("engineer", "engineer@luxmap.local", "Maintenance Engineer", UserRole.MaintenanceEngineer),
        new("crew", "crew@luxmap.local", "Survey and Repair Crew", UserRole.FieldCrew),
    ];
}
