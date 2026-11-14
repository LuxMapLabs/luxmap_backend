using LuxMap.Modules.Assets;
using LuxMap.Modules.Telemetry.Entities;
using LuxMap.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LuxMap.Modules.Telemetry;

/// <summary>Answers <see cref="ICabinetDeviceLookup"/> from <c>iot_node</c> and <c>feeder_control</c>.</summary>
public sealed class CabinetDeviceLookup(LuxMapDbContext db) : ICabinetDeviceLookup
{
    public async Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> DevicesAsync(
        IReadOnlyCollection<string> cabinetIds, CancellationToken ct)
    {
        if (cabinetIds.Count == 0)
        {
            return new Dictionary<string, IReadOnlyList<string>>();
        }

        var nodes = await db.Set<IotNode>().AsNoTracking()
            .Where(node => cabinetIds.Contains(node.CabinetId))
            .OrderBy(node => node.CreatedAt).ThenBy(node => node.NodeId.Length).ThenBy(node => node.NodeId)
            .Select(node => new { node.CabinetId, node.NodeId })
            .ToListAsync(ct);

        return nodes.GroupBy(node => node.CabinetId).ToDictionary(
            group => group.Key, group => (IReadOnlyList<string>)[.. group.Select(node => node.NodeId)], StringComparer.Ordinal);
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
