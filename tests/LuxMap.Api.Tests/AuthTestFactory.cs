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
/// Host thật cho nhóm test BE-07, có <see cref="FakeTimeProvider"/> thay cho đồng hồ hệ thống
/// để tua thời gian qua cửa sổ ân hạn 30 giây mà không phải chờ thật.
/// </summary>
public sealed class AuthTestFactory : WebApplicationFactory<Program>
{
    public FakeTimeProvider Clock { get; } = new(DateTimeOffset.UtcNow);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Production");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
        });
    }

    /// <summary>Truy vấn thẳng database — kiểm trạng thái THẬT của bản ghi, không tin service.</summary>
    public async Task<T> QueryAsync<T>(Func<LuxMapDbContext, Task<T>> query)
    {
        await using var scope = Services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<LuxMapDbContext>());
    }

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
