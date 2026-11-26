using LuxMap.Modules.Faults;
using LuxMap.Modules.WorkOrders.Entities;
using LuxMap.Persistence;
using LuxMap.Modules.Faults.Entities;
using LuxMap.Persistence.Audit;
using LuxMap.Persistence.Jobs;
using LuxMap.Modules.Notifications;
using LuxMap.Modules.Notifications.Entities;
using LuxMap.Shared.Contracts.Enums;
using Microsoft.EntityFrameworkCore;

namespace LuxMap.Modules.WorkOrders;

/// <summary>Answers <see cref="IActiveWorkOrderLookup"/> from the <c>work_order_fault</c> link table.</summary>
/// <remarks>
/// Reads <see cref="WorkOrderFault"/>, which is commune-scoped only — not assignee-scoped like
/// <see cref="WorkOrder"/> — so the ID comes back whoever the work order is assigned to.
/// </remarks>
public sealed class ActiveWorkOrderLookup(LuxMapDbContext db) : IActiveWorkOrderLookup
{
    public IActiveWorkOrderLookup InContext(LuxMapDbContext context) => new ActiveWorkOrderLookup(context);

    public async Task<string?> AttachToInspectionAsync(Fault fault, DateTime now, string correlationId, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Inspection attachment requires the fault transaction.");
        if (fault.OriginNodeId is null || fault.FaultStatus != FaultStatus.Detected) return null;
        var candidates = await (
            from link in db.Set<WorkOrderFault>().AsNoTracking()
            join existing in db.Set<Fault>().AsNoTracking() on link.FaultId equals existing.FaultId
            join order in db.Set<WorkOrder>().IgnoreQueryFilters().AsNoTracking() on link.WorkOrderId equals order.WorkOrderId
            where link.ReleasedAt == null && link.CommuneId == fault.CommuneId && order.CommuneId == fault.CommuneId &&
                existing.OriginNodeId == fault.OriginNodeId && existing.FeederId == fault.FeederId &&
                (existing.FaultStatus == FaultStatus.Detected || existing.FaultStatus == FaultStatus.Confirmed ||
                 existing.FaultStatus == FaultStatus.InProgress || existing.FaultStatus == FaultStatus.Resolved) &&
                order.TaskKind == TaskKind.Inspection &&
                (order.WoStatus == WorkOrderStatus.Open || order.WoStatus == WorkOrderStatus.Assigned)
            select order.WorkOrderId).Distinct().Take(2).ToArrayAsync(ct);
        if (candidates.Length != 1) return null;
        var id = candidates[0];
        // Do not wait for a WO while holding fault locks: verification takes locks in reverse order.
        var locked = await db.Database.SqlQuery<int>($"SELECT 1 AS \"Value\" FROM work_order WHERE work_order_id = {id} AND commune_id = {fault.CommuneId} FOR UPDATE SKIP LOCKED").ToListAsync(ct);
        if (locked.Count == 0) return null;
        var wo = await db.Set<WorkOrder>().IgnoreQueryFilters().SingleAsync(w => w.WorkOrderId == id && w.CommuneId == fault.CommuneId, ct);
        if (wo.TaskKind != TaskKind.Inspection || wo.WoStatus is not (WorkOrderStatus.Open or WorkOrderStatus.Assigned)) return null;
        if (await db.Set<WorkOrderFault>().AnyAsync(l => l.FaultId == fault.FaultId && l.ReleasedAt == null, ct)) return null;
        var before = new { wo.UpdatedAt, fault_id = fault.FaultId, linked = false };
        db.Add(new WorkOrderFault { WorkOrderId = id, FaultId = fault.FaultId, CommuneId = fault.CommuneId, LinkedAt = now });
        wo.UpdatedAt = now;
        JobContextFactory.AuditFor(db, correlationId).Record(new(now, AuditActorKind.Iot, null, null,
            fault.CommuneId, AuditEntityType.WorkOrder, id, AuditAction.DetailsChanged, before,
            new { wo.UpdatedAt, fault_id = fault.FaultId, linked = true }));
        var recipients = (await Notifier.ManagersCoveringAsync(db, [fault.CommuneId], ct)).ToList();
        if (wo.AssignedTo is not null) recipients.Add(wo.AssignedTo);
        Notifier.Stage(db, new(NotificationType.WorkOrderAssigned, fault.CommuneId, NotificationEntityType.WorkOrder,
            id, "Bổ sung sự cố IoT vào phiếu kiểm tra", $"{id}: bổ sung {fault.FaultId}, {fault.IotAnomalyCode}."),
            recipients.Distinct().ToArray(), null, now);
        await db.SaveChangesAsync(ct);
        return id;
    }

    public async Task<IReadOnlyDictionary<string, string>> ActiveWorkOrdersAsync(
        IReadOnlyCollection<string> faultIds, CancellationToken ct)
    {
        if (faultIds.Count == 0)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        return await db.Set<WorkOrderFault>().AsNoTracking()
            .Where(link => link.ReleasedAt == null && faultIds.Contains(link.FaultId))
            .ToDictionaryAsync(link => link.FaultId, link => link.WorkOrderId, StringComparer.Ordinal, ct);
    }

    /// <remarks>
    /// The work order side is read with <c>IgnoreQueryFilters</c>: the assignee filter would hide a
    /// repair assigned to someone else from a field engineer, and the answer must not depend on who
    /// asks. Scope still holds — the join starts from the commune-scoped link rows of faults the
    /// caller can already see, and it reads only the task kind.
    /// </remarks>
    public async Task<IReadOnlyDictionary<string, string>> ActiveRepairsAsync(
        IReadOnlyCollection<string> faultIds, CancellationToken ct)
    {
        if (faultIds.Count == 0)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        return await (
                from link in db.Set<WorkOrderFault>().AsNoTracking()
                join order in db.Set<WorkOrder>().IgnoreQueryFilters().AsNoTracking()
                    on link.WorkOrderId equals order.WorkOrderId
                where link.ReleasedAt == null && faultIds.Contains(link.FaultId) && order.TaskKind == TaskKind.Repair
                select new { link.FaultId, link.WorkOrderId })
            .ToDictionaryAsync(row => row.FaultId, row => row.WorkOrderId, StringComparer.Ordinal, ct);
    }
}
