using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LuxMap.Modules.Assets.Entities;
using LuxMap.Modules.Telemetry.Entities;
using LuxMap.Persistence;
using LuxMap.Shared.Contracts.Errors;
using LuxMap.Shared.Contracts.Paging;
using LuxMap.Shared.Http;
using Microsoft.EntityFrameworkCore;

namespace LuxMap.Modules.IOT;

public sealed class IotOptions
{
    public string? BaseUrl { get; set; }
    public string[] AllowedOrigins { get; set; } = [];
    public Dictionary<string, IotServerProfile> Servers { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, NodeProfile> Nodes { get; set; } = new(StringComparer.Ordinal);
}

public sealed record IotServerProfile(string? BaseUrl, string? CommuneId = null, double? Latitude = null, double? Longitude = null);

public sealed record NodeProfile
{
    public string? ServerId { get; init; }
    public short RelayNo { get; init; } = 1;
    public string DeviceId { get; init; } = "";
    public string FeederId { get; init; } = "";
    public string PoleId { get; init; } = "";
    public double NominalVoltage { get; init; }
    public double NominalCurrentMa { get; init; }
    public double NominalPowerMw { get; init; }
    public double VoltageTolerancePercent { get; init; } = 10;
    public double CurrentTolerancePercent { get; init; } = 25;
    public double PowerTolerancePercent { get; init; } = 25;
    public double OffCurrentLimitMa { get; init; } = 10;
    public int MaxSilenceSeconds { get; init; } = 60;
    public bool UseTenLampCalibration { get; init; }

    internal bool IsValid() =>
        !string.IsNullOrWhiteSpace(DeviceId) && !string.IsNullOrWhiteSpace(FeederId) &&
        !string.IsNullOrWhiteSpace(PoleId) && RelayNo is >= 1 and <= 32 &&
        Positive(NominalVoltage) && Positive(NominalCurrentMa) && Positive(NominalPowerMw) &&
        Tolerance(VoltageTolerancePercent) && Tolerance(CurrentTolerancePercent) &&
        Tolerance(PowerTolerancePercent) && Positive(OffCurrentLimitMa) && MaxSilenceSeconds > 0;

