using LuxMap.Modules.Assets;
using LuxMap.Modules.Telemetry.Entities;
using LuxMap.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LuxMap.Modules.Telemetry;

/// <summary>Answers <see cref="ICabinetDeviceLookup"/> from <c>iot_node</c> and <c>feeder_control</c>.</summary>
public sealed class CabinetDeviceLookup(LuxMapDbContext db) : ICabinetDeviceLookup
{
    public async Task<IReadOnlyDictionary<string, string>> DevicesAsync(
        IReadOnlyCollection<string> cabinetIds, CancellationToken ct)
    {
        if (cabinetIds.Count == 0)
        {
            return new Dictionary<string, string>();
        }

        // At most one device per cabinet (ux_iot_node_cabinet_id), so the dictionary cannot collide.
        return await db.Set<IotNode>().AsNoTracking()
            .Where(node => cabinetIds.Contains(node.CabinetId))
            .ToDictionaryAsync(node => node.CabinetId, node => node.NodeId, StringComparer.Ordinal, ct);
    }

    public async Task<IReadOnlyDictionary<string, string>> ControllersAsync(
        IReadOnlyCollection<string> feederIds, CancellationToken ct)
    {
        if (feederIds.Count == 0)
        {
            return new Dictionary<string, string>();
        }

        // feeder_id is the key of feeder_control: zero or one device per feeder (I-12).
        return await db.Set<FeederControl>().AsNoTracking()
            .Where(control => feederIds.Contains(control.FeederId))
            .ToDictionaryAsync(control => control.FeederId, control => control.NodeId, StringComparer.Ordinal, ct);
    }
}
