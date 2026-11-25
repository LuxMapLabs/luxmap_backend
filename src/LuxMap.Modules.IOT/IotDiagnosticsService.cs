using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using LuxMap.IotWire;
using LuxMap.Modules.Assets;
using LuxMap.Modules.Assets.Entities;
using LuxMap.Shared.Contracts.Enums;
using LuxMap.Modules.IOT.Entities;
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

public sealed record IotServerProfile(string? BaseUrl, string? CommuneId = null, double? Latitude = null, double? Longitude = null, string? LegacyGatewayId = null, double RadiusKm = 0);

public sealed record NodeProfile
{
    public string? ServerId { get; init; }
    public string DeviceId { get; init; } = "";
    public string FeederId { get; init; } = "";
    public double NominalVoltage { get; init; }
    public double NominalCurrentMa { get; init; }
    public double NominalPowerMw { get; init; }
    public double RadiusKm { get; init; }
    public double VoltageTolerancePercent { get; init; } = 10;
    // Legacy source fields retained for existing clients and stored profiles.
    public double CurrentTolerancePercent { get; init; } = 25;
    public double PowerTolerancePercent { get; init; } = 25;
    public double OffCurrentLimitMa { get; init; } = 10;
    public int MaxSilenceSeconds { get; init; } = 60;
    public bool UseTenLampCalibration { get; init; }
    public int InstalledLampCount { get; init; } = 10;
    public string? LampCountCalibration { get; init; }

    internal bool IsValid() =>
        !string.IsNullOrWhiteSpace(DeviceId) && !string.IsNullOrWhiteSpace(FeederId) &&
        Positive(NominalVoltage) && Positive(NominalCurrentMa) && Positive(NominalPowerMw) &&
        Tolerance(VoltageTolerancePercent) && MaxSilenceSeconds > 0 && InstalledLampCount is > 0 and <= 1000 &&
        LampCountCalibration is null or "4v" or "5v" or "5v-quiet-v1";

    private static bool Positive(double value) => double.IsFinite(value) && value > 0;
    private static bool Tolerance(double value) => Positive(value) && value <= 100;
}

/// <summary>Persisted source configuration; independent of MQTT availability.</summary>
public sealed record IotNodeSourceDetails(string NodeId, string ServerId, string DeviceId, string FeederId,
    double NominalVoltage, double NominalCurrentMa, double NominalPowerMw, double RadiusKm = 0);

public sealed record IotMeasurement(
    DateTime ReceivedAt, string LampCommand, double Voltage, double CurrentMa, double PowerMw);

public sealed record IotAnomaly(
    string Code,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] double? Observed = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] double? Reference = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] double? DeviationPercent = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] double? Limit = null);

public sealed record IotSensorHealth(string Status, DateTime? ReceivedAt);

public sealed record IotDiagnosticResult(
    string NodeId, string FeederId, string Status,
    IotMeasurement? Measurement, IReadOnlyList<IotAnomaly> Anomalies)
{
    public string ConnectionStatus { get; init; } = "no_data";
    public string ElectricalStatus { get; init; } = "no_data";
    public DateTime? LastSeenAt { get; init; }
    public DateTime? LastCommandAckAt { get; init; }
    public ElectricalWindowAssessment? ElectricalWindow { get; init; }
    public DateTime? MeasuredAt { get; init; }
    public string? MeasurementKey { get; init; }
    public string? SensorHealthKey { get; init; }
    public string? FirmwareVersion { get; init; }
    public string? CalibrationVersion { get; init; }
    public long? MeasurementSequence { get; init; }
    public IotSensorHealth SensorHealth { get; init; } = new("unknown", null);
}

public sealed record IotNodeStatus(
    string NodeId, string? ServerId, string Status, DateTime? LastReceivedAt)
{
    public string ElectricalStatus { get; init; } = "no_data";
    public DateTime? LastSeenAt { get; init; }
    public DateTime? LastCommandAckAt { get; init; }
    public string? CabinetId { get; init; }
    public string? CommuneId { get; init; }
    public IotLocation? Location { get; init; }
    public double RadiusKm { get; init; }
}

public sealed record IotElectricalResult(
    string NodeId, string ServerId, string Status, IotMeasurement? Measurement, FeederPowerState PowerState = FeederPowerState.Unknown)
{
    public string ElectricalStatus { get; init; } = "no_data";
    public DateTime? LastSeenAt { get; init; }
    public DateTime? LastCommandAckAt { get; init; }
}

public sealed record IotLocation(
    [property: System.Text.Json.Serialization.JsonRequired] double Lat,
    [property: System.Text.Json.Serialization.JsonRequired] double Lng);
public sealed record IotServerStatus(string ServerId, string Status, string? CommuneId = null, IotLocation? Location = null, double RadiusKm = 0)
{
    public bool CredentialIssued { get; init; }
}
public sealed record IotPoleControl(string PoleId, string? FeederId, string? NodeId, bool CanControlFeeder, string? ControlChannel = null);

public sealed record IotArchivedReading(IotArchivedData Data, DateTimeOffset ReceivedAt, string? GatewayId);
public sealed record IotArchivedData(string? Timestamp, string? SessionId, long? SequenceNumber,
    string? Mode, string? LampCommand, double? Lux, double? Voltage, double? Current, double? Power, string? ReceptionType = null);

public sealed record IotAutoSchedule(string? DayStart, string? NightStart);
public sealed record IotCommandRequest(string? Mode, string? Command, IotAutoSchedule? AutoSchedule = null, string? AutoType = null);
public sealed record IotGatewayRouteRequest(string? DeviceId);
public sealed record IotGatewayRoute(string GatewayId, string[] DeviceIds, bool CredentialIssued);
public sealed record IotGatewayCredential(string ClientId, string Username, string Password, string[] NodeIds)
{
    public string? ServerId { get; init; }
}
public sealed record IotCommandStatus(
    string NodeId, string? CommandId, string? Mode, string? Command, bool CommandAcknowledged, IotAutoSchedule? AutoSchedule = null, DateTimeOffset? ExpiresAt = null, FeederPowerState PowerState = FeederPowerState.Unknown, string? AutoType = null,
    string Status = "pending", DateTimeOffset? ExecutedAt = null, DateTimeOffset? AcknowledgedAt = null, bool LateAck = false);

public sealed record IotCommandHistoryItem(
    string CommandId, string NodeId, string FeederId, string ServerId, string DeviceId,
    string Mode, string? Command, IotAutoSchedule? AutoSchedule,
    string RequestedBy, DateTime RequestedAt, DateTime? BridgeAcceptedAt,
    IotCommandHistoryStatus Status = IotCommandHistoryStatus.Pending, string? AutoType = null,
    DateTimeOffset? ExecutedAt = null, DateTimeOffset? AcknowledgedAt = null, bool LateAck = false,
    DateTimeOffset? ExpiresAt = null);

