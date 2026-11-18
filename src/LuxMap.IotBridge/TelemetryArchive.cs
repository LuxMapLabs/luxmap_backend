using System.Text.Json;
using Microsoft.Data.Sqlite;

record ArchivedTelemetry(long Cursor, TelemetryReading Reading);
record TelemetryPage(IReadOnlyList<ArchivedTelemetry> Items, long NextCursor, bool HasMore);

record TelemetryHistoryPage(int Page, int PageSize, int Total, IReadOnlyList<ArchivedTelemetry> Items);

record CommandReceiptKey(string DeviceId, string CommandId);
record CommandReceipt(string DeviceId, string CommandId, DateTimeOffset ReceivedAt);

record FeederTelemetryWindow(string NodeId, string ServerId, string DeviceId, DateTimeOffset From, DateTimeOffset? Until);
record FeederArchivedTelemetry(long Cursor, TelemetryReading Reading, string NodeId, string ServerId, bool Active);
record FeederTelemetryBatch(int Total, IReadOnlyList<FeederArchivedTelemetry> Items);

sealed partial class TelemetryArchive
{
    private readonly string connectionString;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public TelemetryArchive(string directory, IEnumerable<DeviceState> existingDevices)
    {
        connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(directory, "telemetry.sqlite"),
        }.ToString();
        using var connection = Open();
        using var schema = connection.CreateCommand();
        schema.CommandText = """
            PRAGMA journal_mode = WAL;
            CREATE TABLE IF NOT EXISTS telemetry (
                id INTEGER PRIMARY KEY,
                device_id TEXT NOT NULL,
                sample_key TEXT NOT NULL,
                reading TEXT NOT NULL,
                UNIQUE (device_id, sample_key)
            );
            CREATE TABLE IF NOT EXISTS command_receipts (
                device_id TEXT NOT NULL,
                command_id TEXT NOT NULL,
                mode TEXT NOT NULL,
                command TEXT,
                received_at TEXT,
                PRIMARY KEY (device_id, command_id)
            );
            CREATE INDEX IF NOT EXISTS telemetry_device_id ON telemetry (device_id, id);
            CREATE INDEX IF NOT EXISTS telemetry_device_measured_at
                ON telemetry (device_id, json_extract(reading, '$.data.timestamp') DESC, id DESC);
            CREATE INDEX IF NOT EXISTS telemetry_device_received_at
                ON telemetry (device_id, json_extract(reading, '$.receivedAt') DESC, id DESC);
            """;
        schema.ExecuteNonQuery();

        using var transaction = connection.BeginTransaction();
        foreach (var device in existingDevices)
            foreach (var reading in device.History)
                Insert(connection, transaction, device.DeviceId, reading);
        transaction.Commit();

