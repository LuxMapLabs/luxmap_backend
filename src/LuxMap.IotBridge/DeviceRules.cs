using System.Globalization;
using System.Text.RegularExpressions;

static class DeviceRules
{
    public static bool ValidId(string id) => id.Length is > 0 and <= 64 && Regex.IsMatch(id, "^[A-Za-z0-9][A-Za-z0-9_-]*$");

    public static string? ValidateTelemetry(string id, TelemetryRequest? data)
    {
        if (data is null || data.DeviceId != id) return "deviceId must match the node topic.";
        if (data.SessionId is null || !ValidId(data.SessionId) || data.SequenceNumber is null or <= 0 or > uint.MaxValue)
            return "sessionId and sequenceNumber (1..4294967295) are required for replay/deduplication.";
        if (data.Mode is not ("AUTO" or "MANUAL") || data.LampCommand is not ("ON" or "OFF"))
            return "mode: AUTO/MANUAL; lampCommand: ON/OFF.";
        if (data.Lux is not { } lux || !double.IsFinite(lux) || lux < 0 ||
            data.Voltage is not { } voltage || !double.IsFinite(voltage) ||
            data.Current is not { } current || !double.IsFinite(current) ||
            data.Power is not { } power || !double.IsFinite(power)) return "Invalid sensor values.";
        if (data.Timestamp is not null && !DateTime.TryParseExact(data.Timestamp,
            "yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)) return "Invalid RTC timestamp.";
        return null;
    }
}