    private static bool Positive(double value) => double.IsFinite(value) && value > 0;
    private static bool Tolerance(double value) => Positive(value) && value <= 100;
}

public sealed record IotMeasurement(
    DateTime ReceivedAt, string LampCommand, double Voltage, double CurrentMa, double PowerMw);

public sealed record IotAnomaly(
    string Code, double? Observed, double? Reference, double? DeviationPercent, double? Limit);

public sealed record IotDiagnosticResult(
    string NodeId, string FeederId, string PoleId, short RelayNo, string Status,
    IotMeasurement? Measurement, IReadOnlyList<IotAnomaly> Anomalies)
{
    public LampCountEstimate? LampCount { get; init; }
}

public sealed record IotNodeStatus(
    string NodeId, string? ServerId, string Status, DateTime? LastReceivedAt)
{
    public string? CabinetId { get; init; }
    public string? CommuneId { get; init; }
    public IotLocation? Location { get; init; }
}

public sealed record IotElectricalResult(
    string NodeId, string ServerId, string Status, IotMeasurement? Measurement);

public sealed record IotLocation(
    [property: System.Text.Json.Serialization.JsonRequired] double Lat,
    [property: System.Text.Json.Serialization.JsonRequired] double Lng);
public sealed record IotServerStatus(string ServerId, string Status, string? CommuneId = null, IotLocation? Location = null);
public sealed record IotPoleControl(string PoleId, string? FeederId, string? NodeId, short? RelayNo, bool CanControlFeeder);

public sealed record IotTelemetryPage(IReadOnlyList<IotArchivedSample> Items, long NextCursor, bool HasMore);
public sealed record IotArchivedSample(long Cursor, IotArchivedReading Reading);
public sealed record IotArchivedReading(IotArchivedData Data, DateTimeOffset ReceivedAt, string? GatewayId);
public sealed record IotArchivedData(string? Timestamp, string? SessionId, long? SequenceNumber,
    string? Mode, string? LampCommand, double? Lux, double? Voltage, double? Current, double? Power);

public sealed record IotCommandRequest(string? Mode, string? Command);
public sealed record IotCommandStatus(
    string NodeId, string? CommandId, string Mode, string? Command, bool CommandAcknowledged);

public sealed class IotDiagnosticsService(
    LuxMapDbContext db, HttpClient client, IotRegistrationStore registry)
{
    public async Task<IotDiagnosticResult> GetAsync(string nodeId, CancellationToken ct)
    {
        await RequireNodeAsync(nodeId, ct);
        var settings = await registry.ReadAsync([nodeId], ct);
        if (!settings.Nodes.TryGetValue(nodeId, out var profile) || !profile.IsValid() ||
            !TryServer(settings, profile, out _, out var baseUri))
            throw Unavailable("Chưa cấu hình nguồn đo hoặc ngưỡng hợp lệ cho thiết bị IoT.");

        var topology = await (
            from control in db.Set<FeederControl>()
            join feeder in db.Set<Feeder>() on control.FeederId equals feeder.FeederId
            join pole in db.Set<Pole>() on feeder.FeederId equals pole.FeederId
            where control.NodeId == nodeId && control.RelayNo == profile.RelayNo &&
                  feeder.FeederId == profile.FeederId && pole.PoleId == profile.PoleId
            select new { feeder.FeederId, pole.PoleId, control.RelayNo })
            .SingleOrDefaultAsync(ct);
        if (topology is null)
            throw Unavailable("Trụ đèn phải thuộc feeder được gán cho relay của node IoT.");

        var result = new IotDiagnosticResult(
            nodeId, topology.FeederId, topology.PoleId, topology.RelayNo, "no_data", null, []);
        var device = await ReadDeviceAsync(baseUri, profile.DeviceId, ct);
        var measurement = ReadMeasurement(device);
        if (measurement is null) return result;

        var diagnosis = Evaluate(result, measurement, profile, DateTimeOffset.UtcNow);
        if (!profile.UseTenLampCalibration || diagnosis.Status == "stale" || measurement.LampCommand != "ON" ||
            !LampCountEstimator.InCalibrationRange(measurement.Voltage))
            return diagnosis;

        var estimate = LampCountEstimator.Evaluate(RecentOnCurrents(device!));
        return estimate.EstimatedMissing is > 0
            ? diagnosis with
            {
                Status = "anomaly",
                LampCount = estimate,
                Anomalies = [.. diagnosis.Anomalies, new("lamp_count_shortfall", estimate.EstimatedMissing, null, null, null)],
            }
            : diagnosis with { LampCount = estimate };
    }

    public async Task<IotPoleControl> PoleControlAsync(string poleId, CancellationToken ct)
    {
        var pole = await db.Set<Pole>().AsNoTracking().Where(row => row.PoleId == poleId)
            .Select(row => new { row.PoleId, row.FeederId }).SingleOrDefaultAsync(ct);
        if (pole is null)
            throw new LuxMapException(ErrorCodes.AssetNotFound, HttpStatusCode.NotFound, "Không tìm thấy trụ đèn.");

        var control = await (
            from row in db.Set<FeederControl>().AsNoTracking()
            join node in db.Set<IotNode>() on row.NodeId equals node.NodeId
            where row.FeederId == pole.FeederId
            select new { row.NodeId, row.RelayNo, node.SupportsRemoteControl }).SingleOrDefaultAsync(ct);
        if (control is null) return new(poleId, pole.FeederId, null, null, false);

        var settings = await registry.ReadAsync([control.NodeId], ct);
        var canControl = control.SupportsRemoteControl &&
            settings.Nodes.TryGetValue(control.NodeId, out var profile) &&
            profile.FeederId == pole.FeederId && profile.RelayNo == control.RelayNo && control.RelayNo == 1 &&
            TryServer(settings, profile, out _, out _);
        return new(poleId, pole.FeederId, control.NodeId, control.RelayNo, canControl);
    }

    public async Task<IotCommandStatus> SendCommandAsync(
        string nodeId, IotCommandRequest request, CancellationToken ct)
    {
        var (uri, deviceId, profile) = await DeviceSourceAsync(nodeId, ct);
        var canControl = profile.RelayNo == 1 && await (
            from node in db.Set<IotNode>()
            join control in db.Set<FeederControl>() on node.NodeId equals control.NodeId
            where node.NodeId == nodeId && node.SupportsRemoteControl &&
                  control.RelayNo == profile.RelayNo && control.FeederId == profile.FeederId
            select control.FeederId).AnyAsync(ct);
        if (!canControl)
            throw new LuxMapException(ErrorCodes.ValidationFailed, HttpStatusCode.BadRequest,
                "Node chưa hỗ trợ điều khiển hoặc feeder chưa được gán đúng relay 1.");
        var mode = request.Mode?.Trim().ToUpperInvariant();
        var command = request.Command?.Trim().ToUpperInvariant();
        if (!(mode == "AUTO" && command is null || mode == "MANUAL" && command is ("ON" or "OFF")))
            throw new LuxMapException(ErrorCodes.ValidationFailed, HttpStatusCode.BadRequest,
                "Dùng MANUAL với ON hoặc OFF, hoặc AUTO với command null.");

        var endpoint = new Uri(uri, $"api/luxmap/devices/{Uri.EscapeDataString(deviceId)}/command");
        try
        {
            using var response = await client.PostAsJsonAsync(endpoint, new IotCommandRequest(mode, command), ct);
            response.EnsureSuccessStatusCode();
            if (!IsJson(response)) throw Unavailable("Server IoT không trả về JSON.");
            var saved = await response.Content.ReadFromJsonAsync<PrototypeCommand>(cancellationToken: ct);
            if (saved is null || string.IsNullOrWhiteSpace(saved.CommandId) || saved.Mode != mode || saved.Command != command)
                throw Unavailable("Server IoT trả về lệnh không khớp yêu cầu.");
            return new(nodeId, saved.CommandId, mode!, command, false);
        }
        catch (Exception ex) when (IsSourceFailure(ex, ct))
        {
            throw Unavailable("Không xác nhận được việc gửi lệnh đến server IoT.");
        }
    }

    public async Task<IotCommandStatus> CommandAsync(string nodeId, CancellationToken ct)
    {
        var (uri, deviceId, _) = await DeviceSourceAsync(nodeId, ct);
        var device = await ReadDeviceAsync(uri, deviceId, ct);
        if (device?.DesiredCommand is not { Mode: "AUTO" or "MANUAL" } command)
            throw Unavailable("Server IoT chưa có trạng thái lệnh cho thiết bị.");
        return new(nodeId, command.CommandId, command.Mode!, command.Command, device.CommandAcknowledged);
    }

    public async Task<IotTelemetryPage> TelemetryHistoryAsync(
        string nodeId, long after, int limit, CancellationToken ct)
    {
        if (after < 0 || limit is < 1 or > 1000)
            throw new LuxMapException(ErrorCodes.ValidationFailed, HttpStatusCode.BadRequest,
                "after phải từ 0 và limit phải từ 1 đến 1000.");

        var (uri, deviceId, _) = await DeviceSourceAsync(nodeId, ct);
        var path = FormattableString.Invariant(
            $"api/luxmap/devices/{Uri.EscapeDataString(deviceId)}/telemetry/history?after={after}&limit={limit}");
        try
        {
            using var response = await client.GetAsync(new Uri(uri, path), ct);
            if (response.StatusCode == HttpStatusCode.NotFound) return new([], after, false);
            response.EnsureSuccessStatusCode();
            if (!IsJson(response)) throw Unavailable("Server IoT không trả về JSON.");
            var page = await response.Content.ReadFromJsonAsync<IotTelemetryPage>(cancellationToken: ct);
            return page is { Items: not null } && page.NextCursor >= after
                ? page : throw Unavailable("Server IoT trả về trang dữ liệu không hợp lệ.");
        }
        catch (Exception ex) when (IsSourceFailure(ex, ct))
        {
            throw Unavailable("Không đọc được lịch sử đo từ server IoT.");
        }
    }

    private async Task<(Uri Uri, string DeviceId, NodeProfile Profile)> DeviceSourceAsync(string nodeId, CancellationToken ct)
    {
        await RequireNodeAsync(nodeId, ct);
        var settings = await registry.ReadAsync([nodeId], ct);
        if (!settings.Nodes.TryGetValue(nodeId, out var profile) || !TryServer(settings, profile, out _, out var uri))
            throw Unavailable("Chưa cấu hình nguồn điều khiển cho thiết bị IoT.");
        return (uri, profile.DeviceId, profile);
    }

    public async Task<IotElectricalResult> ElectricalAsync(string nodeId, CancellationToken ct)
    {
        await RequireNodeAsync(nodeId, ct);
        var settings = await registry.ReadAsync([nodeId], ct);
        if (!settings.Nodes.TryGetValue(nodeId, out var profile) || !TryServer(settings, profile, out var id, out var uri))
            throw Unavailable("Chưa cấu hình nguồn đo cho thiết bị IoT.");

        var measurement = await ReadMeasurementAsync(uri, profile.DeviceId, ct);
        return new(nodeId, id, StatusOf(measurement, profile, DateTimeOffset.UtcNow), measurement);
    }

    public async Task<PagedResult<IotNodeStatus>> NodesAsync(PageRequest page, CancellationToken ct)
    {
        var nodes = db.Set<IotNode>().AsNoTracking();
        var total = await nodes.CountAsync(ct);
        var ids = await nodes.OrderBy(node => node.CreatedAt).ThenBy(node => node.NodeId.Length)
            .ThenBy(node => node.NodeId).Skip(page.Skip).Take(page.PageSize)
            .Select(node => node.NodeId).ToListAsync(ct);
        var settings = await registry.ReadAsync(ids, ct);
        var result = ids.Select(id => settings.Nodes.TryGetValue(id, out var profile) &&
            TryServer(settings, profile, out var serverId, out _)
            ? new IotNodeStatus(id, serverId, "source_unavailable", null)
            : new IotNodeStatus(id, profile?.ServerId, "unconfigured", null)).ToArray();

        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            await Parallel.ForEachAsync(Enumerable.Range(0, ids.Count), new ParallelOptions
            {
                MaxDegreeOfParallelism = 12,
                CancellationToken = budget.Token,
            }, async (index, token) => result[index] = await NodeStatusAsync(settings, ids[index], token));
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested && budget.IsCancellationRequested)
        {
            // Unprobed rows retain source_unavailable rather than extending the request by another batch.
        }
        var positions = await (
            from node in nodes
            join cabinet in db.Set<ElectricalCabinet>().AsNoTracking() on node.CabinetId equals cabinet.CabinetId
            where ids.Contains(node.NodeId)
            select new { node.NodeId, node.CabinetId, node.CommuneId, Lat = cabinet.Geom.Y, Lng = cabinet.Geom.X })
            .ToDictionaryAsync(row => row.NodeId, ct);
        for (var index = 0; index < result.Length; index++)
            if (positions.TryGetValue(ids[index], out var position))
                result[index] = result[index] with
                {
                    CabinetId = position.CabinetId, CommuneId = position.CommuneId,
                    Location = new(position.Lat, position.Lng),
                };
        return PagedResult<IotNodeStatus>.From(page, total, result);
    }