        foreach (var device in existingDevices)
        {
            TrackCommand(device.DeviceId, device.DesiredCommand);
            // Older Bridge versions retained one already validated ACK per device.
            if (device.LastCommandAck is { Data: { CommandId: { } id, Mode: "AUTO" or "MANUAL" } ack } previous &&
                Guid.TryParseExact(id, "N", out _))
            {
                TrackCommand(device.DeviceId, new(ack.Mode, ack.Mode == "MANUAL" ? ack.LampCommand : null, id));
                RecordReceipt(ack, previous.ReceivedAt);
            }
        }
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(connectionString);
        connection.Open();
        return connection;
    }

    private static string SampleKey(TelemetryReading reading, string serialized) =>
        reading.Data.SessionId is { } session && reading.Data.SequenceNumber is { } sequence
            ? $"{session}:{sequence}"
            : $"legacy:{serialized}";

    private static bool Insert(SqliteConnection connection, SqliteTransaction? transaction,
        string deviceId, TelemetryReading reading)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO telemetry (device_id, sample_key, reading) VALUES ($device, $key, $reading)
            ON CONFLICT (device_id, sample_key) DO NOTHING;
            """;
        var serialized = JsonSerializer.Serialize(reading, Json);
        command.Parameters.AddWithValue("$device", deviceId);
        command.Parameters.AddWithValue("$key", SampleKey(reading, serialized));
        command.Parameters.AddWithValue("$reading", serialized);
        return command.ExecuteNonQuery() == 1;
    }

    public TelemetryReading Save(string deviceId, TelemetryReading reading)
    {
        using var connection = Open();
        if (Insert(connection, null, deviceId, reading)) return reading;

        // If the archive committed before the snapshot failed, resume with the original receipt time.
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT reading FROM telemetry WHERE device_id = $device AND sample_key = $key;";
        command.Parameters.AddWithValue("$device", deviceId);
        command.Parameters.AddWithValue("$key", SampleKey(reading, JsonSerializer.Serialize(reading, Json)));
        return Read((string?)command.ExecuteScalar());
    }

    public TelemetryPage ReadPage(string deviceId, long after, int limit)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, reading FROM telemetry WHERE device_id = $device AND id > $after
            ORDER BY id LIMIT $take;
            """;
        command.Parameters.AddWithValue("$device", deviceId);
        command.Parameters.AddWithValue("$after", after);
        command.Parameters.AddWithValue("$take", limit + 1);
        using var reader = command.ExecuteReader();
        var items = new List<ArchivedTelemetry>();
        while (items.Count < limit && reader.Read())
            items.Add(new(reader.GetInt64(0), Read(reader.GetString(1))));
        return new(items, items.Count == 0 ? after : items[^1].Cursor, reader.Read());
    }

    public TelemetryHistoryPage ReadHistoryPage(string deviceId, int page, int pageSize, bool measuredAt)
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction(deferred: true);
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.Parameters.AddWithValue("$device", deviceId);
        command.CommandText = "SELECT count(*) FROM telemetry WHERE device_id = $device;";
        var total = Convert.ToInt32(command.ExecuteScalar());

        var offset = ((long)page - 1) * pageSize;
        if (offset >= total) return new(page, pageSize, total, []);

        // RTC uses a sortable local timestamp. Receipt timestamps are serialized in UTC by DeviceStore.
        // The archive ID orders ties and SQLite places missing RTC timestamps last.
        command.CommandText = measuredAt
            ? """
                SELECT id, reading FROM telemetry WHERE device_id = $device
                ORDER BY json_extract(reading, '$.data.timestamp') DESC, id DESC LIMIT $take OFFSET $offset;
                """
            : """
                SELECT id, reading FROM telemetry WHERE device_id = $device
                ORDER BY json_extract(reading, '$.receivedAt') DESC, id DESC LIMIT $take OFFSET $offset;
                """;
        command.Parameters.AddWithValue("$take", pageSize);
        command.Parameters.AddWithValue("$offset", offset);
        using var reader = command.ExecuteReader();
        var items = new List<ArchivedTelemetry>();
        while (reader.Read()) items.Add(new(reader.GetInt64(0), Read(reader.GetString(1))));
        return new(page, pageSize, total, items);
    }

    public FeederTelemetryBatch ReadFeederHistory(FeederTelemetryWindow[] windows, long skip, int take, bool measuredAt)
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction(deferred: true);
        using var sql = connection.CreateCommand();
        sql.Transaction = transaction;
        var bounds = windows.Select(window => new
        {
            window.NodeId, window.ServerId, window.DeviceId,
            fromRtc = RtcBound(window.From), untilRtc = window.Until is { } until ? RtcBound(until) : null,
            fromReceipt = ReceiptBound(window.From),
            untilReceipt = window.Until is { } end ? ReceiptBound(end) : null,
        });
        sql.Parameters.AddWithValue("$bounds", JsonSerializer.Serialize(bounds, Json));
        // A sample belongs to the half-open assignment window containing its measurement time.
        // For records without an RTC timestamp use the UTC Bridge receipt time.
        sql.CommandText = FeederReadingsSql + "SELECT count(*) FROM readings;";
        var total = Convert.ToInt32(sql.ExecuteScalar());
        if (skip >= total) return new(total, []);

        var order = measuredAt ? "rtc DESC, id DESC" : "receipt DESC, id DESC";
        sql.CommandText = FeederReadingsSql + $"SELECT id, reading, node_id, server_id, until_rtc IS NULL FROM readings ORDER BY {order} LIMIT $take OFFSET $skip;";
        sql.Parameters.AddWithValue("$take", take);
        sql.Parameters.AddWithValue("$skip", skip);
        using var reader = sql.ExecuteReader();
        var items = new List<FeederArchivedTelemetry>();
        while (reader.Read())
        {
            items.Add(new(reader.GetInt64(0), Read(reader.GetString(1)),
                reader.GetString(2), reader.GetString(3), reader.GetBoolean(4)));
        }
        return new(total, items);
    }

    private const string FeederReadingsSql = """
            WITH bounds AS (
                SELECT json_extract(value, '$.nodeId') AS node_id,
                       json_extract(value, '$.serverId') AS server_id,
                       json_extract(value, '$.deviceId') AS device_id,
                       json_extract(value, '$.fromRtc') AS from_rtc,
                       json_extract(value, '$.untilRtc') AS until_rtc,
                       json_extract(value, '$.fromReceipt') AS from_receipt,
                       json_extract(value, '$.untilReceipt') AS until_receipt
                FROM json_each($bounds)
            ),
            candidates AS (
                SELECT t.id, t.reading, b.*,
                       json_extract(t.reading, '$.data.timestamp') AS rtc,
                       json_extract(t.reading, '$.receivedAt') AS received
                FROM bounds b JOIN telemetry t ON t.device_id = b.device_id
            ),
            normalized AS (
                SELECT *, substr(received, 1, 19) || '.' ||
                    substr(CASE WHEN substr(received, 20, 1) = '.'
                        THEN replace(substr(received, 21), '+00:00', '') ELSE '' END || '0000000', 1, 7) AS receipt
                FROM candidates
            ),
            readings AS (
                SELECT * FROM normalized
                WHERE (rtc IS NOT NULL AND rtc >= from_rtc AND (until_rtc IS NULL OR rtc < until_rtc))
                    OR (rtc IS NULL AND receipt >= from_receipt AND (until_receipt IS NULL OR receipt < until_receipt))
            )
            """;

    private static string RtcBound(DateTimeOffset instant)
    {
        var local = instant.ToOffset(TimeSpan.FromHours(7));
        var fraction = local.Ticks % TimeSpan.TicksPerSecond;
        if (fraction != 0) local = local.AddTicks(TimeSpan.TicksPerSecond - fraction);
        return local.ToString("yyyy-MM-ddTHH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string ReceiptBound(DateTimeOffset instant) =>
        instant.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffffff", System.Globalization.CultureInfo.InvariantCulture);

    public void TrackCommand(string deviceId, LampCommand command)
    {
        if (!Guid.TryParseExact(command.CommandId, "N", out _)) return;
        using var connection = Open();
        using var sql = connection.CreateCommand();
        sql.CommandText = """
            INSERT INTO command_receipts (device_id, command_id, mode, command)
            VALUES ($device, $id, $mode, $command)
            ON CONFLICT (device_id, command_id) DO UPDATE SET mode = excluded.mode
            WHERE mode = excluded.mode AND command IS excluded.command;
            """;
        sql.Parameters.AddWithValue("$device", deviceId);
        sql.Parameters.AddWithValue("$id", command.CommandId);
        sql.Parameters.AddWithValue("$mode", command.Mode);
        sql.Parameters.AddWithValue("$command", (object?)command.Command ?? DBNull.Value);
        if (sql.ExecuteNonQuery() == 0) throw new ArgumentException("Command ID already belongs to a different command.");
    }

    public DateTimeOffset? RecordReceipt(CommandAcknowledgement ack, DateTimeOffset receivedAt)
    {
        if (!Guid.TryParseExact(ack.CommandId, "N", out _) || ack.Mode is not ("MANUAL" or "AUTO") ||
            ack.LampCommand is not ("ON" or "OFF")) return null;
        using var connection = Open();
        using var sql = connection.CreateCommand();
        sql.CommandText = """
            UPDATE command_receipts SET received_at = coalesce(received_at, $received)
            WHERE device_id = $device AND command_id = $id AND mode = $mode
                AND (mode = 'AUTO' OR command = $lamp)
            RETURNING received_at;
            """;
        sql.Parameters.AddWithValue("$received", receivedAt.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
        sql.Parameters.AddWithValue("$device", ack.DeviceId!);
        sql.Parameters.AddWithValue("$id", ack.CommandId!);
        sql.Parameters.AddWithValue("$mode", ack.Mode);
        sql.Parameters.AddWithValue("$lamp", ack.LampCommand);
        return sql.ExecuteScalar() is string timestamp
            ? DateTimeOffset.Parse(timestamp, System.Globalization.CultureInfo.InvariantCulture) : null;
    }

    public IReadOnlyList<CommandReceipt> ReadCommandReceipts(CommandReceiptKey[] keys)
    {
        using var connection = Open();
        using var sql = connection.CreateCommand();
        sql.CommandText = """
            SELECT receipts.device_id, receipts.command_id, receipts.received_at
            FROM json_each($keys) AS key
            JOIN command_receipts AS receipts
              ON receipts.device_id = json_extract(key.value, '$.deviceId')
              AND receipts.command_id = json_extract(key.value, '$.commandId')
            WHERE receipts.received_at IS NOT NULL;
            """;
        sql.Parameters.AddWithValue("$keys", JsonSerializer.Serialize(keys, Json));
        using var reader = sql.ExecuteReader();
        var receipts = new List<CommandReceipt>();
        while (reader.Read())
            receipts.Add(new(reader.GetString(0), reader.GetString(1),
                DateTimeOffset.Parse(reader.GetString(2), System.Globalization.CultureInfo.InvariantCulture)));
        return receipts;
    }

    private static TelemetryReading Read(string? value) =>
        value is null ? throw new InvalidDataException("Telemetry archive entry missing.") :
        JsonSerializer.Deserialize<TelemetryReading>(value, Json)
            ?? throw new InvalidDataException("Invalid telemetry archive entry.");
}
