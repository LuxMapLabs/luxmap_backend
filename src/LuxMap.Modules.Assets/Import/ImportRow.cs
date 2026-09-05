using System.Globalization;
using System.Text.Json;
using LuxMap.Persistence.Conventions;
using LuxMap.Shared.Csv;
using NetTopologySuite.Geometries;

namespace LuxMap.Modules.Assets.Import;

/// <summary>
/// One row of an import file, whatever the file format was.
/// </summary>
/// <remarks>
/// The two readers differ ONLY here. Validation, error messages and the upsert all sit above this
/// interface, so a rule fixed for CSV is fixed for GeoJSON in the same edit — the alternative was two
/// validators that agree today and drift by the third ticket.
/// </remarks>
internal interface IImportRow
{
    /// <summary>Line number for CSV, 1-based feature index for GeoJSON. Both point a person at the file.</summary>
    int Row { get; }

    string? this[string column] { get; }

    bool TryGeometry(out Geometry? geometry, out string? error);
}

internal sealed class CsvImportRow(CsvRow row) : IImportRow
{
    public const string GeometryColumn = "geom_wkt";

    public int Row => row.LineNumber;

    public string? this[string column] => row[column];

    public bool TryGeometry(out Geometry? geometry, out string? error)
        => AssetGeometry.TryReadWkt(row[GeometryColumn], out geometry, out error);
}

internal sealed class GeoJsonImportRow(int index, JsonElement properties, JsonElement? geometry) : IImportRow
{
    public int Row => index;

    public string? this[string column]
    {
        get
        {
            if (properties.ValueKind != JsonValueKind.Object
                || !properties.TryGetProperty(column, out var value))
            {
                return null;
            }

            var text = value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Number => value.GetRawText(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => null,
            };

            return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        }
    }

    public bool TryGeometry(out Geometry? result, out string? error)
    {
        if (geometry is null)
        {
            result = null;
            error = "Feature has no geometry.";
            return false;
        }

        return AssetGeometry.TryReadGeoJson(geometry.Value, out result, out error);
    }
}

/// <summary>
/// Reads typed values off an <see cref="IImportRow"/>, collecting a message per bad cell instead of
/// throwing on the first one.
/// </summary>
/// <remarks>
/// Collecting rather than throwing is what makes the error report worth reading: a person fixing a
/// spreadsheet wants every problem on the row, not the first one, then a re-upload to discover the
/// second.
/// </remarks>
internal sealed class ImportRowReader(IImportRow row, List<ImportRowError> errors)
{
    public IImportRow Row => row;

    /// <summary>True while this row has produced no error — i.e. it is still eligible to be written.</summary>
    public bool IsValid { get; private set; } = true;

    public void Fail(string? column, string message)
    {
        IsValid = false;
        errors.Add(new ImportRowError(row.Row, column, message));
    }

    public string? Optional(string column) => row[column];

    public string? Required(string column)
    {
        var value = row[column];
        if (value is null)
        {
            Fail(column, "Required, but the cell is empty.");
        }

        return value;
    }

    public int RequiredInt(string column)
    {
        var text = Required(column);
        if (text is null)
        {
            return 0;
        }

        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            Fail(column, $"'{text}' is not a whole number.");
            return 0;
        }

        return value;
    }

    /// <summary>Contract section 0: dates without a time are <c>YYYY-MM-DD</c>, never the locale format.</summary>
    public DateOnly? Date(string column, bool required)
    {
        var text = required ? Required(column) : row[column];
        if (text is null)
        {
            return null;
        }

        if (!DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var value))
        {
            Fail(column, $"'{text}' is not a date in YYYY-MM-DD form.");
            return null;
        }

        return value;
    }

    public bool Flag(string column, bool fallback)
    {
        var text = row[column];
        if (text is null)
        {
            return fallback;
        }

        if (!bool.TryParse(text, out var value))
        {
            Fail(column, $"'{text}' is not true or false.");
            return fallback;
        }

        return value;
    }

    /// <summary>
    /// Parses a Contract section 1 enum from the EXACT wire string.
    /// </summary>
    /// <remarks>
    /// Matched against <c>ContractEnum.ToDbValue</c>, the same conversion the database CHECK
    /// constraint was built from — so a value this accepts is a value the column accepts, and the
    /// import can never hand PostgreSQL a string that trips a constraint at write time.
    /// </remarks>
    public TEnum RequiredEnum<TEnum>(string column)
        where TEnum : struct, Enum
    {
        var text = Required(column);
        if (text is null)
        {
            return default;
        }

        foreach (var candidate in Enum.GetValues<TEnum>())
        {
            if (string.Equals(ContractEnum.ToDbValue(candidate), text, StringComparison.Ordinal))
            {
                return candidate;
            }
        }

        Fail(column, $"'{text}' is not one of: {string.Join(", ", ContractEnum.AllDbValues<TEnum>())}.");
        return default;
    }

    public TGeometry? Geometry<TGeometry>(string column)
        where TGeometry : Geometry
    {
        if (!row.TryGeometry(out var geometry, out var error))
        {
            Fail(column, error!);
            return null;
        }

        if (geometry is not TGeometry typed)
        {
            Fail(column, $"Expected a {typeof(TGeometry).Name}, but the value is a {geometry!.GeometryType}.");
            return null;
        }

        return typed;
    }
}
