using System.Text.Json;
using Microsoft.Data.Sqlite;

record ArchivedTelemetry(long Cursor, TelemetryReading Reading);
record TelemetryPage(IReadOnlyList<ArchivedTelemetry> Items, long NextCursor, bool HasMore);

record TelemetryHistoryPage(int Page, int PageSize, int Total, IReadOnlyList<ArchivedTelemetry> Items);

record CommandReceiptKey(string DeviceId, string CommandId);
record CommandReceipt(string DeviceId, string CommandId, DateTimeOffset ReceivedAt,
    long? ExecutedAtUnixSeconds = null, long? ExpiresAtUnixSeconds = null);

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
        using (var info = connection.CreateCommand())
        {
            info.CommandText = "PRAGMA table_info(command_receipts);";
            using var fields = info.ExecuteReader();
            var hasType = false;
            while (fields.Read()) hasType |= fields.GetString(1) == "auto_type";
            fields.Close();
            if (!hasType)
            {
                info.CommandText = "ALTER TABLE command_receipts ADD COLUMN auto_type TEXT;";
                info.ExecuteNonQuery();
            }
        }

        using (var info = connection.CreateCommand())
        {
            info.CommandText = "PRAGMA table_info(command_receipts);";
            var columns = new HashSet<string>();
            using (var reader = info.ExecuteReader())
                while (reader.Read()) columns.Add(reader.GetString(1));
            foreach (var (name, type) in new[] { ("expires_at", "INTEGER"), ("executed_at", "INTEGER"), ("command_json", "TEXT") })
                if (!columns.Contains(name))
                {
                    info.CommandText = $"ALTER TABLE command_receipts ADD COLUMN {name} {type};";
                    info.ExecuteNonQuery();
                }
        }

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
                TrackCommand(device.DeviceId, new(ack.Mode, ack.Mode == "MANUAL" ? ack.LampCommand : null, id, AutoType: ack.AutoType));
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

    public FeederTelemetryBatch ReadFeederHistory(FeederTelemetryWindow[] windows, long skip, int take, bool measuredAt, DateTimeOffset? from = null, DateTimeOffset? to = null)
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
        var filter = " WHERE 1=1";
        var time = measuredAt ? "rtc" : "receipt";
        if (from is { } start) {
            filter += $" AND {time} >= $from";
            sql.Parameters.AddWithValue("$from", measuredAt ? RtcBound(start) : ReceiptBound(start));
        }
        if (to is { } finish) {
            filter += $" AND {time} < $to";
            sql.Parameters.AddWithValue("$to", measuredAt ? RtcBound(finish) : ReceiptBound(finish));
        }
        sql.CommandText = FeederReadingsSql + "SELECT count(*) FROM readings" + filter + ";";
        var total = Convert.ToInt32(sql.ExecuteScalar());
        if (skip >= total) return new(total, []);

        var order = measuredAt ? "rtc DESC, id DESC" : "receipt DESC, id DESC";
        sql.CommandText = FeederReadingsSql + $"SELECT id, reading, node_id, server_id, until_rtc IS NULL FROM readings{filter} ORDER BY {order} LIMIT $take OFFSET $skip;";
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
            INSERT INTO command_receipts (device_id, command_id, mode, command, auto_type, expires_at, command_json)
            VALUES ($device, $id, $mode, $command, $autoType, $expires, $json)
            ON CONFLICT (device_id, command_id) DO UPDATE SET mode = excluded.mode,
                command_json = coalesce(command_json, excluded.command_json),
                expires_at = coalesce(expires_at, excluded.expires_at)
            WHERE mode = excluded.mode AND command IS excluded.command
              AND (mode <> 'AUTO' OR coalesce(auto_type, 'scheduled') = coalesce(excluded.auto_type, 'scheduled'));
            """;
        sql.Parameters.AddWithValue("$device", deviceId);
        sql.Parameters.AddWithValue("$id", command.CommandId);
        sql.Parameters.AddWithValue("$mode", command.Mode);
        sql.Parameters.AddWithValue("$expires", (object?)command.ExpiresAtUnixSeconds ?? DBNull.Value);
        sql.Parameters.AddWithValue("$json", JsonSerializer.Serialize(command, Json));
        sql.Parameters.AddWithValue("$command", (object?)command.Command ?? DBNull.Value);
        sql.Parameters.AddWithValue("$autoType", command.Mode == "AUTO"
            ? command.AutoType ?? "scheduled" : (object)DBNull.Value);
        if (sql.ExecuteNonQuery() == 0) throw new ArgumentException("Command ID already belongs to a different command.");
    }

    public DateTimeOffset? RecordReceipt(CommandAcknowledgement ack, DateTimeOffset receivedAt)
    {
        if (!Guid.TryParseExact(ack.CommandId, "N", out _) || ack.Mode is not ("MANUAL" or "AUTO") ||
            ack.LampCommand is not ("ON" or "OFF")) return null;
        if (ack.ExecutedAtUnixSeconds is { } executed && (executed < 0 || executed > receivedAt.ToUnixTimeSeconds())) return null;
        using var connection = Open();
        using var sql = connection.CreateCommand();
        sql.CommandText = """
            UPDATE command_receipts SET received_at = coalesce(received_at, $received),
                executed_at = CASE WHEN received_at IS NULL THEN $executed ELSE executed_at END
            WHERE device_id = $device AND command_id = $id AND mode = $mode
                AND (mode = 'AUTO' OR command = $lamp)
                AND (mode <> 'AUTO' OR coalesce(auto_type, 'scheduled') = $autoType)
                AND (received_at IS NOT NULL OR expires_at IS NULL OR
                    ($executed IS NULL AND $receivedUnix < expires_at) OR
                    ($executed IS NOT NULL AND $executed < expires_at AND $executed <= $receivedUnix
                        AND $executed >= expires_at - 20))
            RETURNING received_at;
            """;
        sql.Parameters.AddWithValue("$received", receivedAt.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
        sql.Parameters.AddWithValue("$receivedUnix", receivedAt.ToUnixTimeSeconds());
        sql.Parameters.AddWithValue("$executed", (object?)ack.ExecutedAtUnixSeconds ?? DBNull.Value);
        sql.Parameters.AddWithValue("$device", ack.DeviceId!);
        sql.Parameters.AddWithValue("$id", ack.CommandId!);
        sql.Parameters.AddWithValue("$mode", ack.Mode);
        sql.Parameters.AddWithValue("$lamp", ack.LampCommand);
        sql.Parameters.AddWithValue("$autoType", ack.AutoType ?? "scheduled");
        return sql.ExecuteScalar() is string timestamp
            ? DateTimeOffset.Parse(timestamp, System.Globalization.CultureInfo.InvariantCulture) : null;
    }

    public IReadOnlyList<CommandReceipt> ReadCommandReceipts(CommandReceiptKey[] keys)
    {
        using var connection = Open();
        using var sql = connection.CreateCommand();
        sql.CommandText = """
            SELECT receipts.device_id, receipts.command_id, receipts.received_at, receipts.executed_at, receipts.expires_at
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
                DateTimeOffset.Parse(reader.GetString(2), System.Globalization.CultureInfo.InvariantCulture),
                reader.IsDBNull(3) ? null : reader.GetInt64(3), reader.IsDBNull(4) ? null : reader.GetInt64(4)));
        return receipts;
    }

    public LampCommand? ReadTrackedCommand(string deviceId, string commandId)
    {
        using var connection = Open();
        using var sql = connection.CreateCommand();
        sql.CommandText = "SELECT command_json FROM command_receipts WHERE device_id = $device AND command_id = $id;";
        sql.Parameters.AddWithValue("$device", deviceId);
        sql.Parameters.AddWithValue("$id", commandId);
        return sql.ExecuteScalar() is string json ? JsonSerializer.Deserialize<LampCommand>(json, Json) : null;
    }

    public (LampCommand Command, ReceivedCommandAck Ack)? ReadLastSuccess(string deviceId)
    {
        using var connection = Open();
        using var sql = connection.CreateCommand();
        sql.CommandText = """
            SELECT command_json, received_at, executed_at FROM command_receipts
            WHERE device_id = $device AND received_at IS NOT NULL AND command_json IS NOT NULL
            ORDER BY json_extract(command_json, '$.version') DESC, received_at DESC LIMIT 1;
            """;
        sql.Parameters.AddWithValue("$device", deviceId);
        using var reader = sql.ExecuteReader();
        if (!reader.Read()) return null;
        var command = JsonSerializer.Deserialize<LampCommand>(reader.GetString(0), Json)!;
        return (command, new(new(deviceId, command.CommandId, command.Mode, command.Command, command.AutoType,
            reader.IsDBNull(2) ? null : reader.GetInt64(2)), DateTimeOffset.Parse(reader.GetString(1), System.Globalization.CultureInfo.InvariantCulture)));
    }

    private static TelemetryReading Read(string? value) =>
        value is null ? throw new InvalidDataException("Telemetry archive entry missing.") :
        JsonSerializer.Deserialize<TelemetryReading>(value, Json)
            ?? throw new InvalidDataException("Invalid telemetry archive entry.");
}
