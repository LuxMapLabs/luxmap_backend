using LuxMap.Modules.Assets.Entities;
using LuxMap.Persistence;
using LuxMap.Persistence.Audit;
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

/// <summary>
/// ASSET-SEGMENT-LENGTH-NULL — <c>road_segment.length_m</c> is nullable for a segment with no route. The rollback must refuse while any
/// segment has no length (the generated <c>defaultValue: 0</c> would invent one), and succeed once none is left.
/// </summary>
[Collection(nameof(AssetDatabaseCollection))]
public sealed class SegmentLengthNullableMigrationTests(AssetImportFixture fixture)
{
    [Fact]
    public async Task Nullable_segment_length_rollback_refuses_while_a_length_is_missing_and_never_invents_one()
    {
        await using var container = new PostgreSqlBuilder(TestDatabase.Image)
            .WithDatabase("segment_length_nullable_test").WithUsername("migration_test")
            .WithPassword(System.Security.Cryptography.RandomNumberGenerator.GetHexString(32)).Build();
        await container.StartAsync();
        await using var source = PersistenceServiceCollectionExtensions.BuildDataSource(container.GetConnectionString());
        var options = new DbContextOptionsBuilder<LuxMapDbContext>();
        PersistenceServiceCollectionExtensions.Configure(options, source);
        await using var db = new LuxMapDbContext(options.Options, fixture.Services.GetRequiredService<ModuleAssemblyCatalog>(),
            fixture.Services.GetRequiredService<ICommuneScopeAccessor>());
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync();
        var migration = (await db.Database.GetAppliedMigrationsAsync()).Single(id => id.EndsWith("_RoadSegmentLengthNullable", StringComparison.Ordinal));
        const string previous = "20261111135148_RoadSegmentGeomNullable";
        Assert.False(db.Database.HasPendingModelChanges());
        await AssertNullable(db, "YES");

        using (db.EnterUnscopedSystemWriteBackdoor())
        {
            var commune = new AdministrativeUnit { Name = "segment-length-null rollback" };
            db.Add(commune);
            await db.SaveChangesAsync();
            var road = new RoadSegment { CommuneId = commune.CommuneId, SegmentName = "unmeasured", RoadClass = RoadClass.InterVillage,
                DataSource = DataSource.Simulated, LengthM = null };
            db.Add(road);
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            var error = await Assert.ThrowsAsync<PostgresException>(() => migrator.MigrateAsync(previous));
            Assert.Equal(PostgresErrorCodes.RaiseException, error.SqlState);
            Assert.Contains("Cannot roll back RoadSegmentLengthNullable: 1 segment(s)", error.MessageText);
            Assert.Contains(migration, await db.Database.GetAppliedMigrationsAsync());
            await AssertNullable(db, "YES");
            var preserved = await db.Set<RoadSegment>().IgnoreQueryFilters().SingleAsync(s => s.SegmentId == road.SegmentId);
            Assert.Null(preserved.LengthM);

            preserved.Geom = new LineString([new Coordinate(108, 16), new Coordinate(108.01, 16)]) { SRID = 4326 };
            preserved.LengthM = 1100;
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
            await migrator.MigrateAsync(previous);
            Assert.DoesNotContain(migration, await db.Database.GetAppliedMigrationsAsync());
            await AssertNullable(db, "NO");
            await migrator.MigrateAsync();
            await AssertNullable(db, "YES");
            Assert.False(db.Database.HasPendingModelChanges());
        }
    }

    private static async Task AssertNullable(LuxMapDbContext db, string nullable)
        => Assert.Equal(nullable, await db.Database.SqlQuery<string>($"SELECT is_nullable AS \"Value\" FROM information_schema.columns WHERE table_schema = 'public' AND table_name = 'road_segment' AND column_name = 'length_m'").SingleAsync());
}