    public async Task<IReadOnlyList<IotServerStatus>> ServersAsync(CancellationToken ct)
    {
        // A server is visible only if it has a registered node in the caller's commune scope.
        var visibleIds = await db.Set<IotNode>().AsNoTracking()
            .Select(node => node.NodeId).ToListAsync(ct);
        var settings = await registry.ReadAsync(visibleIds, ct);
        var servers = new SortedDictionary<string, (Uri Uri, string DeviceId)>(StringComparer.Ordinal);
        foreach (var profile in settings.Nodes.Values.OrderBy(profile => profile.DeviceId, StringComparer.Ordinal))
            if (TryServer(settings, profile, out var id, out var uri))
                servers.TryAdd(id, (uri, profile.DeviceId));

        foreach (var (id, profile) in settings.Servers)
            if (profile.CommuneId is { } communeId && db.CurrentCommuneScope.Allows(communeId) &&
                IotOrigin.TryAllowed(settings, profile.BaseUrl, out var uri))
                servers.TryAdd(id, (uri, ""));

        var entries = servers.ToArray();
        var result = new IotServerStatus[entries.Length];
        await Parallel.ForEachAsync(Enumerable.Range(0, entries.Length), new ParallelOptions
        {
            MaxDegreeOfParallelism = 12,
            CancellationToken = ct,
        }, async (index, token) =>
        {
            var (id, (uri, deviceId)) = entries[index];
            settings.Servers.TryGetValue(id, out var profile);
            var communeId = profile?.CommuneId;
            var visible = communeId is not null && db.CurrentCommuneScope.Allows(communeId);
            var location = visible && profile is { Latitude: double lat, Longitude: double lng }
                ? new IotLocation(lat, lng) : null;
            var status = deviceId.Length == 0 ? "unconfigured" : await ServerStatusAsync(uri, deviceId, token);
            result[index] = new(id, status, visible ? communeId : null, location);
        });
        return result;
    }

