using System.Globalization;
using System.Text.Json;

record AutoHistoryWindow(string DeviceId, string FeederId, DateTimeOffset From, DateTimeOffset? Until);
record AutoHistoryEvent(string EventId, string DeviceId, string FeederId, string Command,
    DateTimeOffset OccurredAt, DateTimeOffset ReceivedAt, string? AutoType = null);

sealed partial class TelemetryArchive
{
    // Telemetry is the durable event source, independent of the bounded devices.json display cache.
    // RTC ordering makes replay reconstruct the same transitions without duplicate log entries.
    public IReadOnlyList<AutoHistoryEvent> ReadAutoTransitions(AutoHistoryWindow[] windows)
    {
        using var connection = Open();
        using var sql = connection.CreateCommand();
        sql.CommandText = """
            WITH samples AS (
                SELECT id, device_id, reading,
                    json_extract(reading, '$.data.mode') AS mode,
                    json_extract(reading, '$.data.lampCommand') AS lamp,
                    coalesce(json_extract(reading, '$.data.timestamp'),
                        strftime('%Y-%m-%dT%H:%M:%S', json_extract(reading, '$.receivedAt'), '+7 hours')) AS rtc,
                    json_extract(reading, '$.data.sessionId') AS session,
                    json_extract(reading, '$.data.sequenceNumber') AS seq
                FROM telemetry
                WHERE device_id IN (SELECT json_extract(value, '$.deviceId') FROM json_each($windows))
            ), ordered AS (
                SELECT *,
                    lag(mode) OVER timeline AS previous_mode,
                    lag(lamp) OVER timeline AS previous_lamp,
                    lag(rtc) OVER timeline AS previous_rtc
                FROM samples
                WINDOW timeline AS (PARTITION BY device_id ORDER BY rtc, session, seq, id)
            )
            SELECT id, device_id, reading, rtc, previous_rtc
            FROM ordered WHERE mode = 'AUTO' AND previous_mode = 'AUTO'
                AND lamp IN ('ON', 'OFF') AND previous_lamp IN ('ON', 'OFF') AND lamp <> previous_lamp
            ORDER BY rtc DESC, id DESC;
            """;
        sql.Parameters.AddWithValue("$windows", JsonSerializer.Serialize(windows, Json));
        using var reader = sql.ExecuteReader();
        var events = new List<AutoHistoryEvent>();
        while (reader.Read())
        {
            var reading = Read(reader.GetString(2));
            var occurred = DateTimeOffset.ParseExact(reader.GetString(3), "yyyy-MM-dd'T'HH:mm:ss",
                CultureInfo.InvariantCulture, DateTimeStyles.None);
            occurred = new DateTimeOffset(occurred.DateTime, TimeSpan.FromHours(7)).ToUniversalTime();
            var previous = new DateTimeOffset(DateTime.ParseExact(reader.GetString(4), "yyyy-MM-dd'T'HH:mm:ss",
                CultureInfo.InvariantCulture), TimeSpan.FromHours(7)).ToUniversalTime();
            var device = reader.GetString(1);
            foreach (var window in windows.Where(w => w.DeviceId == device && previous >= w.From &&
                occurred >= w.From && (w.Until == null || occurred < w.Until)).Distinct())
            {
                events.Add(new("sensor-" + reader.GetInt64(0).ToString(CultureInfo.InvariantCulture),
                    device, window.FeederId, reading.Data.LampCommand!, occurred, reading.ReceivedAt, reading.Data.AutoType ?? "scheduled"));
            }
        }
        return events;
    }
}
