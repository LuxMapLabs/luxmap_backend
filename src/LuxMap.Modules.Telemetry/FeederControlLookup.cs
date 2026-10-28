using LuxMap.Modules.Assets;
using LuxMap.Modules.Telemetry.Entities;
using LuxMap.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LuxMap.Modules.Telemetry;

/// <summary>Both relay and node retain their commune query filters.</summary>
public sealed class FeederControlLookup(LuxMapDbContext db) : IFeederControlLookup
{
    public async Task<IReadOnlyDictionary<string, FeederControlSnapshot>> ControlsAsync(
        IReadOnlyCollection<string> feederIds, CancellationToken ct)
    {
        if (feederIds.Count == 0) return new Dictionary<string, FeederControlSnapshot>();

        return await (from control in db.Set<FeederControl>().AsNoTracking()
                      join node in db.Set<IotNode>().AsNoTracking() on control.NodeId equals node.NodeId
                      where feederIds.Contains(control.FeederId)
                      select new { control.FeederId, Snapshot = new FeederControlSnapshot(
                          node.NodeId, control.RelayNo, control.ControlMode, control.ModeReportedAt) })
            .ToDictionaryAsync(row => row.FeederId, row => row.Snapshot, StringComparer.Ordinal, ct);
    }
}
