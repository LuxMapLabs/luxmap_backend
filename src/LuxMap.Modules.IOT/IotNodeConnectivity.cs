namespace LuxMap.Modules.IOT;

public sealed partial class IotDiagnosticsService
{
    private sealed record NodeContact(string Status, string ElectricalStatus, DateTime? LastSeenAt, DateTime? LastCommandAckAt);

    private static NodeContact ContactOf(PrototypeDevice? device, IotMeasurement? measurement,
        NodeProfile profile, DateTimeOffset now)
    {
        static DateTimeOffset? Max(IEnumerable<DateTimeOffset?> times) =>
            times.Where(time => time.HasValue).Max();
        bool Received(DateTimeOffset? time) => time is { } value && value != default && value <= now;
        bool Fresh(DateTimeOffset? time) => Received(time) && now - time!.Value <= TimeSpan.FromSeconds(profile.MaxSilenceSeconds);
        var ack = device?.LastCommandAck;
        var oldAck = ack?.Data?.LampCommand is "ON" or "OFF" ? ack.ReceivedAt : (DateTimeOffset?)null;
        var ackAt = Max(new[] { device?.LastCommandAckAt, oldAck }.Where(Received));
        var health = device?.SensorHealth is { } report && report.Data?.IsValidFor(profile.DeviceId) == true
            ? report.ReceivedAt : (DateTimeOffset?)null;
        var seen = Max(new DateTimeOffset?[]
        {
            device?.LastSeenAt, ackAt, health,
            measurement is { } sample ? new DateTimeOffset(sample.ReceivedAt) : null,
        }.Where(Received));
        var measuredAt = MeasuredAt(device?.LatestTelemetry?.Data?.Timestamp);
        // Old Bridge records without RTC use the receipt time. A malformed/future RTC
        // must not make an electrical reading fresh.
        var hasRtc = !string.IsNullOrWhiteSpace(device?.LatestTelemetry?.Data?.Timestamp);
        var electrical = measurement is null ? "no_data"
            : Fresh(new DateTimeOffset(measurement.ReceivedAt)) &&
              (hasRtc ? Fresh(measuredAt) : true) ? "fresh" : "stale";
        return new(seen is null ? "no_data" : Fresh(seen) ? "online" : "offline",
            electrical, seen?.UtcDateTime, ackAt?.UtcDateTime);
    }
}
