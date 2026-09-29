using LuxMap.Modules.Faults;
using LuxMap.Modules.WorkOrders.Entities;
using LuxMap.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LuxMap.Modules.WorkOrders;

/// <summary>Answers <see cref="IActiveWorkOrderLookup"/> from the <c>work_order_fault</c> link table.</summary>
/// <remarks>
/// Reads <see cref="WorkOrderFault"/>, which is commune-scoped only — not assignee-scoped like
/// <see cref="WorkOrder"/> — so the ID comes back whoever the work order is assigned to.
/// </remarks>
public sealed class ActiveWorkOrderLookup(LuxMapDbContext db) : IActiveWorkOrderLookup
{
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
