using LuxMap.Shared.Authorization;

namespace LuxMap.Modules.WorkOrders.Entities;

/// <summary>
/// "This work order was already reported overdue for this due date" (BE-26, D-4). The primary key IS the rule: one notice per (order, due
/// date) — a new due date is a new key, so an order that is late again after being rescheduled is reported again; reassigning or
/// returning it is not. Kept apart from <c>work_order</c>, which is audited: marking the order would forge an audit event and move its
/// <c>updated_at</c> for something nobody did.
/// </summary>
public sealed class WorkOrderOverdueNotice : ICommuneScoped
{
    public required string WorkOrderId { get; set; }

    public DateOnly DueDate { get; set; }

    public required string CommuneId { get; set; }

    public DateTime NotifiedAt { get; set; }
}
