using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using LuxMap.Shared.Contracts.Errors;
using LuxMap.Shared.Http;

namespace LuxMap.Shared.Csv;

/// <summary>
/// BE-31 (drift W-5…W-7, ST-10) — <c>format=csv</c> for every report: one row per item, columns and values spelled
/// EXACTLY as the JSON response spells them.
/// </summary>
/// <remarks>
/// <para>
/// Each row goes through the same <see cref="JsonSerializerOptions"/> as the API, and the cells are read back out of
/// that JSON. A hand-written formatter would be a second answer to "how is a date / an enum / a null written", and
/// the two would drift — <c>calibration_rig</c> in JSON and <c>CalibrationRig</c> in the file.
/// </para>
/// <para>
/// UTF-8 with a BOM, CRLF, RFC 4180 quoting: Excel opens it with Vietnamese intact only when the BOM is there. A text
/// cell that Excel would read as a formula (<c>=</c>, <c>+</c>, <c>-</c>, <c>@</c>, tab, CR) gets a leading
/// apostrophe; numbers are JSON numbers, so a negative <c>days_remaining</c> stays a number.
/// </para>
/// </remarks>
public static class CsvExport
{
    /// <summary>More than this many rows is a 413, not a file: narrow the filter (W-6).</summary>
    public const int MaxRows = 10_000;

    public const string ContentType = "text/csv; charset=utf-8";

    /// <summary>Throws the 413 when <paramref name="rows"/> exceeds <see cref="MaxRows"/>.</summary>
    public static void EnsureWithinLimit(int rows)
    {
        if (rows > MaxRows)
        {
            throw new LuxMapException(ErrorCodes.ExportTooLarge, HttpStatusCode.RequestEntityTooLarge,
                $"The export would have {rows} rows; at most {MaxRows} are allowed. Narrow the filter.",
                new Dictionary<string, object?> { ["rows"] = rows, ["max_rows"] = MaxRows });
        }
    }

    /// <summary><c>format</c> is <c>json</c> (default) or <c>csv</c>; anything else is a 400 naming the field.</summary>
    public static bool IsRequested(string? format) => format?.Trim().ToLowerInvariant() switch
    {
        null or "" or "json" => false,
        "csv" => true,
        _ => throw new LuxMapException(ErrorCodes.ValidationFailed, HttpStatusCode.BadRequest,
            "format must be json or csv.", new Dictionary<string, object?> { ["field"] = "format" }),
    };

    public static byte[] Write<T>(IReadOnlyCollection<T> rows, JsonSerializerOptions options)
    {
        EnsureWithinLimit(rows.Count);

        // GetTypeInfo needs a resolver; options nobody has serialized with yet still lack one. Locking them is what the
        // first Serialize call would do anyway.
        if (!options.IsReadOnly) options.MakeReadOnly(populateMissingResolver: true);
        var columns = options.GetTypeInfo(typeof(T)).Properties.Select(p => p.Name).ToArray();
        var text = new StringBuilder();
        AppendLine(text, columns);

        foreach (var row in rows)
        {
            using var json = JsonDocument.Parse(JsonSerializer.SerializeToUtf8Bytes(row, options));
            AppendLine(text, columns.Select(column => Cell(json.RootElement.GetProperty(column), column)));
        }

        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(text.ToString())];
    }

    private static void AppendLine(StringBuilder text, IEnumerable<string> cells)
        => text.Append(string.Join(',', cells.Select(Quote))).Append("\r\n");

    private static string Cell(JsonElement value, string column) => value.ValueKind switch
    {
        JsonValueKind.Null => "",
        JsonValueKind.String => Defuse(value.GetString()!),
        JsonValueKind.Number => value.GetRawText(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        _ => throw new InvalidOperationException($"Column {column} is not a flat value; a CSV row must be flat."),
    };

    private static string Defuse(string text)
        => text.Length > 0 && text[0] is '=' or '+' or '-' or '@' or '\t' or '\r' ? "'" + text : text;

    private static string Quote(string cell)
        => cell.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? "\"" + cell.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"" : cell;

    /// <summary><c>&lt;name&gt;-&lt;yyyy-MM-dd&gt;.csv</c>, for the <c>Content-Disposition</c> header.</summary>
    public static string FileName(string name, DateOnly day) => $"{name}-{day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}.csv";
}

/// <summary>
/// Marks an action that also answers <c>format=csv</c>. The OpenAPI export adds a <c>text/csv</c> body to its 200 next to
/// the JSON one — two <c>ProducesResponseType</c> attributes on one status code would make the second REPLACE the first,
/// and the JSON schema would vanish from the spec.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class CsvExportAttribute : Attribute;
