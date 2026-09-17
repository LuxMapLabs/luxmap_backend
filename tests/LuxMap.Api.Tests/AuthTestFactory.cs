using LuxMap.Modules.Identity.Entities;
using LuxMap.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;

namespace LuxMap.Api.Tests;

/// <summary>
/// A real host for the BE-07 tests, with <see cref="FakeTimeProvider"/> replacing the system clock so
/// time can be wound past the 30-second grace window without actually waiting.
/// </summary>
public sealed class AuthTestFactory : WebApplicationFactory<Program>
{
    public FakeTimeProvider Clock { get; } = new(DateTimeOffset.UtcNow);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Production");
        builder.UseTestCorsOrigin();
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
        });
    }

    /// <summary>Queries the database directly — inspects the REAL row state instead of trusting the service.</summary>
    public async Task<T> QueryAsync<T>(Func<LuxMapDbContext, Task<T>> query)
    {
        await using var scope = Services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<LuxMapDbContext>());
    }

    /// <summary>
    /// The commune the BE-06 seeder puts the seeded users in, read through its
    /// <see cref="SeedKeys.StudySite"/> key.
    /// </summary>
    /// <remarks>
    /// The id itself comes from a sequence, so it records which run happened to create the row first
    /// — never anything a test may assert against as a constant.
    /// </remarks>
    public Task<string> SeededCommuneIdAsync()
        => QueryAsync(db => db.Set<AdministrativeUnit>()
            .Where(unit => unit.SeedKey == SeedKeys.StudySite)
            .Select(unit => unit.CommuneId)
            .SingleAsync());

    public Task<RefreshToken?> FindTokenAsync(string rawToken)
    {
        var hash = Modules.Identity.Auth.RefreshTokenGenerator.Hash(rawToken);
        return QueryAsync(db => db.Set<RefreshToken>().AsNoTracking()
            .FirstOrDefaultAsync(t => t.TokenHash == hash));
    }

    public Task<List<RefreshToken>> ChainAsync(Guid chainId)
        => QueryAsync(db => db.Set<RefreshToken>().AsNoTracking()
            .Where(t => t.ChainId == chainId).OrderBy(t => t.Id).ToListAsync());
}
