using LuxMap.Persistence;
using LuxMap.Persistence.Audit;
using LuxMap.Shared.Authorization;
using LuxMap.Shared.Contracts.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using LuxMap.Modules.Identity.Entities;

namespace LuxMap.Api.Tests;

[Collection(nameof(AssetDatabaseCollection))]
public sealed class SurveyRecurringMigrationTests(AssetImportFixture fixture)
{
    [Fact]
    public async Task Rolls_back_cleanly_until_an_audit_event_for_a_recurring_schedule_exists_and_then_refuses()
    {
        const string previous = "20261109080624_AddAccountAvatars";
        await using var container = new PostgreSqlBuilder(TestDatabase.Image)
            .WithDatabase("survey_recurring_migration_test").WithUsername("migration_test")
            .WithPassword(System.Security.Cryptography.RandomNumberGenerator.GetHexString(32)).Build();
        await container.StartAsync();
        await using var source = PersistenceServiceCollectionExtensions.BuildDataSource(container.GetConnectionString());
        var options = new DbContextOptionsBuilder<LuxMapDbContext>();
        PersistenceServiceCollectionExtensions.Configure(options, source);
        await using var db = new LuxMapDbContext(options.Options, fixture.Services.GetRequiredService<ModuleAssemblyCatalog>(),
            fixture.Services.GetRequiredService<ICommuneScopeAccessor>());
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync();
        Assert.False(db.Database.HasPendingModelChanges());
        Assert.Equal(2, await Tables(db));
        Assert.Contains("survey_recurring_schedule", await Check(db, "ck_audit_event_entity_type"));
        Assert.Contains("'deleted'", await Check(db, "ck_audit_event_action"));

        // Nothing mentions the schedules yet: the rollback runs, the tables go and the old CHECKs are back.
        await migrator.MigrateAsync(previous);
        Assert.Equal(0, await Tables(db));
        Assert.DoesNotContain("survey_recurring_schedule", await Check(db, "ck_audit_event_entity_type"));
        Assert.DoesNotContain("'deleted'", await Check(db, "ck_audit_event_action"));

        // audit_event is append-only, so one row naming the new entity type makes the rollback refuse instead of purging it.
        await migrator.MigrateAsync();
        using var system = db.EnterUnscopedSystemWriteBackdoor();
        var commune = new AdministrativeUnit { Name = "Survey recurring rollback" };
        db.Add(commune); await db.SaveChangesAsync();
        db.Add(new AuditEvent { Action = AuditAction.Deleted, ActorKind = AuditActorKind.System, AfterState = "{}",
            EntityType = AuditEntityType.SurveyRecurringSchedule, EntityId = "SSCH-0001", CommuneId = commune.CommuneId, CorrelationId = "rollback-new" });
        await db.SaveChangesAsync();
        var error = await Assert.ThrowsAsync<Npgsql.PostgresException>(() => migrator.MigrateAsync(previous));
        Assert.Contains("Cannot roll back SurveyRecurringSchedules", error.MessageText);
        Assert.Equal(2, await Tables(db));
        Assert.Contains("survey_recurring_schedule", await Check(db, "ck_audit_event_entity_type"));
    }

    private static Task<int> Tables(LuxMapDbContext db) => db.Database.SqlQuery<int>(
        $"SELECT count(*)::int AS \"Value\" FROM information_schema.tables WHERE table_name IN ('survey_recurring_schedule','survey_recurring_occurrence')").SingleAsync();

    private static Task<string> Check(LuxMapDbContext db, string name) => db.Database.SqlQuery<string>(
        $"SELECT pg_get_constraintdef(oid) AS \"Value\" FROM pg_constraint WHERE conname = {name}").SingleAsync();
}
