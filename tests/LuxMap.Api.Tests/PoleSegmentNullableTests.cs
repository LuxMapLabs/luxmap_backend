using System.Security.Claims;
using LuxMap.Modules.Assets.Entities;
using LuxMap.Modules.Identity.Auth;
using LuxMap.Persistence;
using LuxMap.Shared.Authorization;
using LuxMap.Shared.Contracts.Enums;
using LuxMap.Shared.Contracts.Errors;
using LuxMap.Shared.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using NetTopologySuite.Geometries;
using Npgsql;
using Testcontainers.PostgreSql;

namespace LuxMap.Api.Tests;

[Collection(nameof(AssetDatabaseCollection))]
public sealed class PoleSegmentNullableTests(AssetImportFixture fixture)
{
    [Fact]
    public async Task Pole_without_segment_round_trips_and_stays_commune_scoped()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LuxMapDbContext>();
        var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        var previous = accessor.HttpContext;
        var home = NewPole(fixture.CommuneId);
        var foreign = NewPole(fixture.ForeignCommuneId);
        try
        {
            using (db.EnterUnscopedSystemWriteBackdoor())
            {
                db.AddRange(home, foreign);
                await db.SaveChangesAsync();
            }
            db.ChangeTracker.Clear();
            accessor.HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([
                    new Claim(AuthClaims.Subject, "pole-segment-null-test"),
                    new Claim(AuthClaims.Role, "manager"),
                    new Claim(AuthClaims.CommuneIds, fixture.CommuneId),
                ], "Test")),
            };

            var read = await db.Set<Pole>().Include(p => p.Segment).SingleAsync(p => p.PoleId == home.PoleId);
            Assert.Null(read.SegmentId);
            Assert.Null(read.Segment);
            Assert.Equal(fixture.CommuneId, read.CommuneId);
            Assert.Equal(home.Geom, read.Geom);
            Assert.False(await db.Set<Pole>().AnyAsync(p => p.PoleId == foreign.PoleId));

            db.Add(NewPole(fixture.ForeignCommuneId));
            var added = await Assert.ThrowsAsync<LuxMapException>(() => db.SaveChangesAsync());
            Assert.Equal(ErrorCodes.CommuneForbidden, added.Code);
            db.ChangeTracker.Clear();

            read = await db.Set<Pole>().SingleAsync(p => p.PoleId == home.PoleId);
            read.CommuneId = fixture.ForeignCommuneId;
            var modified = await Assert.ThrowsAsync<LuxMapException>(() => db.SaveChangesAsync());
            Assert.Equal(ErrorCodes.CommuneForbidden, modified.Code);
            db.ChangeTracker.Clear();

            db.Remove(foreign);
            var deleted = await Assert.ThrowsAsync<LuxMapException>(() => db.SaveChangesAsync());
            Assert.Equal(ErrorCodes.CommuneForbidden, deleted.Code);
            db.ChangeTracker.Clear();

            var invalid = NewPole(fixture.CommuneId);
            invalid.SegmentId = "SEG-does-not-exist-pole-segment-null";
            db.Add(invalid);
            var failure = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            var fk = Assert.IsType<PostgresException>(failure.InnerException);
            Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, fk.SqlState);
            Assert.Equal("fk_pole_road_segment_segment_id", fk.ConstraintName);
        }
        finally
        {
            accessor.HttpContext = previous;
            db.ChangeTracker.Clear();
            using (db.EnterUnscopedSystemWriteBackdoor())
            {
                var rows = await db.Set<Pole>().IgnoreQueryFilters()
                    .Where(p => p.PoleId == home.PoleId || p.PoleId == foreign.PoleId).ToListAsync();
                db.RemoveRange(rows);
                await db.SaveChangesAsync();
            }
        }
    }

    [Fact]
    public async Task Nullable_pole_segment_schema_and_rollback_are_explicit()
    {
        await using var container = new PostgreSqlBuilder(TestDatabase.Image)
            .WithDatabase("pole_segment_nullable_test").WithUsername("migration_test")
            .WithPassword(System.Security.Cryptography.RandomNumberGenerator.GetHexString(32)).Build();
        await container.StartAsync();
        await using var source = PersistenceServiceCollectionExtensions.BuildDataSource(container.GetConnectionString());
        var options = new DbContextOptionsBuilder<LuxMapDbContext>();
        PersistenceServiceCollectionExtensions.Configure(options, source);
        await using var db = new LuxMapDbContext(options.Options, fixture.Services.GetRequiredService<ModuleAssemblyCatalog>(),
            fixture.Services.GetRequiredService<ICommuneScopeAccessor>());
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync();
        var migration = (await db.Database.GetAppliedMigrationsAsync()).Single(id => id.EndsWith("_PoleSegmentNullable", StringComparison.Ordinal));
        const string previous = "20261029090000_AccountsSingleCommune";
        Assert.False(db.Database.HasPendingModelChanges());
        await AssertCatalog(db, "YES");

        using (db.EnterUnscopedSystemWriteBackdoor())
        {
            var commune = new AdministrativeUnit { Name = "pole-segment-null rollback" };
            db.Add(commune);
            await db.SaveChangesAsync();
            var pole = NewPole(commune.CommuneId);
            db.Add(pole);
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            var error = await Assert.ThrowsAsync<PostgresException>(() => migrator.MigrateAsync(previous));
            Assert.Equal(PostgresErrorCodes.RaiseException, error.SqlState);
            Assert.Contains("Cannot roll back PoleSegmentNullable: 1 pole(s)", error.MessageText);
            Assert.Contains(migration, await db.Database.GetAppliedMigrationsAsync());
            await AssertCatalog(db, "YES");
            var preserved = await db.Set<Pole>().IgnoreQueryFilters().SingleAsync(p => p.PoleId == pole.PoleId);
            Assert.Null(preserved.SegmentId);
            Assert.Equal(pole.CommuneId, preserved.CommuneId);
            Assert.Equal(pole.Geom, preserved.Geom);

            var road = new RoadSegment
            {
                CommuneId = commune.CommuneId, SegmentName = "rollback", DataSource = DataSource.Simulated, LengthM = 100,
                Geom = new LineString([new Coordinate(108, 16), new Coordinate(108.01, 16.01)]) { SRID = 4326 },
            };
            db.Add(road);
            await db.SaveChangesAsync();
            preserved.SegmentId = road.SegmentId;
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
            await migrator.MigrateAsync(previous);
            Assert.DoesNotContain(migration, await db.Database.GetAppliedMigrationsAsync());
            await AssertCatalog(db, "NO");
            Assert.Equal(road.SegmentId, await db.Set<Pole>().IgnoreQueryFilters()
                .Where(p => p.PoleId == pole.PoleId).Select(p => p.SegmentId).SingleAsync());
            await migrator.MigrateAsync();
            await AssertCatalog(db, "YES");
            Assert.False(db.Database.HasPendingModelChanges());
        }
    }

    private static Pole NewPole(string communeId) => new()
    {
        CommuneId = communeId,
        Geom = new Point(108, 16) { SRID = 4326 },
        DataSource = DataSource.Simulated,
    };

    private static async Task AssertCatalog(LuxMapDbContext db, string nullable)
    {
        Assert.Equal(nullable, await db.Database.SqlQuery<string>($"SELECT is_nullable AS \"Value\" FROM information_schema.columns WHERE table_schema = 'public' AND table_name = 'pole' AND column_name = 'segment_id' AND column_default IS NULL").SingleAsync());
        Assert.Equal("r", await db.Database.SqlQuery<string>($"SELECT confdeltype::text AS \"Value\" FROM pg_constraint WHERE conrelid = 'pole'::regclass AND confrelid = 'road_segment'::regclass AND conname = 'fk_pole_road_segment_segment_id'").SingleAsync());
        Assert.Equal(1, await db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM pg_indexes WHERE schemaname = 'public' AND tablename = 'pole' AND indexname = 'ix_pole_segment_id' AND indexdef LIKE '%(segment_id)' AND indexdef NOT LIKE '%WHERE%'").SingleAsync());
    }
}
