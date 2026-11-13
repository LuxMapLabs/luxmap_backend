using System.Text.Json;
using Microsoft.Data.Sqlite;

record ArchivedTelemetry(long Cursor, TelemetryReading Reading);
record TelemetryPage(IReadOnlyList<ArchivedTelemetry> Items, long NextCursor, bool HasMore);

sealed class TelemetryArchive
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
            CREATE INDEX IF NOT EXISTS telemetry_device_id ON telemetry (device_id, id);
            """;
        schema.ExecuteNonQuery();

        using var transaction = connection.BeginTransaction();
        foreach (var device in existingDevices)
            foreach (var reading in device.History)
                Insert(connection, transaction, device.DeviceId, reading);
        transaction.Commit();
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

    private static TelemetryReading Read(string? value) =>
        value is null ? throw new InvalidDataException("Telemetry archive entry missing.") :
        JsonSerializer.Deserialize<TelemetryReading>(value, Json)
            ?? throw new InvalidDataException("Invalid telemetry archive entry.");
}
