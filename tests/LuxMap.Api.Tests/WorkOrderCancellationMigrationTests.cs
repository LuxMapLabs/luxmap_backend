using LuxMap.Modules.Identity.Entities;
using LuxMap.Modules.Notifications.Entities;
using LuxMap.Modules.WorkOrders.Entities;
using LuxMap.Persistence;
using LuxMap.Persistence.Audit;
using LuxMap.Shared.Authorization;
using LuxMap.Shared.Contracts.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit.Abstractions;

namespace LuxMap.Api.Tests;

[Collection(nameof(AssetDatabaseCollection))]
public sealed class WorkOrderCancellationMigrationTests(AssetImportFixture fixture, ITestOutputHelper output)
{
    [Fact]
    public async Task Cancellation_schema_and_rollback_preserve_prior_values_and_refuse_loss()
    {
        const string previous = "20261023155921_AddVersionTraceability";
        const string current = "20261026100552_AddWorkOrderCancellationRequests";
        await using var container = new PostgreSqlBuilder(TestDatabase.Image)
            .WithDatabase("cancellation_migration_test").WithUsername("migration_test")
            .WithPassword(System.Security.Cryptography.RandomNumberGenerator.GetHexString(32)).Build();
        await container.StartAsync();
        await using var source = PersistenceServiceCollectionExtensions.BuildDataSource(container.GetConnectionString());
        var options = new DbContextOptionsBuilder<LuxMapDbContext>();
        PersistenceServiceCollectionExtensions.Configure(options, source);
        await using var db = new LuxMapDbContext(options.Options, fixture.Services.GetRequiredService<ModuleAssemblyCatalog>(),
            fixture.Services.GetRequiredService<ICommuneScopeAccessor>());
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(previous);
        using var system = db.EnterUnscopedSystemWriteBackdoor();
        var commune = new AdministrativeUnit { Name = "Cancellation rollback" };
        var user = new AppUser { Username = "wocr" + Guid.NewGuid().ToString("N"), FullName = "Rollback actor", Email = "wocr@example.invalid",
            Role = UserRole.FieldEngineer, PasswordHash = "unused", PasswordAlgorithm = "pbkdf2-aspnetcore-v3", PasswordSetAt = DateTime.UtcNow };
        db.AddRange(commune, user); await db.SaveChangesAsync();
        // Insert with SQL while the model has columns which the previous schema does not have yet.
        var id = await db.Database.SqlQuery<string>($"INSERT INTO work_order (commune_id,title,created_by,task_kind,wo_status,assigned_to,assigned_at) VALUES ({commune.CommuneId},'Old row',{user.UserId},'inspection','assigned',{user.UserId},now()) RETURNING work_order_id AS \"Value\"").ToListAsync();
        var woId = Assert.Single(id);
        await migrator.MigrateAsync();
        Assert.False(db.Database.HasPendingModelChanges());
        var old = await db.Set<WorkOrder>().IgnoreQueryFilters().SingleAsync(x => x.WorkOrderId == woId);
        Assert.Equal(CancellationRequestStatus.None, old.CancellationRequestStatus);
        Assert.Null(old.CancellationRequestedAt);
        Assert.Null(old.CancellationRequestNote);
        Assert.Null(old.CancellationRejectedAt);
        Assert.Null(old.CancellationRejectNote);
        var indexes = await db.Database.SqlQuery<string>($"SELECT indexname AS \"Value\" FROM pg_indexes WHERE tablename = 'work_order' ORDER BY indexname").ToArrayAsync();
        Assert.Contains("ix_work_order_commune_id", indexes);
        foreach (var name in new[] { "ck_work_order_cancellation_request_status", "ck_work_order_cancellation_request_state", "ck_notification_type", "ck_audit_event_action" })
            output.WriteLine(await db.Database.SqlQuery<string>($"SELECT conname || ': ' || pg_get_constraintdef(oid) AS \"Value\" FROM pg_constraint WHERE conname = {name}").SingleAsync());
        Assert.Equal(0, await db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM information_schema.columns WHERE table_name = 'work_order' AND column_name = 'xmin'").SingleAsync());
        foreach (var sql in new[]
        {
            "cancellation_request_status = 'bogus'",
            "cancellation_request_status = 'pending'",
            "cancellation_request_note = 'orphan'",
            "cancellation_request_status = 'pending', cancellation_request_note = ' ', cancellation_requested_at = now()",
            "cancellation_request_status = 'pending', cancellation_request_note = repeat('a',501), cancellation_requested_at = now()",
            "cancellation_request_status = 'rejected', cancellation_request_note = 'x', cancellation_requested_at = now(), cancellation_reject_note = 'x', cancellation_rejected_at = now() - interval '1 second'",
            "wo_status = 'in_progress', started_at = now(), cancellation_request_status = 'pending', cancellation_request_note = 'x', cancellation_requested_at = now()",
        })
        {
            var statement = $"UPDATE work_order SET {sql} WHERE work_order_id = {{0}}";
            var error = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(statement, woId));
            Assert.Equal("23514", error.SqlState);
        }
        // A clean rollback preserves the old row and every old index; reapply restores the additive schema.
        db.ChangeTracker.Clear();
        await migrator.MigrateAsync(previous);
        await migrator.MigrateAsync();
        Assert.Equal(indexes, await db.Database.SqlQuery<string>($"SELECT indexname AS \"Value\" FROM pg_indexes WHERE tablename = 'work_order' ORDER BY indexname").ToArrayAsync());
        // All historical members still pass the actual database CHECKs (including citizen, overdue, relocated).
        foreach (var type in Enum.GetValues<NotificationType>())
        {
            var notice = new Notification { CommuneId = commune.CommuneId, RecipientUserId = user.UserId, Type = type,
                EntityType = NotificationEntityType.WorkOrder, EntityId = woId, Title = "Test", Body = "Test" };
            db.Add(notice); await db.SaveChangesAsync();
            if (type is NotificationType.WorkOrderCancellationRequested or NotificationType.WorkOrderCancellationRejected)
            {
                var error = await Assert.ThrowsAsync<PostgresException>(() => migrator.MigrateAsync(previous));
                Assert.Contains("Cannot roll back AddWorkOrderCancellationRequests", error.MessageText);
                Assert.Contains(current, await db.Database.GetAppliedMigrationsAsync());
            }
            db.Remove(notice); await db.SaveChangesAsync();
        }
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE work_order SET cancellation_request_status='pending', cancellation_request_note='x', cancellation_requested_at=now() WHERE work_order_id={woId}");
        var pendingError = await Assert.ThrowsAsync<PostgresException>(() => migrator.MigrateAsync(previous));
        Assert.Contains("Cannot roll back AddWorkOrderCancellationRequests", pendingError.MessageText);
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE work_order SET cancellation_request_status='none', cancellation_request_note=NULL, cancellation_requested_at=NULL WHERE work_order_id={woId}");
        foreach (var action in Enum.GetValues<AuditAction>().Where(x => x is not AuditAction.CancellationRequested and not AuditAction.CancellationRejected))
        {
            db.Add(new AuditEvent { Action = action, ActorKind = AuditActorKind.User, ActorUserId = user.UserId, ActorRole = UserRole.FieldEngineer, AfterState = "{}",
                EntityType = AuditEntityType.WorkOrder, EntityId = woId, CommuneId = commune.CommuneId, CorrelationId = "rollback-old" });
            await db.SaveChangesAsync();
        }
        foreach (var action in new[] { AuditAction.CancellationRequested, AuditAction.CancellationRejected })
        {
            db.Add(new AuditEvent { Action = action, ActorKind = AuditActorKind.User, ActorUserId = user.UserId, ActorRole = UserRole.FieldEngineer, AfterState = "{}",
                EntityType = AuditEntityType.WorkOrder, EntityId = woId, CommuneId = commune.CommuneId, CorrelationId = "rollback-new" });
            await db.SaveChangesAsync();
        }
        var auditError = await Assert.ThrowsAsync<PostgresException>(() => migrator.MigrateAsync(previous));
        Assert.Contains("Cannot roll back AddWorkOrderCancellationRequests", auditError.MessageText);
        Assert.Contains(current, await db.Database.GetAppliedMigrationsAsync());
        Assert.Equal(2, await db.Set<AuditEvent>().IgnoreQueryFilters().CountAsync(x => x.CorrelationId == "rollback-new"));
        Assert.Equal(5, await db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM information_schema.columns WHERE table_name='work_order' AND column_name LIKE 'cancellation_%'").SingleAsync());
        output.WriteLine("Apply / Down / reapply: passed. Down refused pending, each new notification, and new audit; schema/history intact.");
    }
}
