using LuxMap.Modules.Assets.Entities;
using LuxMap.Modules.Faults.Entities;
using LuxMap.Modules.WorkOrders;
using LuxMap.Persistence;
using LuxMap.Shared.Contracts.Enums;
using Microsoft.EntityFrameworkCore;

namespace LuxMap.Modules.Survey.Review;

public sealed class RepairStatusPublisher(LuxMapDbContext db) : IRepairStatusPublication
{
    public async Task<IReadOnlyList<RepairStatusOutcome>> StageAsync(IReadOnlyCollection<string> poleIds,
        IReadOnlyCollection<string> verifiedFaultIds, DateTime observedAt, DateTime now, CancellationToken ct)
    {
        var outcomes = new List<RepairStatusOutcome>();
        var open = FaultStatusSets.Open.ToArray();
        foreach (var poleId in poleIds.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            // A fault's pole FK does not enforce the same commune. Never bypass the pole's read scope.
            var pole = await db.Set<Pole>().AsNoTracking().SingleOrDefaultAsync(x => x.PoleId == poleId, ct);
            if (pole is null) continue;
            // The verified transitions are staged, so the database still considers those faults open.
            if (await db.Set<Fault>().AnyAsync(x => x.PoleId == poleId && !verifiedFaultIds.Contains(x.FaultId)
                && open.Contains(x.FaultStatus)
                && ((x.OverrideFaultType ?? x.FaultType) == FaultType.LampOut
                    || (x.OverrideFaultType ?? x.FaultType) == FaultType.LampDim), ct))
            {
                outcomes.Add(new(poleId, false, "other_open_fault"));
                continue;
            }
            var applied = await PoleStatusPublication.StageAsync(db, poleId, pole.CommuneId,
                FixtureStatus.Normal, null, observedAt, now, null, null, ct);
            outcomes.Add(new(poleId, applied, applied ? "applied" : "not_newer"));
        }
        return outcomes;
    }
}
