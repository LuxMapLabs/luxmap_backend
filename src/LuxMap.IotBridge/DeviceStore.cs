using System.Text.Json;

record CommandRequest(string? Mode, string? Command);
record LampCommand(string Mode, string? Command, string? CommandId = null, long Version = 0);
record TelemetryRequest(string? DeviceId, string? Mode, string? LampCommand,
    double? Lux, double? Voltage, double? Current, double? Power, string? Timestamp,
    string? SessionId = null, long? SequenceNumber = null, string? AppliedCommandId = null);
record TelemetryReading(TelemetryRequest Data, DateTimeOffset ReceivedAt, string? GatewayId = null);
record SequenceRange(long Start, long End);
record CommandAcknowledgement(string? DeviceId, string? CommandId, string? Mode, string? LampCommand);
record ReceivedCommandAck(CommandAcknowledgement Data, DateTimeOffset ReceivedAt);
record DeviceState(string DeviceId, LampCommand DesiredCommand, DateTimeOffset? CommandUpdatedAt,
    TelemetryReading? LatestTelemetry, List<TelemetryReading> History,
    Dictionary<string, List<SequenceRange>>? AcceptedSequences = null, ReceivedCommandAck? LastCommandAck = null)
{
    public bool CommandAcknowledged => DesiredCommand.CommandId is not null &&
        LastCommandAck?.Data.CommandId == DesiredCommand.CommandId;
}

sealed class DeviceStore
{
    private readonly object gate = new();
    private readonly string path;
    private readonly TelemetryArchive archive;
    private readonly JsonSerializerOptions json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private Dictionary<string, DeviceState> devices;
    public DeviceStore(IHostEnvironment environment)
    {
        var directory = Path.Combine(environment.ContentRootPath, "data");
        Directory.CreateDirectory(directory);
        path = Path.Combine(directory, "devices.json");
        devices = File.Exists(path)
            ? JsonSerializer.Deserialize<Dictionary<string, DeviceState>>(File.ReadAllText(path), json)
                ?? throw new InvalidDataException("Invalid device storage.")
            : new();
        archive = new TelemetryArchive(directory, devices.Values);
    }
    private DeviceState Get(string id) => devices.GetValueOrDefault(id)
        ?? new(id, new("AUTO", null, "initial-auto", 0), null, null, new());
    public DeviceState? Read(string id) { lock (gate) return devices.GetValueOrDefault(id); }
    public DeviceState[] ReadAll() { lock (gate) return devices.Values.ToArray(); }
    public TelemetryPage ReadTelemetryHistory(string id, long after, int limit) =>
        archive.ReadPage(id, after, limit);
    private void Commit(DeviceState state)
    {
        var next = new Dictionary<string, DeviceState>(devices) { [state.DeviceId] = state };
        using (var stream = new FileStream(path + ".tmp", FileMode.Create, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(stream, next, json);
            stream.Flush(flushToDisk: true);
        }
        File.Move(path + ".tmp", path, overwrite: true);
        devices = next;
    }
    public LampCommand SetCommand(string id, LampCommand command)
    {
        lock (gate)
        {
            var state = Get(id);
            command = command with
            {
                CommandId = Guid.NewGuid().ToString("N"),
                Version = Math.Max(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), checked(state.DesiredCommand.Version + 1))
            };
            Commit(state with { DesiredCommand = command, CommandUpdatedAt = DateTimeOffset.UtcNow });
            return command;
        }
    }
    // The durable deduplication ledger is independent of the bounded display history.
    // Ranges also accept out-of-order replay without dropping previously unseen samples.
    public bool AcceptTelemetry(string gatewayId, TelemetryRequest data)
    {
        lock (gate)
        {
            var state = Get(data.DeviceId!);
            var ledger = state.AcceptedSequences is null ? new() :
                new Dictionary<string, List<SequenceRange>>(state.AcceptedSequences);
            var ranges = ledger.GetValueOrDefault(data.SessionId!) ?? new();
            var sequence = data.SequenceNumber!.Value;
            if (ranges.Any(range => sequence >= range.Start && sequence <= range.End)) return false;
            var combined = ranges.Append(new(sequence, sequence)).OrderBy(range => range.Start).ToList();
            var merged = new List<SequenceRange>();
            foreach (var range in combined)
            {
                if (merged.Count > 0 && range.Start <= merged[^1].End + 1)
                    merged[^1] = merged[^1] with { End = Math.Max(merged[^1].End, range.End) };
                else merged.Add(range);
            }
            ledger[data.SessionId!] = merged;
            var reading = archive.Save(data.DeviceId!, new TelemetryReading(data, DateTimeOffset.UtcNow, gatewayId));
            var latest = state.LatestTelemetry;
            var newer = latest is null || (latest.Data.SessionId == data.SessionId
                ? sequence > (latest.Data.SequenceNumber ?? 0)
                : data.Timestamp is null || latest.Data.Timestamp is null ||
                    string.CompareOrdinal(data.Timestamp, latest.Data.Timestamp) >= 0);
            Commit(state with
            {
                LatestTelemetry = newer ? reading : latest,
                History = state.History.Append(reading).TakeLast(1000).ToList(),
                AcceptedSequences = ledger
            });
            return true;
        }
    }
    public void AcknowledgeCommand(CommandAcknowledgement ack)
    {
        lock (gate)
        {
            var state = Get(ack.DeviceId!);
            if (ack.CommandId != state.DesiredCommand.CommandId || state.CommandAcknowledged ||
                ack.Mode != state.DesiredCommand.Mode || ack.LampCommand is not ("ON" or "OFF") ||
                (ack.Mode == "MANUAL" && ack.LampCommand != state.DesiredCommand.Command)) return;
            Commit(state with { LastCommandAck = new(ack, DateTimeOffset.UtcNow) });
        }
    }
}
