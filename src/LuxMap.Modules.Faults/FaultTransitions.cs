using System.Net;
using LuxMap.Modules.Faults.Entities;
using LuxMap.Shared.Contracts.Enums;
using LuxMap.Shared.Http;

namespace LuxMap.Modules.Faults;

/// <summary>Stages a fault transition; the caller owns SaveChanges and the shared audit event.</summary>
public sealed class FaultTransitions
{
    public void Apply(Fault fault, FaultStatus target, DateTime now, string actor,
        DateTime? resolvedAt = null)
    {
        var allowed = (fault.FaultStatus, target) switch
        {
            (FaultStatus.Detected, FaultStatus.Confirmed or FaultStatus.Rejected) => true,
            (FaultStatus.Confirmed, FaultStatus.InProgress) => true,
            (FaultStatus.InProgress, FaultStatus.Resolved) => true,
            (FaultStatus.Resolved, FaultStatus.Verified) => true,
            _ => false,
        };
        if (!allowed)
        {
            throw new LuxMapException("INVALID_STATE_TRANSITION", HttpStatusCode.Conflict, "Invalid fault transition.");
        }
        if (target is FaultStatus.Confirmed or FaultStatus.Rejected)
        {
            fault.ConfirmedBy = actor;
            fault.ConfirmedAt = now;
        }
        if (target == FaultStatus.Resolved)
        {
            fault.ResolvedBy = actor;
            fault.ResolvedAt = resolvedAt ?? now;
        }
        fault.FaultStatus = target;
        fault.UpdatedAt = now;
    }
}