    private async Task<string> ServerStatusAsync(Uri uri, string deviceId, CancellationToken ct)
    {
        try
        {
            using var response = await client.GetAsync(new Uri(uri, $"api/luxmap/gateways/by-device/{Uri.EscapeDataString(deviceId)}/status"), ct);
            if (!response.IsSuccessStatusCode || !IsJson(response)) return "unreachable";
            var gateway = await response.Content.ReadFromJsonAsync<GatewayHealth>(cancellationToken: ct);
            return gateway?.Status is "online" or "offline" or "broker_down" or "unconfigured" ? gateway.Status : "unreachable";
        }
        catch (Exception ex) when (IsSourceFailure(ex, ct))
        {
            return "unreachable";
        }
    }

    private async Task<IotNodeStatus> NodeStatusAsync(IotOptions settings, string nodeId, CancellationToken ct)
    {
        if (!settings.Nodes.TryGetValue(nodeId, out var profile) || !TryServer(settings, profile, out var id, out var uri))
            return new(nodeId, profile?.ServerId, "unconfigured", null);

        try
        {
            var measurement = await ReadMeasurementAsync(uri, profile.DeviceId, ct);
            return new(nodeId, id, StatusOf(measurement, profile, DateTimeOffset.UtcNow), measurement?.ReceivedAt);
        }
        catch (LuxMapException ex) when (ex.Code == "IOT_SOURCE_UNAVAILABLE")
        {
            return new(nodeId, id, "source_unavailable", null);
        }
    }

