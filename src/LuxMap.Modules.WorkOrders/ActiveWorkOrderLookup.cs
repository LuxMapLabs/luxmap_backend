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
}
