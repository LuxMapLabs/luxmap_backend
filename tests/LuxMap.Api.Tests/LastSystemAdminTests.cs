using LuxMap.Modules.Identity.Accounts;
using LuxMap.Modules.Identity.Entities;
using LuxMap.Persistence;
using LuxMap.Shared.Contracts.Enums;
using LuxMap.Shared.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LuxMap.Api.Tests;

/// <summary>
/// Runs ALONE, after every parallel collection: the claim "no other active system admin exists" holds only while nobody else writes
/// one. Under READ COMMITTED the check sees every admin committed after this test locked the others — and
/// <c>SystemMonitoringTests.One_unreachable_admin_…</c> commits an active one from another collection (CI run 39919424341, 23/10/2026).
/// </summary>
[CollectionDefinition(nameof(SystemAdminCountCollection), DisableParallelization = true)]
public sealed class SystemAdminCountCollection : ICollectionFixture<AccountTestFactory>;

[Collection(nameof(SystemAdminCountCollection))]
public sealed class LastSystemAdminTests(AccountTestFactory factory)
{
    /// <summary>
    /// Run against the service inside a transaction that is rolled back: the other admins are locked
    /// only where this transaction can see it, so no concurrent test ever meets an admin-less database.
    /// </summary>
    [Fact]
    public async Task The_last_active_system_admin_can_be_neither_demoted_nor_locked()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LuxMapDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<UserAdminService>();
        await using var transaction = await db.Database.BeginTransactionAsync();

        await db.Database.ExecuteSqlRawAsync("UPDATE app_user SET is_locked = true WHERE role = 'system_admin' AND username <> 'admin'");
        var adminId = await db.Set<AppUser>().Where(u => u.Username == "admin").Select(u => u.UserId).SingleAsync();

        var demote = await Assert.ThrowsAsync<LuxMapException>(() =>
            service.UpdateAsync(adminId, new UpdateUserRequest { Role = UserRole.Manager, CommuneIds = [] }, CancellationToken.None));
        var lockIt = await Assert.ThrowsAsync<LuxMapException>(() => service.LockAsync(adminId, CancellationToken.None));

        Assert.Equal(AccountErrors.LastSystemAdmin, demote.Code);
        Assert.Equal(AccountErrors.LastSystemAdmin, lockIt.Code);
        await transaction.RollbackAsync();
    }
}