public enum IotCommandHistoryStatus { Success, Pending, Expired }

public sealed partial class IotDiagnosticsService(
    LuxMapDbContext db, HttpClient client, IotRegistrationStore registry, IotFeederSourceLookup sources,
    TimeProvider clock) : IFeederPowerLookup
{
    internal IotDiagnosticsService ForContext(LuxMapDbContext context) =>
        new(context, client, registry.ForContext(context), sources, clock);

    public async Task<IotDiagnosticResult> GetAsync(string nodeId, CancellationToken ct)
    {
        await RequireNodeAsync(nodeId, ct);
        var settings = await registry.ReadAsync([nodeId], ct);
        if (!settings.Nodes.TryGetValue(nodeId, out var profile) || !profile.IsValid() ||
            !TryServer(settings, profile, out _, out var baseUri))
            throw Unavailable("Chưa cấu hình nguồn đo hoặc ngưỡng hợp lệ cho thiết bị IoT.");

        if (!await CompatibleFeederAsync(nodeId, profile.FeederId, false, ct))
            throw Unavailable("Feeder phải thuộc cùng tủ điện và xã với node IoT.");
        var device = await ReadDeviceAsync(baseUri, profile.DeviceId, ct);
        var now = clock.GetUtcNow();
        var report = device?.SensorHealth;
        var sensorHealth = report is null || report.ReceivedAt == default || report.Data?.IsValidFor(profile.DeviceId) != true
            ? new IotSensorHealth("unknown", null)
            : new IotSensorHealth(
                report.ReceivedAt <= now && now - report.ReceivedAt <= TimeSpan.FromSeconds(60) ? "fresh" : "stale",
                report.ReceivedAt.UtcDateTime);
        var codes = sensorHealth.Status == "fresh" ? report?.Data?.Codes ?? [] : [];
        var diagnosis = new IotDiagnosticResult(nodeId, profile.FeederId, "no_data", null, [])
        {
            SensorHealth = sensorHealth,
            MeasuredAt = MeasuredAt(device?.LatestTelemetry?.Data?.Timestamp)?.UtcDateTime,
            MeasurementKey = device?.LatestTelemetry?.Data is { } telemetry ? $"{telemetry.SessionId}/{telemetry.SequenceNumber}" : null,
            MeasurementSequence = device?.LatestTelemetry?.Data?.SequenceNumber,
            SensorHealthKey = report?.Data is { } health ? $"{health.SessionId}/{health.SequenceNumber}" : null,
            FirmwareVersion = report?.Data?.FirmwareVersion,
            CalibrationVersion = ElectricalWindowEvaluator.Version,
        };
        var measurement = ReadMeasurement(device);
        if (measurement is not null)
        {
            var electricalAvailable = !codes.Any(code => SensorHealthCode.GroupOf(code) == SensorHealthCode.Ina);
            diagnosis = Evaluate(diagnosis, measurement, profile, now, electricalAvailable);
            if (electricalAvailable && !codes.Any(code => SensorHealthCode.GroupOf(code) == SensorHealthCode.Rtc) &&
                diagnosis.Status != "stale" && measurement.LampCommand == "ON")
            {
                var (window, alerts) = ElectricalWindowEvaluator.Evaluate(RecentOnMeasurements(device!, profile, now), profile);
                diagnosis = diagnosis with {
                    ElectricalWindow = window,
                    Status = alerts.Count == 0 ? diagnosis.Status : "anomaly",
                    Anomalies = [.. diagnosis.Anomalies, .. alerts],
                };
            }
        }

        var contact = ContactOf(device, measurement, profile, now);
        if (measurement is not null && contact.ElectricalStatus == "stale" && diagnosis.Status != "stale")
            diagnosis = diagnosis with
            {
                Status = "stale", ElectricalWindow = null,
                Anomalies = [new("telemetry_stale", Math.Max(0, (now - measurement.ReceivedAt).TotalSeconds),
                    null, null, profile.MaxSilenceSeconds)],
            };
        diagnosis = diagnosis with
        {
            ConnectionStatus = contact.Status, ElectricalStatus = contact.ElectricalStatus,
            LastSeenAt = contact.LastSeenAt, LastCommandAckAt = contact.LastCommandAckAt,
        };
        return codes.Length == 0 ? diagnosis : diagnosis with
        {
            Status = "anomaly",
            Anomalies = [.. diagnosis.Anomalies, .. codes.Select(code => new IotAnomaly(code))],
        };
    }

    public async Task RequireMqttAuthorizationAsync(string serverId, string deviceId, string communeId,
        string? key, CancellationToken ct)
    {
        var uri = await ProvisioningOriginAsync(serverId, communeId, key, ct);
        using var request = ProvisioningRequest(HttpMethod.Get, uri,
            $"gateways/{Uri.EscapeDataString(serverId.ToLowerInvariant())}/devices/{Uri.EscapeDataString(deviceId)}/authorization", key!);
        try
        {
            using var response = await client.SendAsync(request, ct);
            if (response.StatusCode == HttpStatusCode.NotFound)
                throw new LuxMapException(ErrorCodes.ValidationFailed, HttpStatusCode.BadRequest,
                    "Thiết bị MQTT chưa được Admin cấp tuyến và secret.");
            if (response.StatusCode != HttpStatusCode.NoContent)
                throw Unavailable("IoT Bridge không xác nhận quyền MQTT.");
        }
        catch (Exception ex) when (IsSourceFailure(ex, ct))
        {
            throw Unavailable("Không kiểm tra được quyền MQTT trên IoT Bridge.");
        }
    }

    public async Task<IotGatewayCredential> IssueGatewayCredentialAsync(string serverId, string gatewayId,
        string? key, CancellationToken ct)
    {
        gatewayId = await CanonicalGatewayAsync(serverId, gatewayId, ct);
        if (!ValidMqttId(gatewayId))
            throw new LuxMapException(ErrorCodes.ValidationFailed, HttpStatusCode.BadRequest, "Gateway ID không hợp lệ.");

        var uri = await ProvisioningOriginAsync(serverId, null, key, ct);
        using var request = ProvisioningRequest(HttpMethod.Post, uri,
            $"gateways/{Uri.EscapeDataString(gatewayId)}/credential", key!);
        try
        {
            using var response = await client.SendAsync(request, ct);
            if (response.StatusCode == HttpStatusCode.NotFound)
                throw new LuxMapException(ErrorCodes.AssetNotFound, HttpStatusCode.NotFound, "Gateway chưa có tuyến MQTT.");
            if (response.StatusCode == HttpStatusCode.Conflict)
                throw new LuxMapException("IOT_REGISTRATION_CONFLICT", HttpStatusCode.Conflict, "Gateway đã được cấp secret.");
            if (response.StatusCode != HttpStatusCode.OK || !IsJson(response))
                throw Unavailable("IoT Bridge không cấp được secret.");
            var credential = await response.Content.ReadFromJsonAsync<IotGatewayCredential>(cancellationToken: ct);
            if (credential is null || credential.ClientId != gatewayId || credential.Username != gatewayId ||
                credential.Password is not { Length: 64 } || credential.NodeIds is null || credential.NodeIds.Any(id => !ValidMqttId(id)))
                throw Unavailable("IoT Bridge trả thông tin xác thực không hợp lệ.");
            return credential with { ServerId = gatewayId };
        }
        catch (Exception ex) when (IsSourceFailure(ex, ct))
        {
            throw Unavailable("Không cấp được secret từ IoT Bridge.");
        }
    }


    public async Task<IotGatewayCredential> GetGatewayCredentialAsync(string serverId, string gatewayId,
        string? key, CancellationToken ct)
    {
        gatewayId = await CanonicalGatewayAsync(serverId, gatewayId, ct);
        if (!ValidMqttId(gatewayId))
            throw new LuxMapException(ErrorCodes.ValidationFailed, HttpStatusCode.BadRequest, "Gateway ID không hợp lệ.");
        var uri = await ProvisioningOriginAsync(serverId, null, key, ct);
        using var request = ProvisioningRequest(HttpMethod.Get, uri,
            $"gateways/{Uri.EscapeDataString(gatewayId)}/credential", key!);
        try
        {
            using var response = await client.SendAsync(request, ct);
            if (response.StatusCode == HttpStatusCode.NotFound)
                throw new LuxMapException(ErrorCodes.AssetNotFound, HttpStatusCode.NotFound, "Gateway chưa có key hoặc key đã thu hồi.");
            if (response.StatusCode == HttpStatusCode.Conflict)
                throw new LuxMapException("IOT_CREDENTIAL_NOT_RETRIEVABLE", HttpStatusCode.Conflict,
                    "Key cũ chỉ có hash. Cần thu hồi rồi cấp mới để có thể xem lại.");
            if (response.StatusCode != HttpStatusCode.OK || !IsJson(response))
                throw Unavailable("Không lấy được key từ IoT Bridge.");
            var credential = await response.Content.ReadFromJsonAsync<IotGatewayCredential>(cancellationToken: ct);
            if (credential is null || credential.ClientId != gatewayId || credential.Username != gatewayId ||
                credential.Password is not { Length: 64 } || credential.NodeIds is null || credential.NodeIds.Any(id => !ValidMqttId(id)))
                throw Unavailable("IoT Bridge trả thông tin key không hợp lệ.");
            return credential with { ServerId = gatewayId };
        }
        catch (Exception ex) when (IsSourceFailure(ex, ct))
        {
            throw Unavailable("Không lấy được key từ IoT Bridge.");
        }
    }

    public async Task RevokeGatewayCredentialAsync(string serverId, string gatewayId, string? key, CancellationToken ct)
    {
        gatewayId = await CanonicalGatewayAsync(serverId, gatewayId, ct);
        if (!ValidMqttId(gatewayId))
            throw new LuxMapException(ErrorCodes.ValidationFailed, HttpStatusCode.BadRequest, "Gateway ID không hợp lệ.");
        var uri = await ProvisioningOriginAsync(serverId, null, key, ct);
        using var request = ProvisioningRequest(HttpMethod.Delete, uri,
            $"gateways/{Uri.EscapeDataString(gatewayId)}/credential", key!);
        try
        {
            using var response = await client.SendAsync(request, ct);
            if (response.StatusCode != HttpStatusCode.NoContent)
                throw Unavailable("Không thu hồi được key trên IoT Bridge.");
        }
        catch (Exception ex) when (IsSourceFailure(ex, ct))
        {
            throw Unavailable("Không thu hồi được key trên IoT Bridge.");
        }
    }

    private async Task<Uri> ProvisioningOriginAsync(string serverId, string? communeId, string? key, CancellationToken ct)
    {
        var settings = await registry.ReadAsync([], ct);
        if (!settings.Servers.TryGetValue(serverId, out var server) ||
            (communeId is not null && server.CommuneId != communeId))
            throw new LuxMapException(ErrorCodes.AssetNotFound, HttpStatusCode.NotFound, "Không tìm thấy IoT server trong phạm vi.");
        if (key is not { Length: >= 32 } || !IotOrigin.TryAllowed(settings, server.BaseUrl, out var uri))
            throw Unavailable("Chưa cấu hình địa chỉ hoặc khóa quản trị IoT Bridge.");
        return uri;
    }

    private static HttpRequestMessage ProvisioningRequest(HttpMethod method, Uri uri, string path, string key)
    {
        var request = new HttpRequestMessage(method, new Uri(uri, $"api/luxmap/{path}"));
        request.Headers.Add("X-Iot-Provisioning-Key", key);
        return request;
    }

    public async Task<IReadOnlyList<IotGatewayRoute>> GatewayRoutesAsync(string serverId, string? key, CancellationToken ct)
    {
        var uri = await ProvisioningOriginAsync(serverId, null, key, ct);
        var route = await ReadServerRouteAsync(uri, serverId.ToLowerInvariant(), key!, ct);
        return route is null ? [] : [route];
    }

    internal static bool ValidMqttId(string? id) => id is { Length: > 0 and <= 64 } &&
        char.IsAsciiLetterOrDigit(id[0]) && id.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    internal async Task<string> EnsureGatewayRouteAsync(string serverId, string deviceId,
        string communeId, string? key, CancellationToken ct, bool repairExisting = false)
    {
        serverId = serverId.ToLowerInvariant();
        var uri = await ProvisioningOriginAsync(serverId, communeId, key, ct);
        var target = await ReadServerRouteAsync(uri, serverId, key!, ct);
        if (target?.CredentialIssued != true)
            throw new LuxMapException(ErrorCodes.ValidationFailed, HttpStatusCode.BadRequest,
                "Server cần có MQTT key trước khi thêm node.");
        try {
            // Even an existing assignment republishes the node list after a reconnect.
            await RegisterGatewayRouteAsync(serverId, serverId, new(deviceId), key, ct);
        }
        catch (LuxMapException ex) when (ex.Code == "IOT_REGISTRATION_CONFLICT" && repairExisting) {
            // Repair legacy ACL drift only for an already persisted, commune-visible source.
            var owned = await (from source in db.Set<IotSourceRegistration>()
                join node in db.Set<IotNode>() on source.NodeId equals node.NodeId
                where source.ServerId == serverId && source.DeviceId == deviceId &&
                    source.DetachedAt == null && node.CabinetId != null
                select source.NodeId).AnyAsync(ct);
            if (!owned) throw;
            var owner = await ReadBridgeDeviceOwnerAsync(uri, deviceId, key!, ct);
            var settings = await registry.ReadAsync([], ct);
            // Never claim the canonical ID or saved alias of a different registered server.
            if (owner is not null && settings.Servers.Any(pair =>
                    !string.Equals(pair.Key, serverId, StringComparison.OrdinalIgnoreCase) &&
                    (string.Equals(pair.Key, owner, StringComparison.OrdinalIgnoreCase) || pair.Value.LegacyGatewayId == owner)))
                throw;
            var profile = new NodeProfile { ServerId = serverId, DeviceId = deviceId };
            await ChangeBindingAsync(profile, owner, false, communeId, key, ct);
        }
        var confirmed = await ReadServerRouteAsync(uri, serverId, key!, ct);
        if (confirmed?.CredentialIssued != true || !confirmed.DeviceIds.Contains(deviceId))
            throw Unavailable("Bridge chưa xác nhận node thuộc server đã chọn; vui lòng thử lại.");
        return serverId;
    }

    private async Task<string?> ReadBridgeDeviceOwnerAsync(Uri uri, string deviceId, string key, CancellationToken ct)
    {
        using var request = ProvisioningRequest(HttpMethod.Get, uri, "gateways/routes", key);
        try {
            using var response = await client.SendAsync(request, ct);
            if (response.StatusCode != HttpStatusCode.OK || !IsJson(response))
                throw Unavailable("Không đọc được chủ tuyến MQTT để đồng bộ node.");
            var routes = await response.Content.ReadFromJsonAsync<IotGatewayRoute[]>(cancellationToken: ct);
            if (routes is null || routes.Any(route => route is null || !ValidMqttId(route.GatewayId) ||
                route.DeviceIds is null || route.DeviceIds.Length > 32 || route.DeviceIds.Any(id => !ValidMqttId(id))) ||
                routes.Select(route => route.GatewayId).Distinct(StringComparer.Ordinal).Count() != routes.Length)
                throw Unavailable("Bridge trả danh sách tuyến không hợp lệ.");
            var owners = routes.Where(route => route.DeviceIds.Contains(deviceId)).Select(route => route.GatewayId).ToArray();
            if (owners.Length > 1) throw Unavailable("Thiết bị có nhiều chủ tuyến MQTT; cần kiểm tra cấu hình.");
            return owners.SingleOrDefault();
        }
        catch (Exception ex) when (IsSourceFailure(ex, ct)) {
            throw Unavailable("Không đọc được tuyến MQTT để đồng bộ node.");
        }
    }

    public async Task RegisterGatewayRouteAsync(string serverId, string gatewayId, IotGatewayRouteRequest request,
        string? provisioningKey, CancellationToken ct)
    {
        gatewayId = await CanonicalGatewayAsync(serverId, gatewayId, ct);
        if (!ValidMqttId(gatewayId) || !ValidMqttId(request.DeviceId))
            throw new LuxMapException(ErrorCodes.ValidationFailed, HttpStatusCode.BadRequest, "Gateway ID hoặc Device ID không hợp lệ.");

        var settings = await registry.ReadAsync([], ct);
        if (!settings.Servers.TryGetValue(serverId, out var server))
            throw new LuxMapException(ErrorCodes.AssetNotFound, HttpStatusCode.NotFound, "Không tìm thấy IoT server.");
        if (!IotOrigin.TryAllowed(settings, server.BaseUrl, out var uri) || provisioningKey is not { Length: >= 32 })
            throw Unavailable("Chưa cấu hình địa chỉ hoặc khóa quản trị IoT Bridge.");

        using var message = new HttpRequestMessage(HttpMethod.Put,
            new Uri(uri, $"api/luxmap/gateways/{Uri.EscapeDataString(gatewayId)}/route"));
        message.Headers.Add("X-Iot-Provisioning-Key", provisioningKey);
        message.Content = JsonContent.Create(request);
        try
        {
            using var response = await client.SendAsync(message, ct);
            if (response.StatusCode == HttpStatusCode.Conflict)
                throw new LuxMapException("IOT_REGISTRATION_CONFLICT", HttpStatusCode.Conflict,
                    "Gateway hoặc thiết bị đã được gán cho một tuyến MQTT khác.");
            if (response.StatusCode != HttpStatusCode.NoContent)
                throw Unavailable("IoT Bridge không xác nhận đăng ký tuyến MQTT.");
        }
        catch (Exception ex) when (IsSourceFailure(ex, ct))
        {
            throw Unavailable("Không đăng ký được tuyến MQTT trên IoT Bridge.");
        }
    }

    public async Task<IotPoleControl> PoleControlAsync(string poleId, CancellationToken ct)
    {
        var pole = await db.Set<Pole>().AsNoTracking().Where(row => row.PoleId == poleId)
            .Select(row => new { row.PoleId, row.FeederId }).SingleOrDefaultAsync(ct);
        if (pole is null)
            throw new LuxMapException(ErrorCodes.AssetNotFound, HttpStatusCode.NotFound, "Không tìm thấy trụ đèn.");

        if (pole.FeederId is null) return new(poleId, null, null, false);
        var (settings, links) = await sources.LinksAsync(db, [pole.FeederId], ct);
        if (!links.TryGetValue(pole.FeederId, out var link))
            return new(poleId, pole.FeederId, null, false);
        var canControl = link.RemoteControl && TryServer(settings, link.Profile, out _, out _);
        return new(poleId, pole.FeederId, link.NodeId, canControl, canControl ? "iot_bridge" : null);
    }

    private Task<bool> CompatibleFeederAsync(string nodeId, string feederId, bool remote, CancellationToken ct) =>
        (from node in db.Set<IotNode>()
         from feeder in db.Set<Feeder>()
         where node.NodeId == nodeId && feeder.FeederId == feederId &&
               node.CabinetId != null && node.CabinetId == feeder.CabinetId && node.CommuneId == feeder.CommuneId &&
               (!remote || node.SupportsRemoteControl)
         select feeder.FeederId).AnyAsync(ct);

    public async Task<IReadOnlyDictionary<string, FeederPowerState>> ReadAsync(
        IReadOnlyCollection<string> feederIds, CancellationToken ct)
    {
        var result = new System.Collections.Concurrent.ConcurrentDictionary<string, FeederPowerState>();
        if (feederIds.Count == 0) return result;
        var (settings, links) = await sources.LinksAsync(db, feederIds, ct);
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            await Parallel.ForEachAsync(links, new ParallelOptions
            {
                MaxDegreeOfParallelism = 12, CancellationToken = budget.Token,
            }, async (entry, token) =>
            {
                if (!TryServer(settings, entry.Value.Profile, out _, out var uri)) return;
                try
                {
                    var device = await ReadDeviceAsync(uri, entry.Value.Profile.DeviceId, token);
                    result[entry.Key] = PowerStateOf(device, entry.Value.Profile.MaxSilenceSeconds, DateTimeOffset.UtcNow);
                }
                catch (LuxMapException error) when (error.Code == "IOT_SOURCE_UNAVAILABLE") { }
            });
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested && budget.IsCancellationRequested) { }
        return result;
    }

    private static FeederPowerState PowerStateOf(PrototypeDevice? device, int maxSilenceSeconds, DateTimeOffset now)
    {
        if (maxSilenceSeconds <= 0) return FeederPowerState.Unknown;
        var ack = device?.LastCommandAck;
        var sample = device?.LatestTelemetry;
        var freshAfter = now.AddSeconds(-maxSilenceSeconds);
        // RTC timestamps are local UTC+07:00. Archived samples can arrive long after they were measured.
        if (sample?.Data is { } data && MeasuredAt(data.Timestamp) is { } measuredAt)
        {
            var power = DecodePower(data.LampCommand);
            if (measuredAt >= freshAfter && measuredAt <= now &&
                (ack is null || measuredAt > ack.ReceivedAt) && power != FeederPowerState.Unknown)
                return power;
        }
        return ack is not null && ack.ReceivedAt >= freshAfter && ack.ReceivedAt <= now
            ? DecodePower(ack.Data?.LampCommand) : FeederPowerState.Unknown;
    }

    private static FeederPowerState DecodePower(string? state) => state switch
    {
        "ON" => FeederPowerState.On,
        "OFF" => FeederPowerState.Off,
        _ => FeederPowerState.Unknown,
    };

    public async Task<IotCommandStatus> SendCommandAsync(
        string nodeId, IotCommandRequest request, CancellationToken ct)
    {
        var (uri, deviceId, profile) = await DeviceSourceAsync(nodeId, ct);
        var owners = await sources.ControllersAsync(db, [profile.FeederId], ct);
        FeederControllerBinding.RequireCompatible(profile.FeederId, nodeId, owners.GetValueOrDefault(profile.FeederId));
        var canControl = await CompatibleFeederAsync(nodeId, profile.FeederId, true, ct);
        if (!canControl)
            throw new LuxMapException(ErrorCodes.ValidationFailed, HttpStatusCode.BadRequest,
                "Node chưa hỗ trợ điều khiển hoặc feeder không thuộc tủ điện của node.");
        var mode = request.Mode?.Trim().ToUpperInvariant();
        var command = request.Command?.Trim().ToUpperInvariant();
        if (!(mode == "AUTO" && command is null || mode == "MANUAL" && command is ("ON" or "OFF")))
            throw new LuxMapException(ErrorCodes.ValidationFailed, HttpStatusCode.BadRequest,
                "Dùng MANUAL với ON hoặc OFF, hoặc AUTO với command null.");

        var autoType = mode == "AUTO" ? request.AutoType?.Trim().ToLowerInvariant() ?? "scheduled" : null;
        if ((mode == "MANUAL" && request.AutoType is not null) ||
            (mode == "AUTO" && autoType is not ("full_time" or "scheduled")))
            throw new LuxMapException(ErrorCodes.ValidationFailed, HttpStatusCode.BadRequest,
                "auto_type chỉ nhận full_time hoặc scheduled trong chế độ AUTO.",
                new Dictionary<string, object?> { ["field"] = "auto_type" });

        BridgeAutoSchedule? bridgeSchedule = null;
        if (request.AutoSchedule is { } schedule)
        {
            if (mode != "AUTO" || autoType != "scheduled" || !TryParseClock(schedule.DayStart, out var day) ||
                !TryParseClock(schedule.NightStart, out var night) || day == night)
                throw new LuxMapException(ErrorCodes.ValidationFailed, HttpStatusCode.BadRequest,
                    "Lịch chỉ dùng với AUTO scheduled và cần hai giờ khác nhau theo định dạng HH:mm.");

            bridgeSchedule = new(day.Hour * 60 + day.Minute, night.Hour * 60 + night.Minute);
        }

        if (await ServerStatusAsync(uri, profile.ServerId ?? "default", ct) != "online")
            throw Unavailable("Gateway chưa online. Hãy thử lại khi gateway kết nối.");

        var commandId = Guid.NewGuid().ToString("N");
        var expiresAt = DateTimeOffset.FromUnixTimeSeconds(clock.GetUtcNow().AddSeconds(20).ToUnixTimeSeconds());
        var serverId = profile.ServerId ?? "default";
        await registry.AuditCommandAsync(serverId, nodeId, new
        {
            event_type = "command_requested", command_id = commandId, mode, command,
            device_id = deviceId, feeder_id = profile.FeederId,
            auto_schedule = request.AutoSchedule, auto_type = autoType, expires_at = expiresAt,
        }, ct);

        var endpoint = new Uri(uri, $"api/luxmap/devices/{Uri.EscapeDataString(deviceId)}/command");
        try
        {
            using var response = await client.PostAsJsonAsync(endpoint, new { mode, command, commandId, autoSchedule = bridgeSchedule, autoType,
                expiresAtUnixSeconds = expiresAt.ToUnixTimeSeconds() }, ct);
            response.EnsureSuccessStatusCode();
            if (!IsJson(response)) throw Unavailable("Server IoT không trả về JSON.");
            var saved = await response.Content.ReadFromJsonAsync<PrototypeCommand>(cancellationToken: ct);
            if (saved is null || saved.CommandId != commandId || saved.Mode != mode || saved.Command != command ||
                (mode == "AUTO" && (saved.AutoType ?? "scheduled") != autoType))
                throw Unavailable("Server IoT trả về lệnh không khớp yêu cầu.");
            await registry.AuditCommandAsync(serverId, nodeId, new
            {
                event_type = "command_accepted", command_id = commandId, mode, command,
            }, ct);
            return new(nodeId, commandId, mode!, command, false, saved.AutoSchedule?.ToApi(), saved.ExpiresAt, AutoType: autoType);
        }
        catch (Exception ex) when (IsSourceFailure(ex, ct))
        {
            throw Unavailable("Không xác nhận được việc gửi lệnh đến server IoT.");
        }
    }

    public async Task<IotCommandStatus> CommandAsync(string nodeId, CancellationToken ct)
    {
        var (uri, deviceId, profile) = await DeviceSourceAsync(nodeId, ct);
        var device = await ReadDeviceAsync(uri, deviceId, ct);
        var command = device?.LastSuccessfulCommand ?? (device?.CommandAcknowledged == true ? device.DesiredCommand : null);
        var ack = device?.LastSuccessfulAck ?? (device?.CommandAcknowledged == true ? device.LastCommandAck : null);
        if (command is not { Mode: "AUTO" or "MANUAL" } || ack is null)
            return new(nodeId, null, null, null, false, Status: "no_data");
        if (command.ExpiresAt is { } deadline && ack.ReceivedAt >= deadline &&
            ack.Data?.ExecutedAtUnixSeconds is { } applied)
            await registry.AuditLateReceiptAsync(profile.ServerId ?? "default", nodeId, command.CommandId!, ack.ReceivedAt, applied, ct);
        return new(nodeId, command.CommandId, command.Mode!, command.Command, true, command.AutoSchedule?.ToApi(), command.ExpiresAt,
            PowerStateOf(device, profile.MaxSilenceSeconds, clock.GetUtcNow()),
            command.Mode == "AUTO" ? command.AutoType ?? "scheduled" : null, "success",
            ack.Data?.ExecutedAtUnixSeconds is { } executed ? DateTimeOffset.FromUnixTimeSeconds(executed) : null,
            ack.ReceivedAt, command.ExpiresAt is { } expiry && ack.ReceivedAt >= expiry);
    }

    public async Task<PagedResult<IotCommandHistoryItem>> CommandHistoryAsync(
        string nodeId, PageRequest page, CancellationToken ct)
    {
        await RequireNodeAsync(nodeId, ct);
        var automatic = await ReadAutoHistoryAsync(nodeId, ct);
        var history = await registry.ReadCommandHistoryAsync(nodeId, page, ct, automatic);
        if (history.Items.Count == 0) return history;

        var settings = await registry.ReadAsync([], ct);
        Uri ResolveBridge(IotCommandHistoryItem item)
        {
            var address = settings.Servers.TryGetValue(item.ServerId, out var server) ? server.BaseUrl
                : item.ServerId == "default" ? settings.BaseUrl : null;
            return IotOrigin.TryAllowed(settings, address, out var uri) ? uri
                : throw Unavailable("Chưa cấu hình địa chỉ Bridge để kiểm tra ACK của lịch sử.");
        }

        var groups = history.Items.Where(item => item.Status != IotCommandHistoryStatus.Success)
            .GroupBy(ResolveBridge).ToArray();
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(TimeSpan.FromSeconds(5));
        var resolved = new System.Collections.Concurrent.ConcurrentDictionary<string, CommandReceipt>(StringComparer.Ordinal);
        try
        {
            await Parallel.ForEachAsync(groups, new ParallelOptions
            {
                MaxDegreeOfParallelism = 12, CancellationToken = budget.Token,
            }, async (group, token) =>
            {
                var keys = group.Select(item => new CommandReceiptKey(item.DeviceId, item.CommandId)).ToHashSet();
                foreach (var receipt in await ReadCommandReceiptsAsync(group.Key, keys, token))
                    resolved.TryAdd(receipt.CommandId, receipt);
            });
        }
        catch (Exception ex) when (IsSourceFailure(ex, ct))
        {
            throw Unavailable("Không kiểm tra được ACK của lịch sử từ IoT Bridge.");
        }

        foreach (var item in history.Items)
            if (resolved.TryGetValue(item.CommandId, out var receipt) &&
                receipt.ExecutedAtUnixSeconds is { } applied && receipt.ExpiresAtUnixSeconds is { } expiry &&
                receipt.ReceivedAt.ToUnixTimeSeconds() >= expiry && applied < expiry)
                await registry.AuditLateReceiptAsync(item.ServerId, nodeId, item.CommandId, receipt.ReceivedAt, applied, ct);

        return history with
        {
            Items = [.. history.Items.Select(item =>
            {
                if (item.RequestedBy == "sensor" && item.Status == IotCommandHistoryStatus.Success) return item;
                resolved.TryGetValue(item.CommandId, out var ack);
                var expires = item.ExpiresAt ?? new DateTimeOffset(DateTime.SpecifyKind(item.RequestedAt, DateTimeKind.Utc)).AddSeconds(20);
                var success = item.Status == IotCommandHistoryStatus.Success || ack is not null && ack.ReceivedAt >= item.RequestedAt &&
                    (ack.ReceivedAt < expires || ack.ExecutedAtUnixSeconds is { } executedAt &&
                        executedAt < expires.ToUnixTimeSeconds() && executedAt >= expires.AddSeconds(-20).ToUnixTimeSeconds() &&
                        executedAt <= ack.ReceivedAt.ToUnixTimeSeconds());
                return item with {
                    Status = success ? IotCommandHistoryStatus.Success : clock.GetUtcNow() >= expires
                        ? IotCommandHistoryStatus.Expired : IotCommandHistoryStatus.Pending,
                    ExpiresAt = expires,
                    ExecutedAt = ack?.ExecutedAtUnixSeconds is { } executed ? DateTimeOffset.FromUnixTimeSeconds(executed) : item.ExecutedAt,
                    AcknowledgedAt = ack?.ReceivedAt ?? item.AcknowledgedAt,
                    LateAck = success && ack is not null && ack.ReceivedAt >= expires,
                };
            })],
        };
    }

    private async Task<CommandReceipt[]> ReadCommandReceiptsAsync(
        Uri origin, HashSet<CommandReceiptKey> keys, CancellationToken ct)
    {
        using var response = await client.PostAsJsonAsync(new Uri(origin, "api/luxmap/devices/command-receipts"), keys, ct);
        response.EnsureSuccessStatusCode();
        if (!IsJson(response)) throw Unavailable("IoT Bridge không trả về JSON cho ACK của lịch sử.");
        var receipts = await response.Content.ReadFromJsonAsync<CommandReceipt[]>(cancellationToken: ct);
        if (receipts is null || receipts.Any(receipt => receipt is null ||
                !keys.Contains(new(receipt.DeviceId, receipt.CommandId))) ||
            receipts.Select(receipt => receipt.CommandId).Distinct(StringComparer.Ordinal).Count() != receipts.Length)
            throw Unavailable("IoT Bridge trả về ACK lịch sử không hợp lệ.");
        return receipts;
    }

    private sealed record CommandReceiptKey(string DeviceId, string CommandId);
    private sealed record CommandReceipt(string DeviceId, string CommandId, DateTimeOffset ReceivedAt,
        long? ExecutedAtUnixSeconds = null, long? ExpiresAtUnixSeconds = null);

    private async Task<(Uri Uri, string DeviceId, NodeProfile Profile)> DeviceSourceAsync(
        string nodeId, CancellationToken ct, bool includeDetached = false)
    {
        await RequireNodeAsync(nodeId, ct);
        var settings = await registry.ReadAsync([nodeId], ct, includeDetached);
        if (!settings.Nodes.TryGetValue(nodeId, out var profile) || !TryServer(settings, profile, out _, out var uri))
            throw Unavailable("Chưa cấu hình nguồn điều khiển cho thiết bị IoT.");
        return (uri, profile.DeviceId, profile);
    }

    public async Task<IotNodeSourceDetails> NodeSourceDetailsAsync(string nodeId, CancellationToken ct)
    {
        var communeId = await db.Set<IotNode>().AsNoTracking().Where(node => node.NodeId == nodeId)
            .Select(node => node.CommuneId).SingleOrDefaultAsync(ct);
        if (communeId is null)
            throw new LuxMapException(ErrorCodes.AssetNotFound, HttpStatusCode.NotFound, "Không tìm thấy thiết bị IoT.");
        var settings = await registry.ReadAsync([nodeId], ct);
        if (!settings.Nodes.TryGetValue(nodeId, out var profile) || !ServerMatchesCommune(settings, profile, communeId))
            throw new LuxMapException(ErrorCodes.AssetNotFound, HttpStatusCode.NotFound,
                "Node chưa có cấu hình nguồn đo đang hoạt động.");
        return new(nodeId, profile.ServerId ?? "default", profile.DeviceId, profile.FeederId,
            profile.NominalVoltage, profile.NominalCurrentMa, profile.NominalPowerMw, profile.RadiusKm);
    }

    public async Task<IotElectricalResult> ElectricalAsync(string nodeId, CancellationToken ct)
    {
        await RequireNodeAsync(nodeId, ct);
        var settings = await registry.ReadAsync([nodeId], ct);
        if (!settings.Nodes.TryGetValue(nodeId, out var profile) || !TryServer(settings, profile, out var id, out var uri))
            throw Unavailable("Chưa cấu hình nguồn đo cho thiết bị IoT.");

        var device = await ReadDeviceAsync(uri, profile.DeviceId, ct);
        var measurement = ReadMeasurement(device);
        var now = clock.GetUtcNow();
        var contact = ContactOf(device, measurement, profile, now);
        return new(nodeId, id, contact.Status, measurement,
            PowerStateOf(device, profile.MaxSilenceSeconds, now))
        {
            ElectricalStatus = contact.ElectricalStatus, LastSeenAt = contact.LastSeenAt,
            LastCommandAckAt = contact.LastCommandAckAt,
        };
    }

    public async Task<PagedResult<IotNodeStatus>> NodesAsync(PageRequest page, CancellationToken ct)
    {
        var nodes = db.Set<IotNode>().AsNoTracking().Where(node => node.CabinetId != null);
        var total = await nodes.CountAsync(ct);
        if (((long)page.Page - 1) * page.PageSize >= total)
            return PagedResult<IotNodeStatus>.From(page, total, []);
        var ids = await nodes.OrderBy(node => node.CreatedAt).ThenBy(node => node.NodeId.Length)
            .ThenBy(node => node.NodeId).Skip(page.Skip).Take(page.PageSize)
            .Select(node => node.NodeId).ToListAsync(ct);
        var settings = await registry.ReadAsync(ids, ct);
        var result = ids.Select(id => settings.Nodes.TryGetValue(id, out var profile) &&
            TryServer(settings, profile, out var serverId, out _)
            ? new IotNodeStatus(id, serverId, "source_unavailable", null) { ElectricalStatus = "source_unavailable" }
            : new IotNodeStatus(id, profile?.ServerId, "unconfigured", null) { ElectricalStatus = "unconfigured" }).ToArray();

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
            select new { node.NodeId, node.CabinetId, node.CommuneId, node.RadiusKm, Lat = cabinet.Geom.Y, Lng = cabinet.Geom.X })
            .ToDictionaryAsync(row => row.NodeId, ct);
        for (var index = 0; index < result.Length; index++)
            if (positions.TryGetValue(ids[index], out var position))
                result[index] = result[index] with
                {
                    CabinetId = position.CabinetId, CommuneId = position.CommuneId,
                    Location = new(position.Lat, position.Lng), RadiusKm = position.RadiusKm,
                };
        return PagedResult<IotNodeStatus>.From(page, total, result);
    }

    public async Task<IReadOnlyList<IotServerStatus>> ServersAsync(CancellationToken ct)
    {
        // A server is visible only if it has a registered node in the caller's commune scope.
        var visibleIds = await db.Set<IotNode>().AsNoTracking().Where(node => node.CabinetId != null)
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
            var health = await ReadServerHealthAsync(uri, id, token);
            result[index] = new(id, health.Status ?? "unreachable", visible ? communeId : null, location, visible ? profile?.RadiusKm ?? 0 : 0) {
                CredentialIssued = health.CredentialIssued,
            };
        });
        return result;
    }

    private async Task<string> ServerStatusAsync(Uri uri, string serverId, CancellationToken ct)
        => (await ReadServerHealthAsync(uri, serverId, ct)).Status ?? "unreachable";

    private async Task<GatewayHealth> ReadServerHealthAsync(Uri uri, string serverId, CancellationToken ct)
    {
        try {
            using var response = await client.GetAsync(new Uri(uri,
                $"api/luxmap/gateways/{Uri.EscapeDataString(serverId)}/status"), ct);
            if (!response.IsSuccessStatusCode || !IsJson(response)) return new("unreachable");
            var health = await response.Content.ReadFromJsonAsync<GatewayHealth>(cancellationToken: ct);
            return health?.Status is "online" or "offline" or "broker_down" or "unconfigured" ? health : new("unreachable");
        }
        catch (Exception ex) when (IsSourceFailure(ex, ct)) { return new("unreachable"); }
    }

    private async Task<IotNodeStatus> NodeStatusAsync(IotOptions settings, string nodeId, CancellationToken ct)
    {
        if (!settings.Nodes.TryGetValue(nodeId, out var profile) || !TryServer(settings, profile, out var id, out var uri))
            return new(nodeId, profile?.ServerId, "unconfigured", null) { ElectricalStatus = "unconfigured" };

        try
        {
            var device = await ReadDeviceAsync(uri, profile.DeviceId, ct);
            var measurement = ReadMeasurement(device);
            var contact = ContactOf(device, measurement, profile, clock.GetUtcNow());
            return new(nodeId, id, contact.Status, measurement?.ReceivedAt)
            {
                ElectricalStatus = contact.ElectricalStatus, LastSeenAt = contact.LastSeenAt,
                LastCommandAckAt = contact.LastCommandAckAt,
            };
        }
        catch (LuxMapException ex) when (ex.Code == "IOT_SOURCE_UNAVAILABLE")
        {
            return new(nodeId, id, "source_unavailable", null) { ElectricalStatus = "source_unavailable" };
        }
    }

    private async Task RequireNodeAsync(string nodeId, CancellationToken ct)
    {
        if (!await db.Set<IotNode>().AnyAsync(node => node.NodeId == nodeId, ct))
            throw new LuxMapException(ErrorCodes.AssetNotFound, HttpStatusCode.NotFound, "Không tìm thấy thiết bị IoT.");
    }

    internal static bool ServerMatchesCommune(IotOptions settings, NodeProfile profile, string communeId) =>
        profile.ServerId is null || (settings.Servers.TryGetValue(profile.ServerId, out var server) &&
            (server.CommuneId is null || server.CommuneId == communeId));

    private static bool TryServer(IotOptions settings, NodeProfile profile, out string id, out Uri uri)
    {
        id = profile.ServerId ?? "default";
        uri = null!;
        var url = settings.Servers.TryGetValue(id, out var server)
            ? server.BaseUrl : profile.ServerId is null ? settings.BaseUrl : null;
        return !string.IsNullOrWhiteSpace(profile.DeviceId) && profile.DeviceId is not ("." or "..") &&
            profile.MaxSilenceSeconds > 0 && IotOrigin.TryAllowed(settings, url, out uri);
    }

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

    private static DateTimeOffset? MeasuredAt(string? timestamp) =>
        DateTime.TryParseExact(timestamp, "yyyy-MM-ddTHH:mm:ss", System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var local) && local.Ticks >= TimeSpan.FromHours(7).Ticks
            ? new DateTimeOffset(local, TimeSpan.FromHours(7)) : null;

    private static IReadOnlyList<IotMeasurement> RecentOnMeasurements(PrototypeDevice device, NodeProfile profile, DateTimeOffset now)
    {
        var maximumGap = TimeSpan.FromSeconds(profile.MaxSilenceSeconds);
        var latest = device.LatestTelemetry;
        if (latest?.Data is not { SessionId: { Length: > 0 } session, SequenceNumber: > 0,
                LampCommand: "ON", Voltage: double voltage, Current: double current, Power: double power } data ||
            !Valid(voltage) || !Valid(current) || !Valid(power) || latest.ReceivedAt > now ||
            now - latest.ReceivedAt > maximumGap)
            return [];
        var measured = MeasuredAt(data.Timestamp);
        if (measured is null || measured > now || now - measured > maximumGap) return [];
        var samples = new List<IotMeasurement> { new(latest.ReceivedAt.UtcDateTime, "ON", voltage, current, power) };
        // Sorting by receipt time would mix SD replay with live samples. Require adjacent sequences.
        for (var offset = 1; offset < ElectricalWindowEvaluator.WindowSize; offset++)
        {
            var older = (device.History ?? []).Where(r => r.Data?.SessionId == session &&
                r.Data.SequenceNumber == data.SequenceNumber - offset).ToArray();
            if (older.Length != 1 || older[0].Data is not {
                    LampCommand: "ON", Voltage: double oldV, Current: double oldI, Power: double oldP } previous ||
                !Valid(oldV) || !Valid(oldI) || !Valid(oldP) ||
                older[0].ReceivedAt > now || now - older[0].ReceivedAt > maximumGap)
                return samples;
            var oldTime = MeasuredAt(previous.Timestamp);
            if (oldTime is null || oldTime >= measured || measured - oldTime > maximumGap ||
                now - oldTime > maximumGap)
                return samples;
            samples.Add(new(older[0].ReceivedAt.UtcDateTime, "ON", oldV, oldI, oldP));
            measured = oldTime;
        }
        return samples;
    }

    internal static IotDiagnosticResult Evaluate(
        IotDiagnosticResult result, IotMeasurement reading, NodeProfile profile, DateTimeOffset now,
        bool electricalAvailable = true)
    {
        var silence = Math.Max(0, (now - reading.ReceivedAt).TotalSeconds);
        if (result.MeasuredAt is { } measured)
            silence = Math.Max(silence, (now - measured).TotalSeconds);
        if (reading.ReceivedAt > now || result.MeasuredAt > now || silence > profile.MaxSilenceSeconds)
            return result with {
                Status = "stale", Measurement = reading,
                Anomalies = [new("telemetry_stale", silence, null, null, profile.MaxSilenceSeconds)],
            };
        // Electrical anomalies are decided together by the five-sample window.
        return result with { Status = "normal", Measurement = reading, Anomalies = [] };
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

    private sealed record GatewayHealth(string? Status, bool CredentialIssued = false);
    private sealed record PrototypeDevice(
        string? DeviceId, PrototypeReading? LatestTelemetry, PrototypeReading[]? History,
        PrototypeCommand? DesiredCommand, bool CommandAcknowledged, PrototypeAck? LastCommandAck,
        ReceivedSensorHealth? SensorHealth, DateTimeOffset? LastSeenAt = null, DateTimeOffset? LastCommandAckAt = null,
        PrototypeCommand? LastSuccessfulCommand = null, PrototypeAck? LastSuccessfulAck = null);
    private sealed record PrototypeAck(PrototypeAckData? Data, DateTimeOffset ReceivedAt);
    private sealed record PrototypeAckData(string? LampCommand, long? ExecutedAtUnixSeconds = null);
    private static bool TryParseClock(string? value, out TimeOnly time) =>
        TimeOnly.TryParseExact(value, "HH:mm", System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out time);

    private sealed record BridgeAutoSchedule(int DayStartMinute, int NightStartMinute)
    {
        public IotAutoSchedule? ToApi() =>
            DayStartMinute is >= 0 and < 1440 && NightStartMinute is >= 0 and < 1440
                ? new(Format(DayStartMinute), Format(NightStartMinute)) : null;

        private static string Format(int minute) => FormattableString.Invariant($"{minute / 60:00}:{minute % 60:00}");
    }

    private sealed record PrototypeCommand(string? Mode, string? Command, string? CommandId, BridgeAutoSchedule? AutoSchedule, long? ExpiresAtUnixSeconds, string? AutoType = null)
    {
        public DateTimeOffset? ExpiresAt => ExpiresAtUnixSeconds is >= 0 and <= 253402300799
            ? DateTimeOffset.FromUnixTimeSeconds(ExpiresAtUnixSeconds.Value) : null;
    }
    private sealed record PrototypeReading(DateTimeOffset ReceivedAt, PrototypeSample? Data);
    private sealed record PrototypeSample(
        string? LampCommand, double? Voltage, double? Current, double? Power, string? SessionId, long? SequenceNumber,
        string? Timestamp);
}
