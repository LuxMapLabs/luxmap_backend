using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LuxMap.Shared.Serialization;

/// <summary>
/// Contract v1.1 mục 0: thời gian trên API luôn là ISO 8601 UTC, hậu tố <c>Z</c>.
/// Chuẩn hoá kind ngay ở biên serialize — không vá tại chỗ gọi.
/// </summary>
public sealed class UtcDateTimeConverter : JsonConverter<DateTime>
{
    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => UtcNormalization.ToUtc(reader.GetDateTime());

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
        => writer.WriteStringValue(
            UtcNormalization.ToUtc(value).ToString(UtcNormalization.Iso8601Utc, CultureInfo.InvariantCulture));
}

/// <summary>
/// Bản <see cref="DateTimeOffset"/> của <see cref="UtcDateTimeConverter"/> — quy về offset 0 rồi in hậu tố <c>Z</c>.
/// </summary>
public sealed class UtcDateTimeOffsetConverter : JsonConverter<DateTimeOffset>
{
    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => reader.GetDateTimeOffset().ToUniversalTime();

    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options)
        => writer.WriteStringValue(
            value.ToUniversalTime().ToString(UtcNormalization.Iso8601Utc, CultureInfo.InvariantCulture));
}

public static class UtcNormalization
{
    /// <summary>
    /// Không mất dữ liệu: <c>F</c> bỏ số 0 thừa ở phần thập phân, và bỏ luôn dấu chấm khi
    /// phần thập phân bằng 0. Giây tròn in ra <c>2026-08-20T04:00:00Z</c> — đúng như bộ mock FO-26.
    /// </summary>
    public const string Iso8601Utc = "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'";

    /// <summary>
    /// Npgsql yêu cầu <see cref="DateTimeKind.Utc"/> cho <c>TIMESTAMPTZ</c>. <see cref="DateTimeKind.Unspecified"/>
    /// được coi là đã ở UTC — mọi thời điểm đi qua biên này theo contract đã là UTC.
    /// </summary>
    public static DateTime ToUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };
}
