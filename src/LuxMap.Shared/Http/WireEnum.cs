using System.Net;
using System.Text.Json;
using LuxMap.Shared.Contracts.Errors;

namespace LuxMap.Shared.Http;

/// <summary>
/// Query-string enum parsing in the WIRE spelling — the snake_case name the API serialises.
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>Matched against the SNAKE_CASE name, not with <c>Enum.TryParse</c>.</b> The wire values
/// of Contract section 3.1 are lowercase snake_case — <c>calibration_rig</c>,
/// <c>field_report</c>, <c>node_offline</c> — and <c>Enum.TryParse</c> does not know about the
/// underscore, so it rejects every multi-word member while happily accepting single-word ones
/// like <c>normal</c>. That shape of bug hides: the common filters work and a few values are
/// simply refused.
/// </para>
/// <para>
/// The comparison uses the SAME policy that serialises these enums on the way out, so the values
/// a client reads back are exactly the values it may send.
/// </para>
/// </remarks>
public static class WireEnum
{
    /// <summary>
    /// Reads a comma-separated enum list, refusing an unknown member by name. <c>null</c> when the
    /// parameter is absent or blank.
    /// </summary>
    /// <remarks>
    /// Callers bind the parameter as a string and parse it here rather than as <c>TEnum[]</c>,
    /// because the framework's binder answers an unparseable value with a generic 400 that does not
    /// say WHICH value it rejected — and for an enum the front end hardcodes, that is the one thing
    /// worth saying.
    /// </remarks>
    public static IReadOnlyList<TEnum>? ParseCsv<TEnum>(string? raw, string parameter)
        where TEnum : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var parsed = new List<TEnum>();

        foreach (var part in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var match = Enum.GetValues<TEnum>()
                .Where(candidate => Name(candidate).Equals(part, StringComparison.OrdinalIgnoreCase))
                .Select(candidate => (TEnum?)candidate)
                .FirstOrDefault();

            if (match is not { } value)
            {
                throw new LuxMapException(
                    ErrorCodes.ValidationFailed,
                    HttpStatusCode.BadRequest,
                    $"'{part}' is not a valid {parameter}.",
                    new Dictionary<string, object?>
                    {
                        [parameter] = part,
                        ["allowed"] = Enum.GetValues<TEnum>().Select(Name).ToArray(),
                    });
            }

            parsed.Add(value);
        }

        return parsed;
    }

    /// <summary>The value as it appears on the wire — the same policy <c>LuxMapJsonOptions</c> uses.</summary>
    public static string Name<TEnum>(TEnum value)
        where TEnum : struct, Enum
        => JsonNamingPolicy.SnakeCaseLower.ConvertName(value.ToString());
}
