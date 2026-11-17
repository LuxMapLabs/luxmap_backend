using LuxMap.Persistence;
using LuxMap.Shared.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace LuxMap.Api.Tests;

[Collection(nameof(AssetDatabaseCollection))]
public sealed class PolePhotoWithdrawalMigrationTests(AssetImportFixture fixture)
{
    [Fact]
    public async Task Withdrawal_schema_has_nullable_columns_restrict_FK_and_index_and_rolls_back_symmetrically()
    {
        await using var container = new PostgreSqlBuilder(TestDatabase.Image)
            .WithDatabase("photo_withdrawal_migration_test").WithUsername("migration_test")
            .WithPassword(System.Security.Cryptography.RandomNumberGenerator.GetHexString(32)).Build();
        await container.StartAsync();
        await using var source = PersistenceServiceCollectionExtensions.BuildDataSource(container.GetConnectionString());
        var options = new DbContextOptionsBuilder<LuxMapDbContext>();
        PersistenceServiceCollectionExtensions.Configure(options, source);
        await using var db = new LuxMapDbContext(options.Options, fixture.Services.GetRequiredService<ModuleAssemblyCatalog>(),
            fixture.Services.GetRequiredService<ICommuneScopeAccessor>());
        var migrator = db.GetService<IMigrator>();
        var migrations = db.Database.GetMigrations().ToArray();
        var index = Array.FindIndex(migrations, name => name.EndsWith("_AddPolePhotoWithdrawal", StringComparison.Ordinal));
        Assert.True(index > 0);
        await migrator.MigrateAsync(migrations[index]);
        Assert.False(db.Database.HasPendingModelChanges());
        Assert.Equal(2, await db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM information_schema.columns WHERE table_name = 'work_order_pole_photo' AND column_name IN ('withdrawn_at','withdrawn_by') AND is_nullable = 'YES' AND column_default IS NULL").SingleAsync());
        Assert.Equal("r", await db.Database.SqlQuery<string>($"SELECT confdeltype::text AS \"Value\" FROM pg_constraint WHERE conname = 'fk_pole_photo_withdrawn_by'").SingleAsync());
        Assert.Equal(1, await db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM pg_constraint WHERE conname = 'ck_pole_photo_withdrawal'").SingleAsync());
        Assert.Equal(1, await db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM pg_indexes WHERE indexname = 'ix_pole_photo_withdrawn_by' AND indexdef NOT LIKE '%WHERE%'").SingleAsync());
        await migrator.MigrateAsync(migrations[index - 1]);
        Assert.Equal(0, await db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM information_schema.columns WHERE table_name = 'work_order_pole_photo' AND column_name IN ('withdrawn_at','withdrawn_by')").SingleAsync());
        Assert.Equal(0, await db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM pg_constraint WHERE conname IN ('fk_pole_photo_withdrawn_by','ck_pole_photo_withdrawal')").SingleAsync());
        Assert.Equal(0, await db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM pg_indexes WHERE indexname = 'ix_pole_photo_withdrawn_by'").SingleAsync());
        await migrator.MigrateAsync();
        Assert.False(db.Database.HasPendingModelChanges());
    }
}
