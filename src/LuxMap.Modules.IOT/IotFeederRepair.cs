using LuxMap.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LuxMap.Modules.IOT;

public sealed record IotFeederRepairPeriod(string WorkOrderId, DateTime StartedAt, DateTime? EndedAt);

public sealed partial class IotDiagnosticsService
{
    private Task<FeederRepairPeriod?> LatestFeederRepairAsync(string feederId, CancellationToken ct) =>
        db.Set<FeederRepairPeriod>().AsNoTracking().Where(x => x.FeederId == feederId)
            .OrderByDescending(x => x.StartedAt).ThenByDescending(x => x.Id).FirstOrDefaultAsync(ct);

    private static PrototypeDevice? AfterFeederRepair(PrototypeDevice? device, DateTime? endedAt)
    {
        if (device is null || endedAt is null) return device;
        bool MeasuredAfter(PrototypeReading row) => MeasuredAt(row.Data?.Timestamp) is { } at && at > endedAt.Value;
        return device with {
            LatestTelemetry = device.LatestTelemetry is { } latest && MeasuredAfter(latest) ? latest : null,
            History = device.History?.Where(MeasuredAfter).ToArray(),
            SensorHealth = device.SensorHealth?.ReceivedAt > endedAt.Value ? device.SensorHealth : null,
            LastSeenAt = device.LastSeenAt > endedAt.Value ? device.LastSeenAt : null,
            LastCommandAckAt = device.LastCommandAckAt > endedAt.Value ? device.LastCommandAckAt : null,
            LastCommandAck = device.LastCommandAck?.ReceivedAt > endedAt.Value ? device.LastCommandAck : null,
            LastSuccessfulAck = device.LastSuccessfulAck?.ReceivedAt > endedAt.Value ? device.LastSuccessfulAck : null,
        };
    }

    internal static IotDiagnosticResult RepairingDiagnosis(string nodeId, string feederId) =>
        new(nodeId, feederId, "repairing", null, []) { ElectricalStatus = "repairing" };
}
