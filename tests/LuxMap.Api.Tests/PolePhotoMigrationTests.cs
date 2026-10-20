using LuxMap.Modules.Assets.Entities;
using LuxMap.Shared.Contracts.Enums;
using LuxMap.Persistence;
using Npgsql;
using NetTopologySuite.Geometries;
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
        Assert.Contains("20261020134307_AddPolePhotoReviews", await db.Database.GetAppliedMigrationsAsync());
        Assert.False(db.Database.HasPendingModelChanges());
        foreach (var name in new[] { "ak_pole_photo_target", "fk_pole_photo_same_commune", "ck_pole_photo_review_complete",
            "ck_pole_photo_review_note", "ck_pole_photo_review_note_required", "fk_pole_photo_reviewed_by" })
            Assert.Equal(1, await db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM pg_constraint WHERE conname = {name}").SingleAsync());
        Assert.Equal("r", await db.Database.SqlQuery<string>($"SELECT confdeltype::text AS \"Value\" FROM pg_constraint WHERE conname = 'fk_pole_photo_reviewed_by'").SingleAsync());
        Assert.Equal(1, await db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM pg_indexes WHERE indexname = 'ix_pole_photo_reviewed_by' AND indexdef NOT LIKE '%WHERE%'").SingleAsync());
        Assert.Equal(4, await db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM information_schema.columns WHERE table_name = 'work_order_pole_photo' AND column_name IN ('final_cv_state','review_note','reviewed_by','reviewed_at') AND is_nullable = 'YES' AND column_default IS NULL").SingleAsync());
        await migrator.MigrateAsync("20261015153829_AddWorkOrderPolePhotos");
        Assert.Equal(0, await db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM information_schema.columns WHERE table_name = 'work_order_pole_photo' AND column_name = 'reviewed_by'").SingleAsync());
        await migrator.MigrateAsync();
        // Refuse a lossy Down, and leave its transaction/schema intact.
        using (db.EnterUnscopedSystemWriteBackdoor())
        {
            var commune = new AdministrativeUnit { Name = "Photo review rollback test" }; db.Add(commune); await db.SaveChangesAsync();
            var road = new RoadSegment { CommuneId = commune.CommuneId, SegmentName = "rollback", DataSource = DataSource.Simulated,
                Geom = new LineString([new Coordinate(108, 16), new Coordinate(108.01, 16.01)]) { SRID = 4326 } };
            db.Add(road); await db.SaveChangesAsync();
            var pole = new Pole { CommuneId = commune.CommuneId, SegmentId = road.SegmentId, DataSource = DataSource.Simulated,
                Geom = new Point(108, 16) { SRID = 4326 } };
            db.Add(pole); await db.SaveChangesAsync();
            var current = new PoleCurrentStatus { CommuneId = commune.CommuneId, PoleId = pole.PoleId,
                FixtureStatus = FixtureStatus.Out, StatusConfidence = null };
            db.Add(current); await db.SaveChangesAsync();
            var error = await Assert.ThrowsAsync<PostgresException>(() => migrator.MigrateAsync("20261015153829_AddWorkOrderPolePhotos"));
            Assert.Contains("Cannot roll back AddPolePhotoReviews", error.MessageText);
            Assert.Contains("20261020134307_AddPolePhotoReviews", await db.Database.GetAppliedMigrationsAsync());
            db.Remove(current); await db.SaveChangesAsync();
        }
        db.ChangeTracker.Clear();
        await migrator.MigrateAsync("20261014095347_CommuneMapFrame");
        Assert.Null(await db.Database.SqlQuery<string>($"SELECT to_regclass('work_order_pole_photo')::text AS \"Value\"").SingleOrDefaultAsync());
        await migrator.MigrateAsync();
        Assert.Equal(1, await db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM pg_constraint WHERE conname = 'fk_pole_photo_same_commune'").SingleAsync());
    }
}
