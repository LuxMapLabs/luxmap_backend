using LuxMap.Modules.WorkOrders.Entities;
using LuxMap.Shared.Contracts.Enums;

namespace LuxMap.Modules.WorkOrders;

public static class WorkOrderRules
{
    public static bool Eligible(TaskKind kind, FaultStatus status)
        => kind != TaskKind.Survey && FaultStatusSets.IsOpen(status) && (kind == TaskKind.Inspection || status != FaultStatus.Detected);

    public static bool Allows(WorkOrderStatus status, string action,
        CancellationRequestStatus cancellation = CancellationRequestStatus.None) => action switch
    {
        "assign" or "reassign" or "unassign" or "edit" or "cancel" =>
            status is WorkOrderStatus.Open or WorkOrderStatus.Assigned or WorkOrderStatus.InProgress,
        "request_cancellation" => status == WorkOrderStatus.Assigned
            && cancellation is CancellationRequestStatus.None or CancellationRequestStatus.Rejected,
        "reject_cancellation" => cancellation == CancellationRequestStatus.Pending,
        "start" => status == WorkOrderStatus.Assigned,
        "complete" => status == WorkOrderStatus.InProgress,
        "verify" or "return" => status == WorkOrderStatus.Done,
        "follow_up" => status == WorkOrderStatus.Verified,
        _ => false,
    };

    /// <summary>Which kinds may follow a verified work order of this kind (drift FR-2, WSC-1).</summary>
    public static TaskKind[] FollowUpKinds(TaskKind parent) => parent switch
    {
        TaskKind.Survey => [TaskKind.Inspection],
        TaskKind.Inspection => [TaskKind.Repair],
        _ => [],
    };

    public static string[] AllowedActions(WorkOrderStatus status, UserRole? role, bool assigned, TaskKind kind,
        CancellationRequestStatus cancellation = CancellationRequestStatus.None)
    {
        string[] actions = role switch
        {
            UserRole.Manager => FollowUpKinds(kind).Length > 0
                ? ["assign", "unassign", "edit", "verify", "return", "cancel", "follow_up", "reject_cancellation"]
                : ["assign", "unassign", "edit", "verify", "return", "cancel", "reject_cancellation"],
            UserRole.FieldEngineer when assigned => ["start", "complete", "request_cancellation"],
            _ => [],
        };
        return actions.Where(action => Allows(status, action, cancellation)).ToArray();
    }
}
