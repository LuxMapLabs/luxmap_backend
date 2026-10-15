using LuxMap.Persistence;
using LuxMap.Shared.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace LuxMap.Api.Tests;

[Collection(nameof(AssetDatabaseCollection))]
public sealed class PolePhotoMigrationTests(AssetImportFixture fixture)
{
    [Fact]
    public async Task Photo_schema_applies_rolls_back_and_reapplies_on_its_own_empty_container()
    {
        await using var container = new PostgreSqlBuilder(TestDatabase.Image)
            .WithDatabase("pole_photo_migration_test").WithUsername("migration_test")
            .WithPassword(System.Security.Cryptography.RandomNumberGenerator.GetHexString(32)).Build();
        await container.StartAsync();
        await using var source = PersistenceServiceCollectionExtensions.BuildDataSource(container.GetConnectionString());
        var options = new DbContextOptionsBuilder<LuxMapDbContext>();
        PersistenceServiceCollectionExtensions.Configure(options, source);
        await using var db = new LuxMapDbContext(options.Options, fixture.Services.GetRequiredService<ModuleAssemblyCatalog>(),
            fixture.Services.GetRequiredService<ICommuneScopeAccessor>());
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync();
        Assert.Contains("20261015153829_AddWorkOrderPolePhotos", await db.Database.GetAppliedMigrationsAsync());
        await migrator.MigrateAsync("20261014095347_CommuneMapFrame");
        Assert.Null(await db.Database.SqlQuery<string>($"SELECT to_regclass('work_order_pole_photo')::text AS \"Value\"").SingleOrDefaultAsync());
        await migrator.MigrateAsync();
        Assert.Equal(1, await db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM pg_constraint WHERE conname = 'fk_pole_photo_same_commune'").SingleAsync());
    }
}
