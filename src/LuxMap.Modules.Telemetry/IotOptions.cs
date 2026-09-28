using LuxMap.Shared.Contracts.Enums;

namespace LuxMap.Modules.Telemetry;

/// <summary>
/// IoT settings. <c>Iot:OfflineAfter</c> in appsettings, or <c>Iot__OfflineAfter</c> in the
/// environment; a restart applies it. BE-33 may later move it to runtime configuration.
/// </summary>
public sealed record IotOptions
{
    public const string SectionName = "Iot";

    /// <summary>
    /// A device silent for longer than this is <c>offline</c> (I-3). One hour by decision of
    /// 28/09/2026; IOT-11 should raise <c>node_offline</c> from the same value.
    /// </summary>
    public TimeSpan OfflineAfter { get; init; } = TimeSpan.FromHours(1);

    /// <summary>A non-positive threshold would mark every device offline, so startup STOPS.</summary>
    public void Validate()
    {
        if (OfflineAfter <= TimeSpan.Zero)
        {
            throw new InvalidOperationException(
                $"{SectionName}:OfflineAfter must be greater than 0; got {OfflineAfter}.");
        }
    }

    /// <summary>
    /// <c>never_reported</c> when the device never reported; <c>offline</c> when its last report is
    /// OLDER than <see cref="OfflineAfter"/>; otherwise <c>online</c>. Exactly at the threshold is
    /// still online — "silent for longer than" the threshold.
    /// </summary>
    public NodeStatus StatusAt(DateTime? lastReportAt, DateTime now)
        => lastReportAt is not { } last ? NodeStatus.NeverReported
            : now - last > OfflineAfter ? NodeStatus.Offline
            : NodeStatus.Online;
}