    private async Task RequireNodeAsync(string nodeId, CancellationToken ct)
    {
        if (!await db.Set<IotNode>().AnyAsync(node => node.NodeId == nodeId, ct))
            throw new LuxMapException(ErrorCodes.AssetNotFound, HttpStatusCode.NotFound, "Không tìm thấy thiết bị IoT.");
    }

    private static bool TryServer(IotOptions settings, NodeProfile profile, out string id, out Uri uri)
    {
        id = profile.ServerId ?? "default";
        uri = null!;
        var url = settings.Servers.TryGetValue(id, out var server)
            ? server.BaseUrl : profile.ServerId is null ? settings.BaseUrl : null;
        return !string.IsNullOrWhiteSpace(profile.DeviceId) && profile.DeviceId is not ("." or "..") &&
            profile.MaxSilenceSeconds > 0 && IotOrigin.TryAllowed(settings, url, out uri);
    }

    private static string StatusOf(IotMeasurement? measurement, NodeProfile profile, DateTimeOffset now)
        => measurement is null ? "no_data"
            : now - measurement.ReceivedAt > TimeSpan.FromSeconds(profile.MaxSilenceSeconds) ? "offline" : "online";

    private async Task<IotMeasurement?> ReadMeasurementAsync(Uri baseUri, string deviceId, CancellationToken ct)
        => ReadMeasurement(await ReadDeviceAsync(baseUri, deviceId, ct));

    private async Task<PrototypeDevice?> ReadDeviceAsync(Uri baseUri, string deviceId, CancellationToken ct)
    {
        var endpoint = new Uri(baseUri, "api/luxmap/devices/" + Uri.EscapeDataString(deviceId));
        PrototypeDevice? device;
        try
        {
            using var response = await client.GetAsync(endpoint, ct);
            if (response.StatusCode == HttpStatusCode.NotFound)
                return null;
            response.EnsureSuccessStatusCode();
            if (!IsJson(response)) throw Unavailable("Server IoT không trả về JSON.");
            device = await response.Content.ReadFromJsonAsync<PrototypeDevice>(cancellationToken: ct);
        }
        catch (Exception ex) when (IsSourceFailure(ex, ct))
        {
            throw Unavailable("Không đọc được dữ liệu từ server IoT.");
        }

        if (device is null || !string.Equals(device.DeviceId, deviceId, StringComparison.Ordinal))
            throw Unavailable("Server IoT trả về thiết bị không khớp cấu hình.");

        return device;
    }

    private static IotMeasurement? ReadMeasurement(PrototypeDevice? device)
    {
        if (device?.LatestTelemetry is not { } reading)
            return null;

        var sample = reading.Data;
        if (reading.ReceivedAt == default || sample?.LampCommand is not ("ON" or "OFF") ||
            !Valid(sample.Voltage) || !Valid(sample.Current) || !Valid(sample.Power))
            throw Unavailable("Server IoT trả về mẫu đo không hợp lệ.");

        return new IotMeasurement(reading.ReceivedAt.UtcDateTime, sample.LampCommand,
            sample.Voltage!.Value, sample.Current!.Value, sample.Power!.Value);
    }

