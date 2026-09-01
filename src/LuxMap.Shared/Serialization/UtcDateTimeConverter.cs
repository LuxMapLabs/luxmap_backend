using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LuxMap.Shared.Serialization;

/// <summary>
/// Contract v1.1 section 0: API timestamps are always ISO 8601 UTC with a <c>Z</c> suffix.
/// Normalise the kind at the serialization boundary — never patch it at individual call sites.
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
/// The <see cref="DateTimeOffset"/> counterpart of <see cref="UtcDateTimeConverter"/> — shifts to
/// offset zero and writes the <c>Z</c> suffix.
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
    /// Lossless: capital <c>F</c> drops trailing zeros in the fraction, and drops the decimal point
    /// too when the fraction is zero. Whole seconds print as <c>2026-08-20T04:00:00Z</c> — exactly
    /// what the FO-26 mock set uses.
    /// </summary>
    public const string Iso8601Utc = "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'";

    /// <summary>
    /// Npgsql requires <see cref="DateTimeKind.Utc"/> for <c>TIMESTAMPTZ</c>.
    /// <see cref="DateTimeKind.Unspecified"/> is treated as already-UTC — by contract, everything
    /// crossing this boundary is UTC.
    /// </summary>
    public static DateTime ToUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };
}
