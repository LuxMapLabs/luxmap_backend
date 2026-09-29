using LuxMap.Modules.WorkOrders.Entities;
using LuxMap.Shared.Contracts.Enums;

namespace LuxMap.Modules.WorkOrders;

public static class WorkOrderRules
{
    public static bool Eligible(TaskKind kind, FaultStatus status)
        => FaultStatusSets.IsOpen(status) && (kind == TaskKind.Inspection || status != FaultStatus.Detected);

    public static bool Allows(WorkOrderStatus status, string action) => action switch
    {
        "assign" or "reassign" or "unassign" or "edit" or "cancel" =>
            status is WorkOrderStatus.Open or WorkOrderStatus.Assigned or WorkOrderStatus.InProgress,
        "start" => status == WorkOrderStatus.Assigned,
        "complete" => status == WorkOrderStatus.InProgress,
        "verify" or "return" => status == WorkOrderStatus.Done,
        "follow_up" => status == WorkOrderStatus.Verified,
        _ => false,
    };

    /// <summary>Which kinds may follow a verified work order of this kind (drift FR-2). Survey joins with BE-15.</summary>
    public static TaskKind[] FollowUpKinds(TaskKind parent) => parent switch
    {
        TaskKind.Inspection => [TaskKind.Repair],
        _ => [],
    };

    public static string[] AllowedActions(WorkOrderStatus status, UserRole? role, bool assigned, TaskKind kind)
    {
        string[] actions = role switch
        {
            UserRole.Manager => FollowUpKinds(kind).Length > 0
                ? ["assign", "unassign", "edit", "verify", "return", "cancel", "follow_up"]
                : ["assign", "unassign", "edit", "verify", "return", "cancel"],
            UserRole.FieldEngineer when assigned => ["start", "complete"],
            _ => [],
        };
        return actions.Where(action => Allows(status, action)).ToArray();
    }
}
