using System.Text.Json;
using LuxMap.IotWire;

record AutoSchedule(int? DayStartMinute, int? NightStartMinute)
{
    public bool IsValid() => DayStartMinute is >= 0 and < 1440 &&
        NightStartMinute is >= 0 and < 1440 && DayStartMinute != NightStartMinute;
}
record CommandRequest(string? Mode, string? Command, AutoSchedule? AutoSchedule = null, string? CommandId = null, string? AutoType = null);
record LampCommand(string Mode, string? Command, string? CommandId = null, long Version = 0,
    AutoSchedule? AutoSchedule = null, long? ExpiresAtUnixSeconds = null, string? AutoType = null);
record TelemetryRequest(string? DeviceId, string? Mode, string? LampCommand,
    double? Lux, double? Voltage, double? Current, double? Power, string? Timestamp,
    string? SessionId = null, long? SequenceNumber = null, string? AppliedCommandId = null, string? AutoType = null);
record TelemetryReading(TelemetryRequest Data, DateTimeOffset ReceivedAt, string? GatewayId = null);
record SequenceRange(long Start, long End);
record CommandAcknowledgement(string? DeviceId, string? CommandId, string? Mode, string? LampCommand, string? AutoType = null);
record ReceivedCommandAck(CommandAcknowledgement Data, DateTimeOffset ReceivedAt);
record DeviceState(string DeviceId, LampCommand DesiredCommand, DateTimeOffset? CommandUpdatedAt,
    TelemetryReading? LatestTelemetry, List<TelemetryReading> History,
    Dictionary<string, List<SequenceRange>>? AcceptedSequences = null, ReceivedCommandAck? LastCommandAck = null,
    ReceivedSensorHealth? SensorHealth = null)
{
    public DateTimeOffset? LastSeenAt { get; init; }
    public DateTimeOffset? LastCommandAckAt { get; init; }
    public bool CommandAcknowledged => DesiredCommand.CommandId is not null &&
        LastCommandAck?.Data.CommandId == DesiredCommand.CommandId;
}

sealed class DeviceStore
{
    private readonly object gate = new();
    private readonly TimeProvider clock;
    private readonly string path;
    private readonly TelemetryArchive archive;
    private readonly JsonSerializerOptions json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private Dictionary<string, DeviceState> devices;
    public DeviceStore(IHostEnvironment environment, TimeProvider? clock = null)
    {
        this.clock = clock ?? TimeProvider.System;
        var directory = Path.Combine(environment.ContentRootPath, "data");
        Directory.CreateDirectory(directory);
        path = Path.Combine(directory, "devices.json");
        devices = File.Exists(path)
            ? JsonSerializer.Deserialize<Dictionary<string, DeviceState>>(File.ReadAllText(path), json)
                ?? throw new InvalidDataException("Invalid device storage.")
            : new();
        devices = devices.ToDictionary(pair => pair.Key, pair => pair.Value with {
            DesiredCommand = pair.Value.DesiredCommand with {
                AutoType = pair.Value.DesiredCommand.Mode == "AUTO"
                    ? pair.Value.DesiredCommand.AutoType ?? "scheduled" : null
            }
        });
        archive = new TelemetryArchive(directory, devices.Values);
    }
    private DeviceState Get(string id) => devices.GetValueOrDefault(id)
        ?? new(id, new("AUTO", null, "initial-auto", 0, AutoType: "scheduled"), null, null, new());
    public DeviceState? Read(string id) { lock (gate) return devices.GetValueOrDefault(id); }
    public DeviceState[] ReadAll() { lock (gate) return devices.Values.ToArray(); }
    public TelemetryPage ReadTelemetryHistory(string id, long after, int limit) =>
        archive.ReadPage(id, after, limit);
    public TelemetryHistoryPage ReadTelemetryHistoryPage(string id, int page, int pageSize, bool measuredAt) =>
        archive.ReadHistoryPage(id, page, pageSize, measuredAt);

    public IReadOnlyList<AutoHistoryEvent> ReadAutoTransitions(AutoHistoryWindow[] windows) => archive.ReadAutoTransitions(windows);

