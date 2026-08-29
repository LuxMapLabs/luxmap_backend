using LuxMap.Modules.Identity.Seeding;
using LuxMap.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LuxMap.Api.Seeding;

/// <summary>
/// Chạy bằng <c>dotnet run --project src/LuxMap.Api -- --seed</c>. Tách khỏi đường khởi động
/// bình thường: seed là thao tác ghi, không nên chạy ngầm mỗi lần ai đó bật app lên.
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
                "Còn {Count} migration chưa apply. Chạy `dotnet ef database update` trước khi seed.",
                pending.Length);
            return 1;
        }

        var credentials = SeedCredentials.FromEnvironment();
        await scope.ServiceProvider.GetRequiredService<IdentitySeeder>().SeedAsync(credentials);

        return 0;
    }
}
