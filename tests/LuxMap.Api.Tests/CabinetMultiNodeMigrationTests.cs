using LuxMap.Modules.Assets.Entities;
using LuxMap.Modules.Telemetry.Entities;
using LuxMap.Persistence;
using LuxMap.Shared.Authorization;
using LuxMap.Shared.Contracts.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using NetTopologySuite.Geometries;
using Npgsql;
using Testcontainers.PostgreSql;

namespace LuxMap.Api.Tests;

[Collection(nameof(AssetDatabaseCollection))]
public sealed class CabinetMultiNodeMigrationTests(AssetImportFixture fixture)
{
    [Fact]
    public async Task The_migration_preserves_devices_and_refuses_a_rollback_until_cabinets_have_at_most_one_node()
    {
        const string previous = "20261114120000_UniqueIotSourceFeeder";
        const string migration = "20261114171551_AllowMultipleIotNodesPerCabinet";
        await using var container = new PostgreSqlBuilder(TestDatabase.Image)
            .WithDatabase("cabinet_multi_node_migration_test").WithUsername("migration_test")
            .WithPassword(System.Security.Cryptography.RandomNumberGenerator.GetHexString(32)).Build();
        await container.StartAsync();
        await using var source = PersistenceServiceCollectionExtensions.BuildDataSource(container.GetConnectionString());
        var options = new DbContextOptionsBuilder<LuxMapDbContext>();
        PersistenceServiceCollectionExtensions.Configure(options, source);
        await using var db = new LuxMapDbContext(options.Options, fixture.Services.GetRequiredService<ModuleAssemblyCatalog>(),
            fixture.Services.GetRequiredService<ICommuneScopeAccessor>());
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(previous);

        using (db.EnterUnscopedSystemWriteBackdoor())
        {
            var commune = new AdministrativeUnit { Name = "Cabinet multi-node migration" };
            db.Add(commune);
            await db.SaveChangesAsync();
            var cabinet = new ElectricalCabinet
            {
                CabinetName = "Migration cabinet", CommuneId = commune.CommuneId, DataSource = DataSource.Simulated,
                Geom = new Point(108.8, 12.8) { SRID = 4326 },
            };
            db.Add(cabinet);
            await db.SaveChangesAsync();
            var original = new IotNode { CabinetId = cabinet.CabinetId, CommuneId = commune.CommuneId, DataSource = DataSource.Simulated };
            db.Add(original);
            await db.SaveChangesAsync();

            await migrator.MigrateAsync();
            Assert.False(db.Database.HasPendingModelChanges());
            Assert.Contains(migration, await db.Database.GetAppliedMigrationsAsync());
            await AssertCabinetIndexAsync(db, unique: false);
            Assert.Equal(original.NodeId, await db.Set<IotNode>().IgnoreQueryFilters().AsNoTracking().Select(node => node.NodeId).SingleAsync());

            await migrator.MigrateAsync(previous);
            await AssertCabinetIndexAsync(db, unique: true);
            await migrator.MigrateAsync();

            var second = new IotNode { CabinetId = cabinet.CabinetId, CommuneId = commune.CommuneId, DataSource = DataSource.Simulated };
            db.Add(second);
            await db.SaveChangesAsync();
            var error = await Assert.ThrowsAsync<PostgresException>(() => migrator.MigrateAsync(previous));
            Assert.Contains("Cannot roll back AllowMultipleIotNodesPerCabinet", error.MessageText);
            Assert.Contains(migration, await db.Database.GetAppliedMigrationsAsync());
            Assert.Equal(2, await db.Set<IotNode>().IgnoreQueryFilters().CountAsync());
            await AssertCabinetIndexAsync(db, unique: false);

            db.Remove(second);
            await db.SaveChangesAsync();
            await migrator.MigrateAsync(previous);
            await AssertCabinetIndexAsync(db, unique: true);
            Assert.Equal(original.NodeId, await db.Set<IotNode>().IgnoreQueryFilters().AsNoTracking().Select(node => node.NodeId).SingleAsync());
            await migrator.MigrateAsync();
            await AssertCabinetIndexAsync(db, unique: false);
        }
    }

    private static async Task AssertCabinetIndexAsync(LuxMapDbContext db, bool unique)
    {
        Assert.Equal(1, await db.Database.SqlQuery<int>(
            $"SELECT count(*)::int AS \"Value\" FROM pg_indexes WHERE indexname = 'ux_iot_source_registration_feeder_id' AND indexdef LIKE 'CREATE UNIQUE INDEX%'").SingleAsync());
        var name = unique ? "ux_iot_node_cabinet_id" : "ix_iot_node_cabinet_id";
        Assert.Equal(unique, await db.Database.SqlQuery<bool>(
            $"SELECT indisunique AS \"Value\" FROM pg_index WHERE indexrelid = to_regclass({name})").SingleAsync());
        var removed = unique ? "ix_iot_node_cabinet_id" : "ux_iot_node_cabinet_id";
        Assert.Null(await db.Database.SqlQuery<string>($"SELECT to_regclass({removed})::text AS \"Value\"").SingleOrDefaultAsync());
    }
}
