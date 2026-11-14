using LuxMap.Modules.Assets.Entities;
using LuxMap.Modules.Telemetry;
using LuxMap.Modules.Telemetry.Entities;
using LuxMap.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LuxMap.Modules.IOT;

/// <summary>One scoped view of configured and persisted Bridge sources, shared by both control channels.</summary>
public sealed class IotFeederSourceLookup(IOptions<IotOptions> options) : IFeederSourceLookup
{
    internal sealed record FeederLink(string NodeId, NodeProfile Profile, bool RemoteControl);

    public async Task<IReadOnlyDictionary<string, string>> ControllersAsync(
        LuxMapDbContext db, IReadOnlyCollection<string> feederIds, CancellationToken ct)
    {
        var (_, links) = await LinksAsync(db, feederIds, ct);
        return links.ToDictionary(pair => pair.Key, pair => pair.Value.NodeId, StringComparer.Ordinal);
    }

    internal async Task<(IotOptions Settings, Dictionary<string, FeederLink> Links)> LinksAsync(
        LuxMapDbContext db, IReadOnlyCollection<string> feederIds, CancellationToken ct)
    {
        var candidates = await (
            from feeder in db.Set<Feeder>().AsNoTracking()
            from node in db.Set<IotNode>().AsNoTracking()
            from control in db.Set<FeederControl>().AsNoTracking()
                .Where(control => control.FeederId == feeder.FeederId).DefaultIfEmpty()
            where feederIds.Contains(feeder.FeederId) && feeder.CabinetId == node.CabinetId &&
                  feeder.CommuneId == node.CommuneId
            select new { feeder.FeederId, node.NodeId, node.CommuneId, node.SupportsRemoteControl,
                ControllerNodeId = control == null ? null : control.NodeId }).ToListAsync(ct);
        var settings = await IotRegistrationStore.ReadAsync(db, options.Value,
            candidates.Select(row => row.NodeId).Distinct().ToArray(), ct);
        var links = new Dictionary<string, FeederLink>(StringComparer.Ordinal);
        foreach (var row in candidates)
        {
            if (!settings.Nodes.TryGetValue(row.NodeId, out var profile) || profile.FeederId != row.FeederId ||
                !IotDiagnosticsService.ServerMatchesCommune(settings, profile, row.CommuneId))
                continue;

            FeederControllerBinding.RequireCompatible(row.FeederId, row.NodeId, row.ControllerNodeId);
            if (links.TryGetValue(row.FeederId, out var other))
                FeederControllerBinding.RequireCompatible(row.FeederId, row.NodeId, other.NodeId);
            links[row.FeederId] = new(row.NodeId, profile, row.SupportsRemoteControl);
        }
        return (settings, links);
    }
}
