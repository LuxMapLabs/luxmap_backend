using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LuxMap.Modules.Assets.Entities;
using LuxMap.Modules.Telemetry.Entities;
using LuxMap.Persistence;
using LuxMap.Shared.Contracts.Errors;
using LuxMap.Shared.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LuxMap.Modules.IOT;

public sealed class IotOptions
{
    public string? BaseUrl { get; set; }
    public Dictionary<string, NodeProfile> Nodes { get; set; } = new(StringComparer.Ordinal);
}

public sealed record NodeProfile
{
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

    internal bool IsValid() =>
        !string.IsNullOrWhiteSpace(DeviceId) && !string.IsNullOrWhiteSpace(FeederId) &&
        !string.IsNullOrWhiteSpace(PoleId) &&
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
    IotMeasurement? Measurement, IReadOnlyList<IotAnomaly> Anomalies);

public sealed class IotDiagnosticsService(
    LuxMapDbContext db, HttpClient client, IOptions<IotOptions> options)
{
    public async Task<IotDiagnosticResult> GetAsync(string nodeId, CancellationToken ct)
    {
        // Resolve the registered node through the normal commune filter before consulting the bridge.
        if (!await db.Set<IotNode>().AnyAsync(node => node.NodeId == nodeId, ct))
            throw new LuxMapException(ErrorCodes.AssetNotFound, HttpStatusCode.NotFound, "Không tìm thấy thiết bị IoT.");

        var settings = options.Value;
        if (!settings.Nodes.TryGetValue(nodeId, out var profile) || !profile.IsValid() ||
            !Uri.TryCreate(settings.BaseUrl, UriKind.Absolute, out var baseUri) ||
            baseUri.Scheme is not ("http" or "https") || baseUri.AbsolutePath != "/" ||
            baseUri.UserInfo.Length > 0 || baseUri.Query.Length > 0 || baseUri.Fragment.Length > 0)
        {
            throw Unavailable("Chưa cấu hình nguồn đo hoặc ngưỡng hợp lệ cho thiết bị IoT.");
        }

        var topology = await (
            from control in db.Set<FeederControl>()
            join feeder in db.Set<Feeder>() on control.FeederId equals feeder.FeederId
            join pole in db.Set<Pole>() on feeder.FeederId equals pole.FeederId
            where control.NodeId == nodeId && control.RelayNo == 1 &&
                  feeder.FeederId == profile.FeederId && pole.PoleId == profile.PoleId
            select new { feeder.FeederId, pole.PoleId, control.RelayNo })
            .SingleOrDefaultAsync(ct);
        if (topology is null)
            throw Unavailable("Trụ đèn phải thuộc feeder được gán cho relay 1 của node IoT.");

        var result = new IotDiagnosticResult(
            nodeId, topology.FeederId, topology.PoleId, topology.RelayNo, "no_data", null, []);
        var endpoint = new Uri(baseUri, "api/luxmap/devices/" + Uri.EscapeDataString(profile.DeviceId));
        PrototypeDevice? device;
        try
        {
            using var response = await client.GetAsync(endpoint, ct);
            if (response.StatusCode == HttpStatusCode.NotFound)
                return result;
            response.EnsureSuccessStatusCode();
            device = await response.Content.ReadFromJsonAsync<PrototypeDevice>(cancellationToken: ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException ||
                                   ex is OperationCanceledException && !ct.IsCancellationRequested)
        {
            throw Unavailable("Không đọc được dữ liệu từ server IoT.");
        }

        if (device is null || !string.Equals(device.DeviceId, profile.DeviceId, StringComparison.Ordinal))
            throw Unavailable("Server IoT trả về thiết bị không khớp cấu hình.");

        if (device.LatestTelemetry is not { } reading)
            return result;

        var sample = reading.Data;
        if (reading.ReceivedAt == default || sample?.LampCommand is not ("ON" or "OFF") ||
            !Valid(sample.Voltage) || !Valid(sample.Current) || !Valid(sample.Power))
            throw Unavailable("Server IoT trả về mẫu đo không hợp lệ.");

        var measurement = new IotMeasurement(reading.ReceivedAt.UtcDateTime, sample.LampCommand,
            sample.Voltage!.Value, sample.Current!.Value, sample.Power!.Value);
        return Evaluate(result, measurement, profile, DateTimeOffset.UtcNow);
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

    private sealed record PrototypeDevice(string? DeviceId, PrototypeReading? LatestTelemetry);
    private sealed record PrototypeReading(DateTimeOffset ReceivedAt, PrototypeSample? Data);
    private sealed record PrototypeSample(string? LampCommand, double? Voltage, double? Current, double? Power);
}
