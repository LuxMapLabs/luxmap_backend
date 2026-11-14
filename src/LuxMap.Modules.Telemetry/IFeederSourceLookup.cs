using System.Net;
using LuxMap.Modules.Assets.Entities;
using LuxMap.Persistence;
using LuxMap.Shared.Contracts.Errors;
using LuxMap.Shared.Http;
using Microsoft.EntityFrameworkCore;

namespace LuxMap.Modules.Telemetry;

/// <summary>Bridge ownership, implemented by IOT without a reverse Telemetry → IOT project reference.</summary>
public interface IFeederSourceLookup
{
    /// <summary>The caller supplies its scoped context, including the fixed commune context used by MQTT.</summary>
    Task<IReadOnlyDictionary<string, string>> ControllersAsync(
        LuxMapDbContext db, IReadOnlyCollection<string> feederIds, CancellationToken ct);
}

/// <summary>Temporary cross-channel invariant while both relay and Bridge control paths remain in service.</summary>
public static class FeederControllerBinding
{
    public static void RequireCompatible(string feederId, string nodeId, string? controllerNodeId)
    {
        if (controllerNodeId is not null && controllerNodeId != nodeId)
        {
            throw new LuxMapException("IOT_REGISTRATION_CONFLICT", HttpStatusCode.Conflict,
                "That feeder is assigned to different nodes across its IoT sources or control channels.",
                new Dictionary<string, object?>
                {
                    ["feeder_id"] = feederId, ["node_id"] = nodeId, ["controller_node_id"] = controllerNodeId,
                });
        }
    }

    /// <summary>Call after locking the node. Both source registration and relay wiring use node → feeder lock order.</summary>
    public static async Task<Feeder> LockFeederAsync(LuxMapDbContext db, string feederId, CancellationToken ct)
    {
        var communeId = await db.Set<Feeder>().Where(feeder => feeder.FeederId == feederId)
            .Select(feeder => feeder.CommuneId).FirstOrDefaultAsync(ct)
            ?? throw new LuxMapException(ErrorCodes.AssetNotFound, HttpStatusCode.NotFound,
                "That feeder does not exist, or it is outside your permitted commune scope.");
        await db.Database.ExecuteSqlAsync(
            $"SELECT 1 FROM feeder WHERE feeder_id = {feederId} AND commune_id = {communeId} FOR UPDATE", ct);
        return await db.Set<Feeder>().AsNoTracking().SingleAsync(feeder => feeder.FeederId == feederId, ct);
    }
}
