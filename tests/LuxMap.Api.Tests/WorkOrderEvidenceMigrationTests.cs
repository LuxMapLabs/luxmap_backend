using LuxMap.Modules.Assets.Entities;
using LuxMap.Modules.Identity.Entities;
using LuxMap.Persistence;
using LuxMap.Shared.Authorization;
using LuxMap.Shared.Contracts.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Testcontainers.PostgreSql;

namespace LuxMap.Api.Tests;

public partial class WorkOrderTests
{
    [Fact]
    public async Task Evidence_migration_preserves_old_rows_and_refuses_rollback_with_pole_links()
    {
        const string previous = "20261113100141_CommuneBoundary";
        const string current = "20261114044112_AddEvidencePole";
        await using var container = new PostgreSqlBuilder(TestDatabase.Image)
            .WithDatabase("evidence_pole_migration_test").WithUsername("migration_test")
            .WithPassword(System.Security.Cryptography.RandomNumberGenerator.GetHexString(32)).Build();
        await container.StartAsync();
        await using var source = PersistenceServiceCollectionExtensions.BuildDataSource(container.GetConnectionString());
        var options = new DbContextOptionsBuilder<LuxMapDbContext>();
        PersistenceServiceCollectionExtensions.Configure(options, source);
        await using var db = new LuxMapDbContext(options.Options, factory.Services.GetRequiredService<ModuleAssemblyCatalog>(),
            factory.Services.GetRequiredService<ICommuneScopeAccessor>());
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(previous);
        var commune = await OldSchemaCommune.InsertAsync(db, "Evidence pole rollback");
        // A fresh account: OldSchemaUser overwrites UserId, and the shared users["a"] is what teardown deletes by id.
        var user = new AppUser { Username = "wepm" + Guid.NewGuid().ToString("N"), FullName = "Rollback actor", Email = "wepm@example.invalid",
            Role = UserRole.FieldEngineer, PasswordHash = "unused", PasswordAlgorithm = "pbkdf2-aspnetcore-v3", PasswordSetAt = DateTime.UtcNow };
        await OldSchemaUser.InsertAsync(db, user);
        var order = Assert.Single(await db.Database.SqlQuery<string>($"""
            INSERT INTO work_order (commune_id,title,created_by,task_kind,wo_status,assigned_to,assigned_at,started_at)
            VALUES ({commune},'Evidence rollback',{user.UserId},'inspection','in_progress',{user.UserId},now(),now())
            RETURNING work_order_id AS "Value"
            """).ToListAsync());
        var pole = Assert.Single(await db.Database.SqlQuery<string>($"""
            INSERT INTO pole (commune_id, data_source) VALUES ({commune}, 'simulated') RETURNING pole_id AS "Value"
            """).ToListAsync());
        var evidence = Assert.Single(await db.Database.SqlQuery<string>($"""
            INSERT INTO repair_evidence (work_order_id,commune_id,kind,captured_at,lat,lng,object_key,thumbnail_key,byte_count,thumbnail_bytes,uploaded_by)
            VALUES ({order},{commune},'observation',now(),16,108,'original/old.jpg','thumb/old.jpg',1,1,{user.UserId})
            RETURNING evidence_id AS "Value"
            """).ToListAsync());
        Task<string[]> Indexes() => db.Database.SqlQuery<string>($"""
            SELECT indexname AS "Value" FROM pg_indexes WHERE tablename = 'repair_evidence' ORDER BY indexname
            """).ToArrayAsync();
        var oldIndexes = await Indexes();
        await migrator.MigrateAsync(current);
        Assert.False(db.Database.HasPendingModelChanges());
        Assert.Null(await db.Database.SqlQuery<string>($"SELECT pole_id AS \"Value\" FROM repair_evidence WHERE evidence_id = {evidence}").SingleAsync());
        Assert.Equal(oldIndexes, (await Indexes()).Where(x => x != "ix_repair_evidence_pole_id").ToArray());
        Assert.Contains("ix_repair_evidence_pole_id", await Indexes());
        Assert.DoesNotContain(db.Model.FindEntityType(typeof(Pole))!.GetKeys(), k => k.Properties.Any(p => p.Name == "CommuneId"));
        foreach (var name in new[] { "fk_repair_evidence_pole_same_commune", "ck_repair_evidence_fault_no_pole" })
        {
            var definition = await db.Database.SqlQuery<string>($"SELECT pg_get_constraintdef(oid) AS \"Value\" FROM pg_constraint WHERE conname = {name}").SingleAsync();
            output.WriteLine(name + ": " + definition);
            if (name.StartsWith("fk_", StringComparison.Ordinal))
                Assert.Contains("FOREIGN KEY (pole_id, commune_id) REFERENCES pole(pole_id, commune_id) ON DELETE RESTRICT", definition);
        }
        await db.Database.ExecuteSqlAsync($"UPDATE repair_evidence SET pole_id = {pole} WHERE evidence_id = {evidence}");
        var error = await Assert.ThrowsAsync<PostgresException>(() => migrator.MigrateAsync(previous));
        Assert.Contains("AddEvidencePole cannot be rolled back: pole links exist", error.MessageText);
        Assert.Contains(current, await db.Database.GetAppliedMigrationsAsync());
        Assert.Equal(pole, await db.Database.SqlQuery<string>($"SELECT pole_id AS \"Value\" FROM repair_evidence WHERE evidence_id = {evidence}").SingleAsync());
        await db.Database.ExecuteSqlAsync($"UPDATE repair_evidence SET pole_id = NULL WHERE evidence_id = {evidence}");
        await migrator.MigrateAsync(previous);
        Assert.Equal(oldIndexes, await Indexes());
        Assert.Equal(0, await db.Database.SqlQuery<int>($"""
            SELECT count(*)::int AS "Value" FROM pg_constraint
            WHERE conname IN ('fk_repair_evidence_pole_same_commune', 'ck_repair_evidence_fault_no_pole')
            """).SingleAsync());
        Assert.Equal(0, await db.Database.SqlQuery<int>($"""
            SELECT count(*)::int AS "Value" FROM information_schema.columns WHERE table_name = 'repair_evidence' AND column_name = 'pole_id'
            """).SingleAsync());
        Assert.Equal(1, await db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM repair_evidence WHERE evidence_id = {evidence}").SingleAsync());
        await migrator.MigrateAsync(current);
        Assert.Null(await db.Database.SqlQuery<string>($"SELECT pole_id AS \"Value\" FROM repair_evidence WHERE evidence_id = {evidence}").SingleAsync());
        output.WriteLine("AddEvidencePole: Up / Down / reapply preserved the old row and indexes; Down refused a non-null pole link without losing it.");
    }
}
