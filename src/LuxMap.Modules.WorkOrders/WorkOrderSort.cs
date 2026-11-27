using LuxMap.Modules.WorkOrders.Entities;
using LuxMap.Shared.Http;

namespace LuxMap.Modules.WorkOrders;

public enum WorkOrderSortKey
{
    CreatedAt,
    UpdatedAt,
    CompletedAt,
    ClosedAt,
}

/// <summary>
/// The <c>sort</c> parameter of <c>GET /work-orders</c>: a key, optionally prefixed with <c>-</c> for descending (drift WOS-2).
/// </summary>
/// <remarks>
/// The default <c>-created_at</c> is the order the list had before <c>sort</c> existed. NULL <c>completed_at</c> /
/// <c>closed_at</c> sorts LAST in both directions — an order not yet closed is neither the newest nor the oldest closed one.
/// Ties break on <c>created_at, length(work_order_id), work_order_id</c> in the key's direction, never on the ID alone
/// (<c>WO-10000</c> sorts before <c>WO-9999</c> as text).
/// </remarks>
public sealed record WorkOrderSort(WorkOrderSortKey Key, bool Descending)
{
    public static WorkOrderSort Default { get; } = new(WorkOrderSortKey.CreatedAt, Descending: true);

    public static WorkOrderSort Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return Default;
        var trimmed = raw.Trim();
        var descending = trimmed.StartsWith('-');
        return new WorkOrderSort(WireEnum.Parse<WorkOrderSortKey>(descending ? trimmed[1..] : trimmed, "sort"), descending);
    }

    internal static IOrderedQueryable<WorkOrder> Apply(IQueryable<WorkOrder> query, WorkOrderSort sort)
    {
        var ordered = (sort.Key, sort.Descending) switch
        {
            (WorkOrderSortKey.CreatedAt, true) => query.OrderByDescending(x => x.CreatedAt),
            (WorkOrderSortKey.CreatedAt, false) => query.OrderBy(x => x.CreatedAt),
            (WorkOrderSortKey.UpdatedAt, true) => query.OrderByDescending(x => x.UpdatedAt),
            (WorkOrderSortKey.UpdatedAt, false) => query.OrderBy(x => x.UpdatedAt),
            (WorkOrderSortKey.CompletedAt, true) => query.OrderBy(x => x.CompletedAt == null).ThenByDescending(x => x.CompletedAt),
            (WorkOrderSortKey.CompletedAt, false) => query.OrderBy(x => x.CompletedAt == null).ThenBy(x => x.CompletedAt),
            (WorkOrderSortKey.ClosedAt, true) => query.OrderBy(x => x.ClosedAt == null).ThenByDescending(x => x.ClosedAt),
            (WorkOrderSortKey.ClosedAt, false) => query.OrderBy(x => x.ClosedAt == null).ThenBy(x => x.ClosedAt),
            _ => throw new ArgumentOutOfRangeException(nameof(sort), sort, null),
        };
        return sort.Descending
            ? ordered.ThenByDescending(x => x.CreatedAt).ThenByDescending(x => x.WorkOrderId.Length).ThenByDescending(x => x.WorkOrderId)
            : ordered.ThenBy(x => x.CreatedAt).ThenBy(x => x.WorkOrderId.Length).ThenBy(x => x.WorkOrderId);
    }
}
