using LuxMap.Shared.Contracts.Enums;

namespace LuxMap.Modules.Telemetry;

/// <summary>
/// The one rule for a silent device (I-3). The threshold is the <c>iot_offline_after_minutes</c> system setting (BE-33), read by the
/// caller from <c>SystemSettingValues</c>; the map and the <c>node_offline</c> job both come through here.
/// </summary>
public static class NodeSilence
{
    /// <summary>
    /// <c>never_reported</c> when the device never reported; <c>offline</c> when its last report is OLDER than
    /// <paramref name="offlineAfter"/>; otherwise <c>online</c>. Exactly at the threshold is still online — "silent for longer than"
    /// the threshold.
    /// </summary>
    public static NodeStatus StatusAt(DateTime? lastReportAt, DateTime now, TimeSpan offlineAfter)
        => lastReportAt is not { } last ? NodeStatus.NeverReported
            : now - last > offlineAfter ? NodeStatus.Offline
            : NodeStatus.Online;
}
