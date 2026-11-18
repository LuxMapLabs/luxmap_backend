using System.Text.Json;

record FeederAverage(long Period, long SampleCount, double VoltageAvg, double CurrentMaAvg, double PowerMwAvg);
record FeederAverageBatch(int Total, IReadOnlyList<long> Periods, IReadOnlyList<FeederAverage> Items);

sealed partial class TelemetryArchive
{
    public FeederAverageBatch ReadFeederAverages(FeederTelemetryWindow[] windows, long skip, int take,
        bool measuredAt, DateTimeOffset? from, DateTimeOffset? to, bool keysOnly = false)
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction(deferred: true);
        using var sql = connection.CreateCommand();
        sql.Transaction = transaction;
        var bounds = windows.Distinct().Select(window => new
        {
            window.NodeId, window.ServerId, window.DeviceId,
            fromRtc = RtcBound(window.From), untilRtc = window.Until is { } until ? RtcBound(until) : null,
            fromReceipt = ReceiptBound(window.From),
            untilReceipt = window.Until is { } end ? ReceiptBound(end) : null,
        });
        sql.Parameters.AddWithValue("$bounds", JsonSerializer.Serialize(bounds, Json));
        // DS3231 timestamps have no offset and are explicitly Asia/Ho_Chi_Minh (UTC+07).
        // Apply query bounds before grouping; the last bucket can therefore be partial.
        var timestamp = measuredAt
            ? "strftime('%Y-%m-%dT%H:%M:%S', rtc, '-7 hours') || '.0000000'"
            : "receipt";
        var query = FeederReadingsSql + $$"""
            , samples AS (
                SELECT DISTINCT id, reading, {{timestamp}} AS sample_stamp,
                       json_extract(reading, '$.data.voltage') AS voltage,
                       json_extract(reading, '$.data.current') AS current,
                       json_extract(reading, '$.data.power') AS power
                FROM readings
            ),
            valid AS (
                SELECT *, CAST(strftime('%s', substr(sample_stamp, 1, 19)) AS INTEGER) / 30 * 30 AS period
                FROM samples
                WHERE sample_stamp IS NOT NULL AND sample_stamp >= '1970-01-01T00:00:00.0000000'
                  AND ($from IS NULL OR sample_stamp >= $from)
                  AND ($to IS NULL OR sample_stamp < $to)
                  AND json_type(reading, '$.data.voltage') IN ('integer', 'real')
                  AND json_type(reading, '$.data.current') IN ('integer', 'real')
                  AND json_type(reading, '$.data.power') IN ('integer', 'real')
                  AND abs(voltage) < 1e100 AND abs(current) < 1e100 AND abs(power) < 1e100
            ),
            buckets AS (
                SELECT period, count(*) AS samples, avg(voltage) AS voltage,
                       avg(current) AS current, avg(power) AS power
                FROM valid GROUP BY period
            )
            """;
        sql.Parameters.AddWithValue("$from", (object?)(from is { } lower ? ReceiptBound(lower) : null) ?? DBNull.Value);
        sql.Parameters.AddWithValue("$to", (object?)(to is { } upper ? ReceiptBound(upper) : null) ?? DBNull.Value);
        sql.CommandText = query + "SELECT period FROM buckets ORDER BY period DESC;";
        var periods = new List<long>();
        using (var reader = sql.ExecuteReader())
            while (reader.Read()) periods.Add(reader.GetInt64(0));
        if (keysOnly || skip >= periods.Count) return new(periods.Count, periods, []);

        sql.CommandText = query + "SELECT period, samples, voltage, current, power FROM buckets ORDER BY period DESC LIMIT $take OFFSET $skip;";
        sql.Parameters.AddWithValue("$take", take);
        sql.Parameters.AddWithValue("$skip", skip);
        var items = new List<FeederAverage>();
        using (var reader = sql.ExecuteReader())
            while (reader.Read()) items.Add(new(reader.GetInt64(0), reader.GetInt64(1),
                reader.GetDouble(2), reader.GetDouble(3), reader.GetDouble(4)));
        return new(periods.Count, periods, items);
    }
}
