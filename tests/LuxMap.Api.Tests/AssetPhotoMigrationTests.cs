using System.Security.Cryptography;
using LuxMap.Modules.Assets.Entities;
using LuxMap.Modules.Identity.Entities;
using LuxMap.Persistence;
using LuxMap.Persistence.Audit;
using LuxMap.Shared.Authorization;
using LuxMap.Shared.Contracts;
using LuxMap.Shared.Contracts.Enums;
using LuxMap.Shared.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using NetTopologySuite.Geometries;
using Npgsql;
using Testcontainers.PostgreSql;

namespace LuxMap.Api.Tests;

[Collection(nameof(AssetDatabaseCollection))]
public sealed class AssetPhotoMigrationTests(AssetImportFixture fixture)
{
    [Fact]
    public async Task Fresh_schema_rolls_back_only_when_both_photo_tables_and_the_new_audit_types_are_empty()
    {
        await using var container = new PostgreSqlBuilder(TestDatabase.Image).WithDatabase("asset_photo_migration_test")
            .WithUsername("migration_test").WithPassword(RandomNumberGenerator.GetHexString(32)).Build();
        await container.StartAsync();
        await using var source = PersistenceServiceCollectionExtensions.BuildDataSource(container.GetConnectionString());
        var options = new DbContextOptionsBuilder<LuxMapDbContext>();
        PersistenceServiceCollectionExtensions.Configure(options, source);
        await using var db = new LuxMapDbContext(options.Options, fixture.Services.GetRequiredService<ModuleAssemblyCatalog>(),
            fixture.Services.GetRequiredService<ICommuneScopeAccessor>());
        var migrator = db.GetService<IMigrator>();
        var all = db.Database.GetMigrations().ToArray();
        var migration = all.Single(x => x.EndsWith("_AddAssetPhotos", StringComparison.Ordinal));
        var previous = all[Array.IndexOf(all, migration) - 1];
        await migrator.MigrateAsync();
        Assert.False(db.Database.HasPendingModelChanges());
        await migrator.MigrateAsync(previous);
        await migrator.MigrateAsync();
        var sql = migrator.GenerateScript(migration, previous);
        Assert.True(sql.IndexOf("Asset photos or their audit exist", StringComparison.Ordinal) < sql.IndexOf("DROP TABLE", StringComparison.Ordinal));
        Assert.Equal(2, await db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM pg_indexes WHERE indexname IN ('ix_pole_asset_photo_commune_id','ix_cabinet_asset_photo_commune_id') AND indexdef NOT LIKE '%WHERE%'").SingleAsync());
        using var write = db.EnterUnscopedSystemWriteBackdoor();
        var commune = new AdministrativeUnit { Name = "asset-photo-rollback" };
        var user = new AppUser { Username = "asset-photo-migration", Email = "asset-photo-migration@luxmap.local", FullName = "asset photo migration",
            PasswordAlgorithm = "pbkdf2-aspnetcore-v3", Role = UserRole.Manager };
        db.AddRange(commune, user); await db.SaveChangesAsync();
        var pole = new Pole { CommuneId = commune.CommuneId, DataSource = DataSource.Simulated };
        var cabinet = new ElectricalCabinet { CommuneId = commune.CommuneId, DataSource = DataSource.Simulated,
            CabinetName = "asset-photo-rollback", Geom = new Point(106, 10) { SRID = 4326 } };
        db.AddRange(pole, cabinet); await db.SaveChangesAsync();
        foreach (var isPole in new[] { true, false })
        {
            var spec = isPole ? PrefixedIds.PoleAssetPhoto : PrefixedIds.CabinetAssetPhoto;
            var nextSql = $"SELECT {spec.DefaultValueSql} AS \"Value\"";
            var id = await db.Database.SqlQueryRaw<string>(nextSql).SingleAsync();
            IAssetPhoto row = isPole ? new PoleAssetPhoto { PoleId = pole.PoleId } : new CabinetAssetPhoto { CabinetId = cabinet.CabinetId };
            row.PhotoId = id; row.CommuneId = commune.CommuneId; row.DataSource = DataSource.Simulated;
            row.CreatedAt = DateTime.UtcNow; row.UploadedBy = user.UserId; row.ClientOpId = Guid.NewGuid(); row.RequestHash = new string('a', 64);
            row.ObjectKey = StorageKeys.KeyFor(ObjectVariant.Original, id); row.ThumbnailKey = StorageKeys.KeyFor(ObjectVariant.Thumbnail, id);
            row.ByteCount = row.ThumbnailBytes = row.Width = row.Height = 1;
            row.DeletedAt = DateTime.UtcNow; row.DeletedBy = user.UserId; // Even an already hidden photo forbids Down.
            db.Add(row); await db.SaveChangesAsync();
            await Refused(); db.Remove(row); await db.SaveChangesAsync();
        }
        foreach (var type in new[] { AuditEntityType.PoleAssetPhoto, AuditEntityType.CabinetAssetPhoto })
        {
            var audit = new AuditEvent { CommuneId = commune.CommuneId, EntityType = type, EntityId = pole.PoleId,
                ActorKind = AuditActorKind.System, Action = AuditAction.Created, OccurredAt = DateTime.UtcNow,
                AfterState = "{}", CorrelationId = "asset-photo-migration" };
            db.Add(audit); await db.SaveChangesAsync(); await Refused();
            await using (var tx = await db.Database.BeginTransactionAsync())
            {
                await db.Database.ExecuteSqlRawAsync("SET LOCAL luxmap.audit_purge = 'on'");
                await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM audit_event WHERE audit_id = {audit.AuditId}");
                await tx.CommitAsync();
            }
            db.Entry(audit).State = EntityState.Detached;
        }
        db.ChangeTracker.Clear();
        await migrator.MigrateAsync(previous); await migrator.MigrateAsync();
        Assert.Contains(migration, await db.Database.GetAppliedMigrationsAsync());
        async Task Refused()
        {
            var error = await Assert.ThrowsAsync<PostgresException>(() => migrator.MigrateAsync(previous));
            Assert.Contains("Asset photos or their audit exist", error.MessageText);
            Assert.Contains(migration, await db.Database.GetAppliedMigrationsAsync());
            Assert.Equal(2, await db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM information_schema.tables WHERE table_schema = 'public' AND table_name IN ('pole_asset_photo','cabinet_asset_photo')").SingleAsync());
        }
    }
}
