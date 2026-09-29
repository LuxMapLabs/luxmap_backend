using LuxMap.Modules.Faults.Entities;
using LuxMap.Shared.Contracts.Enums;

namespace LuxMap.Modules.Faults;

/// <summary>What a Manager may still do to a fault (BE-19). One source for <c>allowed_actions</c> and PATCH.</summary>
public static class FaultReviewRules
{
    /// <summary>The only types a Manager may move a fault between (D-4).</summary>
    public static readonly IReadOnlyList<FaultType> Reclassifiable = [FaultType.LampOut, FaultType.LampDim];

    /// <summary>
    /// A fault is reviewable while it is open and no repair holds it (WO-10). Advisory for the UI:
    /// a concurrent change can still turn the PATCH into a 409.
    /// </summary>
    public static string[] AllowedActions(Fault fault, bool heldByRepair)
    {
        if (heldByRepair || !FaultStatusSets.IsOpen(fault.FaultStatus))
        {
            return [];
        }

        var actions = new List<string>();
        if (fault.FaultStatus == FaultStatus.Detected)
        {
            actions.Add("confirm");
            actions.Add("reject");
        }

        if (Reclassifiable.Contains(fault.EffectiveType))
        {
            actions.Add("reclassify");
        }

        actions.Add("set_severity");
        actions.Add("edit_note");
        return [.. actions];
    }

    /// <summary>The only transitions PATCH makes (D-2); the rest belong to repair work orders.</summary>
    public static bool IsReviewTransition(FaultStatus from, FaultStatus to)
        => from == FaultStatus.Detected && to is FaultStatus.Confirmed or FaultStatus.Rejected;
}