    private static IReadOnlyList<double> RecentOnCurrents(PrototypeDevice device)
    {
        if (device.LatestTelemetry?.Data is not { SessionId: { Length: > 0 } session, SequenceNumber: > 0 } latest)
            return [];

        // Use sequence order: replayed MQTT samples can arrive out of order.
        var history = (device.History ?? [])
            .Select(reading => reading.Data)
            .Where(sample => sample?.SessionId == session)
            .ToLookup(sample => sample!.SequenceNumber);
        var currents = new List<double>(LampCountEstimator.WindowSize) { latest.Current!.Value };
        for (var sequence = latest.SequenceNumber!.Value - 1;
             sequence > 0 && currents.Count < LampCountEstimator.WindowSize; sequence--)
        {
            var candidates = history[sequence];
            if (candidates.Count() != 1 ||
                candidates.First() is not { LampCommand: "ON", Voltage: double voltage, Current: double current } ||
                !LampCountEstimator.InCalibrationRange(voltage) || !double.IsFinite(current))
                break;

            currents.Add(current);
        }
        return currents;
    }

    internal static IotDiagnosticResult Evaluate(
        IotDiagnosticResult result, IotMeasurement reading, NodeProfile profile, DateTimeOffset now)
    {
        var silence = (now - reading.ReceivedAt).TotalSeconds;
        if (silence > profile.MaxSilenceSeconds)
            return result with
            {
                Status = "stale",
                Measurement = reading,
                Anomalies = [new("telemetry_stale", silence, null, null, profile.MaxSilenceSeconds)],
            };

        var anomalies = new List<IotAnomaly>();
        if (reading.LampCommand == "OFF")
        {
            if (reading.CurrentMa > profile.OffCurrentLimitMa)
                anomalies.Add(new("current_when_off", reading.CurrentMa, null, null, profile.OffCurrentLimitMa));
        }
        else if (Math.Abs(reading.Voltage) <= LampCountEstimator.VoltageLossLimit)
        {
            anomalies.Add(new("voltage_loss", reading.Voltage, null, null, LampCountEstimator.VoltageLossLimit));
        }
        else
        {
            Compare(anomalies, "voltage_deviation", reading.Voltage, profile.NominalVoltage, profile.VoltageTolerancePercent);
            Compare(anomalies, "current_deviation", reading.CurrentMa, profile.NominalCurrentMa, profile.CurrentTolerancePercent);
            Compare(anomalies, "power_deviation", reading.PowerMw, profile.NominalPowerMw, profile.PowerTolerancePercent);
        }

        return result with
        {
            Status = anomalies.Count == 0 ? "normal" : "anomaly",
            Measurement = reading,
            Anomalies = anomalies,
        };
    }

    private static void Compare(List<IotAnomaly> anomalies, string code, double observed, double reference, double tolerance)
    {
        var deviation = (observed - reference) / reference * 100;
        if (Math.Abs(deviation) > tolerance)
            anomalies.Add(new(code, observed, reference, Math.Round(deviation, 2), tolerance));
    }

    private static bool Valid(double? value) => value is double number && double.IsFinite(number);

    private static LuxMapException Unavailable(string message) =>
        new("IOT_SOURCE_UNAVAILABLE", HttpStatusCode.ServiceUnavailable, message);

    private static bool IsSourceFailure(Exception ex, CancellationToken ct) =>
        ex is HttpRequestException or JsonException or IOException or NotSupportedException ||
        ex is OperationCanceledException && !ct.IsCancellationRequested;

    private static bool IsJson(HttpResponseMessage response)
        => response.Content.Headers.ContentType?.MediaType is { } type &&
           (type.Equals("application/json", StringComparison.OrdinalIgnoreCase) ||
            type.EndsWith("+json", StringComparison.OrdinalIgnoreCase));

    private sealed record GatewayHealth(string? Status);
    private sealed record PrototypeDevice(
        string? DeviceId, PrototypeReading? LatestTelemetry, PrototypeReading[]? History,
        PrototypeCommand? DesiredCommand, bool CommandAcknowledged);
    private sealed record PrototypeCommand(string? Mode, string? Command, string? CommandId);
    private sealed record PrototypeReading(DateTimeOffset ReceivedAt, PrototypeSample? Data);
    private sealed record PrototypeSample(
        string? LampCommand, double? Voltage, double? Current, double? Power, string? SessionId, long? SequenceNumber);
}
