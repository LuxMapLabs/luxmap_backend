using LuxMap.Shared.Contracts.Enums;

namespace LuxMap.Modules.Assets;

/// <summary>Assets-owned port: one scoped lookup for all feeders in an inventory page.</summary>
public interface IFeederControlLookup
{
    Task<IReadOnlyDictionary<string, FeederControlSnapshot>> ControlsAsync(
        IReadOnlyCollection<string> feederIds, CancellationToken ct);
}

/// <summary>A connected relay, including the device's historical report even when it is offline.</summary>
public sealed record FeederControlSnapshot(string NodeId, short RelayNo, FeederControlMode? ControlMode, DateTime? ModeReportedAt);
