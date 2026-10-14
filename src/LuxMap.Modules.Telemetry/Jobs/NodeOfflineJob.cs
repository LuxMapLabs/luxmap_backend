using System.Globalization;
using LuxMap.Modules.Assets.Entities;
using LuxMap.Modules.Faults.Entities;
using LuxMap.Modules.Notifications;
using LuxMap.Modules.Notifications.Entities;
using LuxMap.Modules.Telemetry.Configurations;
using LuxMap.Modules.Telemetry.Entities;
using LuxMap.Persistence;
using LuxMap.Persistence.Audit;
using LuxMap.Persistence.Jobs;
using LuxMap.Shared.Contracts;
using LuxMap.Shared.Contracts.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace LuxMap.Modules.Telemetry.Jobs;

/// <summary>
/// BE-26 (D-5…D-8): a device silent for longer than <c>Iot:OfflineAfter</c> — the SAME rule the map reads (<see cref="IotOptions.StatusAt"/>,
/// I-3) — gets one <c>node_offline</c> fault per silence, and the commune's Managers are told.
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>A silence is identified by data, not by when the job ran:</b> <c>detected_at = last_report_at + OfflineAfter</c>, the instant it
/// crossed the threshold. Every instance and every retry computes the same value, so <see cref="FaultOriginNodeConfiguration.OneFaultPerSilence"/>
/// lets exactly one fault in. A fault rejected or resolved during the same silence is not raised again; the device reporting and falling
/// silent again moves <c>last_report_at</c>, which is a new silence.
/// </para>
/// <para>
/// A device that never reported raises nothing (D-5: not yet in service — the map shows it). Nothing closes the fault when the device comes
/// back (D-6): resolving is a person's decision, and there is no system account to sign it.
/// </para>
/// </remarks>
public sealed class NodeOfflineJob(JobContextFactory contexts, IotOptions iot, ILogger<NodeOfflineJob> log) : IScheduledJob
{
    public string Name => "node_offline";

    public TimeSpan Interval => TimeSpan.FromMinutes(1);

    public async Task RunAsync(DateTime now, string correlationId, CancellationToken ct)
    {
        // StatusAt: offline when now − last > OfflineAfter, i.e. last < now − OfflineAfter; exactly at the threshold is still online.
        var cutoff = now - iot.OfflineAfter;
        List<Silent> silent;
        await using (var scan = contexts.Unscoped())
        {
            silent = await scan.Set<IotNode>().IgnoreQueryFilters().AsNoTracking()
                .Where(n => n.LastReportAt != null && n.LastReportAt < cutoff)
                .OrderBy(n => n.NodeId.Length).ThenBy(n => n.NodeId)
                .Select(n => new Silent(n.NodeId, n.CommuneId, n.LastReportAt!.Value))
                .ToListAsync(ct);
        }

        var raised = 0;
        foreach (var commune in silent.GroupBy(s => s.CommuneId))
        {
            await using var db = contexts.Open(commune.Key);
            var audit = JobContextFactory.AuditFor(db, correlationId);
            foreach (var node in commune) raised += await RaiseAsync(db, audit, node, now, ct) ? 1 : 0;
        }

        if (raised > 0) log.LogInformation("Raised {Count} node_offline faults ({Correlation}).", raised, correlationId);
    }

    private async Task<bool> RaiseAsync(LuxMapDbContext db, IAuditTrail audit, Silent candidate, DateTime now, CancellationToken ct)
    {
        // Re-read in the commune's own context: the device may have reported since the scan.
        var node = await db.Set<IotNode>().AsNoTracking().SingleOrDefaultAsync(n => n.NodeId == candidate.NodeId, ct);
        if (node?.LastReportAt != candidate.LastReportAt) return false;

        var detected = candidate.LastReportAt + iot.OfflineAfter;
        if (await db.Set<Fault>().AnyAsync(f => f.OriginNodeId == node.NodeId && f.FaultType == FaultType.NodeOffline
                && f.DetectedAt == detected, ct)) return false;

        // The cabinet's point is where the device is (I-9, CABINET); read past the commune filter — a location, not a record to expose.
        var cabinet = await db.Set<ElectricalCabinet>().IgnoreQueryFilters().AsNoTracking()
            .Where(c => c.CabinetId == node.CabinetId).Select(c => c.Geom).SingleAsync(ct);
        // The same draw the column DEFAULT makes; the id is needed before saving for the audit event. A constant, never input.
        var sql = $"SELECT {PrefixedIds.Fault.DefaultValueSql} AS \"Value\"";
        var faultId = await db.Database.SqlQueryRaw<string>(sql).SingleAsync(ct);
        var silentFor = (int)Math.Round(iot.OfflineAfter.TotalMinutes);
        var fault = new Fault
        {
            FaultId = faultId, CommuneId = node.CommuneId, OriginNodeId = node.NodeId, Lat = cabinet.Y, Lng = cabinet.X,
            FaultType = FaultType.NodeOffline, FaultStatus = FaultStatus.Detected, Severity = Severity.Medium,
            SourceChannel = SourceChannel.Iot, DataSource = node.DataSource, DetectedAt = detected,
            Note = string.Create(CultureInfo.InvariantCulture,
                $"Thiết bị {node.NodeId} ở tủ {node.CabinetId} không báo về quá {silentFor} phút; lần báo cuối {candidate.LastReportAt:yyyy-MM-ddTHH:mm:ss}Z."),
            CreatedAt = now, UpdatedAt = now,
        };
        db.Add(fault);
        audit.Record(new(now, AuditActorKind.Iot, null, null, node.CommuneId, AuditEntityType.Fault, faultId, AuditAction.Created, null,
            new { fault.FaultId, fault.FaultType, fault.FaultStatus, fault.Severity, fault.DataSource, fault.DetectedAt,
                origin_node_id = node.NodeId, last_report_at = candidate.LastReportAt, offline_after_minutes = silentFor }));
        Notifier.Stage(db, Silence(fault, node, silentFor), await Notifier.ManagersCoveringAsync(db, [node.CommuneId], ct), null, now);

        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException error) when (error.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: FaultOriginNodeConfiguration.OneFaultPerSilence })
        {
            return false; // another instance raised this silence first
        }
        finally
        {
            db.ChangeTracker.Clear();
        }
    }

    /// <summary>The notice is <c>fault_reported</c> (D-8); the words are the device's, not "a field engineer reported…".</summary>
    private static NotificationMessage Silence(Fault fault, IotNode node, int minutes) => new(
        NotificationType.FaultReported, fault.CommuneId, NotificationEntityType.Fault, fault.FaultId,
        $"Sự cố mới {fault.FaultId}: Thiết bị mất kết nối",
        $"Thiết bị {node.NodeId} ở tủ {node.CabinetId} không báo về quá {minutes} phút. Sự cố chờ Quản lý xử lý.");

    private sealed record Silent(string NodeId, string CommuneId, DateTime LastReportAt);
}
