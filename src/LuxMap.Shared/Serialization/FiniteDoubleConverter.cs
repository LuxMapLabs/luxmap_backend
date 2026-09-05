using System.Text.Json;
using System.Text.Json.Serialization;

namespace LuxMap.Shared.Serialization;

/// <summary>
/// Refuses <c>NaN</c> and <c>±Infinity</c> at the JSON boundary.
/// </summary>
/// <remarks>
/// <b>Why this is needed at all.</b> <c>LuxMapJsonOptions</c> builds on
/// <see cref="JsonSerializerDefaults.Web"/>, which sets
/// <see cref="JsonNumberHandling.AllowReadingFromString"/> — and for floating-point types that flag
/// accepts the strings <c>"NaN"</c>, <c>"Infinity"</c> and <c>"-Infinity"</c>. So
/// <c>{"lux_value": "NaN"}</c> deserialised cleanly into <see cref="double.NaN"/>, with nothing
/// anywhere reporting a problem.
/// <para>
/// The database <c>CHECK</c> is the backstop, but on its own it turns this into a
/// <c>DbUpdateException</c> and a 500. Rejecting it here gives the caller the 400 it deserves and
/// names the field.
/// </para>
/// <para>
/// ⚠️ Deliberately NOT <see cref="JsonNumberHandling.Strict"/>. That would also stop accepting
/// ordinary quoted numbers such as <c>"12.4"</c>, which WP6 may well send — a wire-format change
/// affecting every endpoint, to fix a problem confined to three literals.
/// </para>
/// <para>
/// Same shape as <c>UtcDateTimeConverter</c>: a Contract rule enforced once at the boundary rather
/// than re-checked at every call site.
/// </para>
/// </remarks>
public sealed class FiniteDoubleConverter : JsonConverter<double>
{
    public override double Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = reader.TokenType == JsonTokenType.String
            ? ParseFromString(ref reader)
            : reader.GetDouble();

        return Ensure(value);
    }

    public override void Write(Utf8JsonWriter writer, double value, JsonSerializerOptions options)
        // Writing a non-finite value would emit a document no strict JSON parser can read, so the
        // caller would see a transport error instead of a data problem.
        => writer.WriteNumberValue(Ensure(value));

    /// <summary>Keeps <c>AllowReadingFromString</c>'s useful half: <c>"12.4"</c> still parses.</summary>
    private static double ParseFromString(ref Utf8JsonReader reader)
    {
        var text = reader.GetString();

        return double.TryParse(text, System.Globalization.NumberStyles.Float,
                   System.Globalization.CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : throw new JsonException($"'{text}' is not a number.");
    }

    private static double Ensure(double value)
        => double.IsFinite(value)
            ? value
            : throw new JsonException(
                "NaN and Infinity are not accepted. A measurement has to be a finite number — "
                + "a non-finite one turns every average computed from it into NaN, silently.");
}
