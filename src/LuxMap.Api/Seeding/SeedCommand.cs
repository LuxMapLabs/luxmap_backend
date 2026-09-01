using LuxMap.Modules.Identity.Seeding;
using LuxMap.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LuxMap.Api.Seeding;

/// <summary>
/// Invoked with <c>dotnet run --project src/LuxMap.Api -- --seed</c>. Kept off the normal startup
/// path: seeding writes to the database and should not run silently every time someone starts the app.
/// </summary>
public static class SeedCommand
{
    public const string Argument = "--seed";

    public static bool IsRequested(string[] args) => args.Contains(Argument, StringComparer.Ordinal);

    public static async Task<int> RunAsync(WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        await using var scope = app.Services.CreateAsyncScope();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Seed");

        var dbContext = scope.ServiceProvider.GetRequiredService<LuxMapDbContext>();
        var pending = (await dbContext.Database.GetPendingMigrationsAsync()).ToArray();
        if (pending.Length > 0)
        {
            logger.LogError(
                "{Count} migration(s) are still pending. Run `dotnet ef database update` before seeding.",
                pending.Length);
            return 1;
        }

        var credentials = SeedCredentials.FromEnvironment();
        await scope.ServiceProvider.GetRequiredService<IdentitySeeder>().SeedAsync(credentials);

        return 0;
    }
}