    public IReadOnlyList<CommandReceipt> ReadCommandReceipts(CommandReceiptKey[] keys) =>
        archive.ReadCommandReceipts(keys);

    public FeederTelemetryBatch ReadFeederHistory(FeederTelemetryWindow[] windows, long skip, int take, bool measuredAt) =>
        archive.ReadFeederHistory(windows, skip, take, measuredAt);

    public FeederAverageBatch ReadFeederAverages(FeederTelemetryWindow[] windows, long skip, int take,
        bool measuredAt, DateTimeOffset? from, DateTimeOffset? to, bool keysOnly) =>
        archive.ReadFeederAverages(windows, skip, take, measuredAt, from, to, keysOnly);

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
            var autoType = AutoControl.Resolve(command.Mode, command.AutoType, command.AutoSchedule);
            command = command with
            {
                CommandId = command.CommandId ?? Guid.NewGuid().ToString("N"), ExpiresAtUnixSeconds = DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeSeconds(),
                AutoType = autoType,
                AutoSchedule = autoType == "full_time" ? null : command.AutoSchedule ?? state.DesiredCommand.AutoSchedule ??
                    (command.Mode == "AUTO" ? new AutoSchedule(360, 1081) : null),
                Version = Math.Max(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), checked(state.DesiredCommand.Version + 1))
            };
            archive.TrackCommand(id, command);
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
            var now = clock.GetUtcNow();
            var reading = archive.Save(data.DeviceId!, new TelemetryReading(data, now, gatewayId));
            var latest = state.LatestTelemetry;
            var newer = latest is null || (latest.Data.SessionId == data.SessionId
                ? sequence > (latest.Data.SequenceNumber ?? 0)
                : data.Timestamp is null || latest.Data.Timestamp is null ||
                    string.CompareOrdinal(data.Timestamp, latest.Data.Timestamp) >= 0);
            Commit(state with
            {
                LastSeenAt = now,
                LatestTelemetry = newer ? reading : latest,
                History = state.History.Append(reading).TakeLast(1000).ToList(),
                AcceptedSequences = ledger
            });
            return true;
        }
    }
    public void AcceptSensorHealth(SensorHealthReport report)
    {
        lock (gate)
        {
            var state = Get(report.DeviceId!);
            if (state.SensorHealth?.Data is { } previous && previous.SessionId == report.SessionId &&
                previous.SequenceNumber >= report.SequenceNumber) return;
            var now = clock.GetUtcNow();
            Commit(state with { SensorHealth = new(report, now), LastSeenAt = now });
        }
    }

    private static DateTimeOffset Later(DateTimeOffset? previous, DateTimeOffset current) =>
        previous is { } last && last > current ? last : current;

    public void AcknowledgeCommand(CommandAcknowledgement ack)
    {
        var receivedAt = clock.GetUtcNow();
        var observedAt = receivedAt;
        lock (gate)
        {
            var state = Get(ack.DeviceId!);
            var isCurrent = ack.CommandId == state.DesiredCommand.CommandId;
            if (ack.CommandId == "initial-auto")
            {
                if (ack.Mode != "AUTO" || ack.LampCommand is not ("ON" or "OFF")) return;
            }
            else
            {
                if (isCurrent && state.CommandAcknowledged) return;
                var receipt = archive.RecordReceipt(ack, receivedAt);
                if (receipt is null) return;
                receivedAt = receipt.Value;
            }
            if (isCurrent && !state.CommandAcknowledged)
                Commit(state with
                {
                    LastCommandAck = new(ack, receivedAt),
                    LastSeenAt = Later(state.LastSeenAt, receivedAt),
                    LastCommandAckAt = Later(state.LastCommandAckAt, receivedAt),
                });
            else if (!isCurrent && ack.CommandId != "initial-auto" && receivedAt == observedAt)
                Commit(state with
                {
                    LastSeenAt = observedAt, LastCommandAckAt = observedAt,
                });
            // A repeated ACK keeps its first receipt time. Gateway retries must not
            // make a powered-off node look online forever.
        }
    }
}
