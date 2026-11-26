using LuxMap.Modules.Faults.Entities;
using LuxMap.Persistence.Audit;
using LuxMap.Persistence.Jobs;
using LuxMap.Shared.Contracts.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LuxMap.Api.Tests;

[Collection(nameof(AssetDatabaseCollection))]
public sealed class IotLegacyFaultCleanupTests(AssetImportFixture fixture)
{
    [Fact]
    public async Task Cleanup_previews_without_writes_then_restores_one_archives_duplicates_preserves_human_decisions_and_is_idempotent()
    {
        var rig = await new LightingTestRig(fixture).RigAsync(issueSecret: false);
        var contexts = fixture.Services.GetRequiredService<JobContextFactory>();
        async Task<string> Seed(int minutes, bool human)
        {
            await using var db = contexts.Open(fixture.CommuneId);
            var at = DateTime.UtcNow.AddMinutes(-minutes);
            var row = new Fault {
                CommuneId = fixture.CommuneId, OriginNodeId = rig.Node, FeederId = rig.Feeders[0], CabinetId = rig.Cabinet,
                SourceChannel = SourceChannel.Iot, DataSource = DataSource.CalibrationRig,
                IotAnomalyCode = "current_drop", FaultType = FaultType.CurrentAnomaly, FaultStatus = FaultStatus.Rejected,
                Severity = Severity.Medium, Lat = 13.5, Lng = 109.5, DetectedAt = at, CreatedAt = at,
                UpdatedAt = at, RecoveredAt = at, ConfirmedAt = at,
                ConfirmedBy = human ? fixture.ManagerUserId : null, ReviewNote = "legacy recovery",
            };
            // Seed old state with the same audit actor/action the historical writer used.
            using var seed = db.EnterUnscopedSystemWriteBackdoor();
            db.Add(row);
            await db.SaveChangesAsync();
            db.Add(new AuditEvent { OccurredAt = at, ActorKind = human ? AuditActorKind.User : AuditActorKind.Iot,
                ActorUserId = human ? fixture.ManagerUserId : null, ActorRole = human ? UserRole.Manager : null,
                CommuneId = fixture.CommuneId, EntityType = AuditEntityType.Fault, EntityId = row.FaultId,
                Action = AuditAction.Rejected, AfterState = "{}", CorrelationId = "test:legacy-rejection" });
            await db.SaveChangesAsync();
            return row.FaultId;
        }
        var older = await Seed(5, false);
        var newest = await Seed(3, false);
        var manual = await Seed(1, true);
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "LuxMap.slnx"))) root = root.Parent;
        Assert.NotNull(root);
        var text = await File.ReadAllTextAsync(Path.Combine(root.FullName, "scripts/maintenance/repair-node-004-iot-faults.sql"));
        text = text.Replace("COM-070", fixture.CommuneId).Replace("NODE-004", rig.Node)
            .Replace("FDR-009", rig.Feeders[0]).Replace("CAB-011", rig.Cabinet);
        var begin = text.IndexOf("BEGIN;", StringComparison.Ordinal) + "BEGIN;".Length;
        var apply = text.IndexOf("\\if :apply", StringComparison.Ordinal);
        var previewSql = text[begin..apply];
        var applyBegin = apply + "\\if :apply".Length;
        var applySql = text[applyBegin..text.IndexOf("COMMIT;", applyBegin, StringComparison.Ordinal)];
        async Task Run(bool commit)
        {
            await using var db = contexts.Open(fixture.CommuneId);
            await using var tx = await db.Database.BeginTransactionAsync();
            await db.Database.ExecuteSqlRawAsync(previewSql);
            if (commit) {
                await db.Database.ExecuteSqlRawAsync(applySql);
                await tx.CommitAsync();
            } else await tx.RollbackAsync();
        }
        await Run(false);
        await using (var db = contexts.Open(fixture.CommuneId)) {
            Assert.Equal(3, await db.Set<Fault>().CountAsync(f => f.OriginNodeId == rig.Node && f.FaultStatus == FaultStatus.Rejected));
        }
        await Run(true);
        await Run(true);
        await using (var db = contexts.Open(fixture.CommuneId)) {
            Assert.False(await db.Set<Fault>().AnyAsync(f => f.FaultId == older));
            var kept = await db.Set<Fault>().SingleAsync(f => f.FaultId == newest);
            Assert.Equal(FaultStatus.Detected, kept.FaultStatus);
            Assert.NotNull(kept.RecoveredAt);
            Assert.Null(kept.ConfirmedAt);
            Assert.Equal(FaultStatus.Rejected, (await db.Set<Fault>().SingleAsync(f => f.FaultId == manual)).FaultStatus);
            Assert.Equal(3, await db.Set<AuditEvent>().CountAsync(a =>
                new[] { older, newest, manual }.Contains(a.EntityId) && a.Action == AuditAction.Rejected));
            Assert.Equal(2, await db.Set<AuditEvent>().CountAsync(a =>
                new[] { older, newest, manual }.Contains(a.EntityId) && a.CorrelationId == "maintenance:IOT-FAULT-RECOVERY-1"));
        }
    }
}
