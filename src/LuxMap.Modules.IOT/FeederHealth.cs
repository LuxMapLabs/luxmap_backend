using LuxMap.Modules.Assets;
using LuxMap.Persistence;
using LuxMap.Shared.Contracts.Enums;
using LuxMap.Shared.Http;
using Microsoft.EntityFrameworkCore;

namespace LuxMap.Modules.IOT;

internal static class FeederHealthClassifier
{
    internal static FeederHealthState Classify(IotDiagnosticResult diagnosis)
    {
        if (diagnosis.ConnectionStatus == "offline") return FeederHealthState.Offline;
        if (diagnosis.Anomalies.Any(IsIncident)) return FeederHealthState.Incident;
        if (diagnosis.ConnectionStatus != "online" || diagnosis.ElectricalStatus != "fresh" ||
            diagnosis.Status != "normal" || diagnosis.Anomalies.Count > 0 ||
            (diagnosis.Measurement?.LampCommand == "ON" && diagnosis.ElectricalWindow?.Status != "evaluated"))
            return FeederHealthState.Warning;
        return FeederHealthState.Normal;
    }

    private static bool IsIncident(IotAnomaly anomaly) => anomaly.Code switch
    {
        "voltage_loss" or "ina226_unavailable" or "ina226_read_failed" or "ina226_shunt_out_of_range" => true,
        "current_drop" => anomaly.Reference is > 0 && anomaly.Observed is { } current
            ? current <= anomaly.Reference.Value * 0.8 : anomaly.DeviationPercent <= -20,
        "voltage_deviation" => anomaly.Reference is > 0 && anomaly.Observed is { } voltage
            ? Math.Abs(voltage - anomaly.Reference.Value) >= anomaly.Reference.Value * 0.2
            : Math.Abs(anomaly.DeviationPercent ?? 0) >= 20,
        _ => false,
    };
}

public sealed partial class IotDiagnosticsService
{
    public async Task<IReadOnlyDictionary<string, FeederHealthSnapshot>> ReadHealthAsync(
        IReadOnlyCollection<string> feederIds, CancellationToken ct)
    {
        var result = new System.Collections.Concurrent.ConcurrentDictionary<string, FeederHealthSnapshot>(
            feederIds.Distinct().ToDictionary(id => id, _ => new FeederHealthSnapshot(FeederHealthState.Unknown, FeederPowerState.Unknown)));
        if (feederIds.Count == 0) return result;
        var repairs = await db.Set<FeederRepairPeriod>().AsNoTracking()
            .Where(x => feederIds.Contains(x.FeederId)).GroupBy(x => x.FeederId)
            .Select(group => new { FeederId = group.Key, Active = group.Any(x => x.EndedAt == null), EndedAt = group.Max(x => x.EndedAt) })
            .ToDictionaryAsync(x => x.FeederId, ct);
        foreach (var repair in repairs.Values.Where(x => x.Active))
            result[repair.FeederId] = new(FeederHealthState.Repairing, FeederPowerState.Unknown);
        var monitoredIds = feederIds.Where(id => !repairs.TryGetValue(id, out var repair) || !repair.Active).ToArray();
        if (monitoredIds.Length == 0) return result;
        var (settings, links) = await sources.LinksAsync(db, monitoredIds, ct);
        foreach (var id in links.Keys) result[id] = new(FeederHealthState.Warning, FeederPowerState.Unknown);
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            // All scoped DB reads are complete before parallel HTTP work begins.
            await Parallel.ForEachAsync(links, new ParallelOptions { MaxDegreeOfParallelism = 12, CancellationToken = budget.Token },
                async (entry, token) =>
                {
                    var profile = entry.Value.Profile;
                    if (!profile.IsValid() || !TryServer(settings, profile, out _, out var uri)) return;
                    try
                    {
                        var device = AfterFeederRepair(await ReadDeviceAsync(uri, profile.DeviceId, token),
                            repairs.GetValueOrDefault(entry.Key)?.EndedAt);
                        var now = clock.GetUtcNow();
                        var diagnosis = Diagnose(entry.Value.NodeId, profile, device, now);
                        result[entry.Key] = new(FeederHealthClassifier.Classify(diagnosis), PowerStateOf(device, profile.MaxSilenceSeconds, now));
                    }
                    catch (LuxMapException error) when (error.Code == "IOT_SOURCE_UNAVAILABLE") { }
                });
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested && budget.IsCancellationRequested) { }
        return result;
    }
}
