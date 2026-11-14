using LuxMap.Shared.Contracts.Enums;

namespace LuxMap.Modules.Assets;

public interface IFeederPowerLookup
{
    Task<IReadOnlyDictionary<string, FeederPowerState>> ReadAsync(
        IReadOnlyCollection<string> feederIds, CancellationToken ct);
}
