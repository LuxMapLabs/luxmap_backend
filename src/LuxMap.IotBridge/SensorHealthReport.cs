namespace LuxMap.IotWire;

internal sealed record SensorHealthReport(string? DeviceId, string? SessionId, long? SequenceNumber,
    string? FirmwareVersion, string[]? Codes)
{
    public bool IsValidFor(string deviceId) => DeviceId == deviceId && ValidId(SessionId) &&
        SequenceNumber is > 0 and <= uint.MaxValue && ValidId(FirmwareVersion) &&
        Codes is { Length: <= 5 } && Codes.All(code => SensorHealthCode.GroupOf(code) != 0) &&
        Codes.Select(SensorHealthCode.GroupOf).Distinct().Count() == Codes.Length;

    private static bool ValidId(string? value) => value is { Length: > 0 and <= 64 } &&
        char.IsAsciiLetterOrDigit(value[0]) &&
        value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
}

internal sealed record ReceivedSensorHealth(SensorHealthReport Data, DateTimeOffset ReceivedAt);

internal static class SensorHealthCode
{
    public const int Rtc = 1, Ina = 2;

    public static int GroupOf(string? code) => code switch
    {
        "ds3231_unavailable" or "ds3231_read_failed" or "ds3231_time_invalid" => Rtc,
        "ina226_unavailable" or "ina226_read_failed" or "ina226_shunt_out_of_range" => Ina,
        "bh1750_unavailable" or "bh1750_read_failed" => 3,
        "microsd_unavailable" or "microsd_read_failed" or "microsd_write_failed" => 4,
        "lora_tx_blocked" or "lora_rx_frame_error" => 5,
        _ => 0,
    };
}
