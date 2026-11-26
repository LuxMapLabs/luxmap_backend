using LuxMap.Shared.Contracts.Enums;

namespace LuxMap.Modules.Assets;

public sealed record FeederHealthSnapshot(FeederHealthState PowerState, FeederPowerState LampState);

/// <summary>One scoped lookup per inventory page for health and actual lamp state together.</summary>
public interface IFeederHealthLookup
{
    Task<IReadOnlyDictionary<string, FeederHealthSnapshot>> ReadHealthAsync(
        IReadOnlyCollection<string> feederIds, CancellationToken ct);
}
