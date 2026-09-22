using System.Globalization;
using System.Net;
using LuxMap.Shared.Contracts.Errors;
using LuxMap.Shared.Http;

namespace LuxMap.Modules.Map.Bbox;

/// <summary>
/// The <c>bbox</c> query parameter: <c>minLng,minLat,maxLng,maxLat</c> in EPSG:4326.
/// </summary>
/// <remarks>
/// <para>
/// <b>It is REQUIRED on every map endpoint and there is no "fetch everything".</b> Contract section
/// 5.1, and CLAUDE.md lists an unbounded variant as an anti-pattern: the point is not tidiness, it
/// is that a table scan over every pole in every commune cannot meet the 500 ms budget and would
/// get slower without anyone noticing which change did it.
/// </para>
/// <para>
/// ⚠️ <b>Non-finite values are refused explicitly.</b> <c>double.TryParse</c> accepts "NaN" and
/// "Infinity", and a NaN bound makes every PostGIS comparison false, so the endpoint would answer
/// <c>200</c> with an empty <c>FeatureCollection</c> — a wrong answer that looks like a right one.
/// This is the query-string twin of the CHECK constraints CLAUDE.md requires on measured doubles,
/// and of <c>FiniteDoubleConverter</c> on the JSON side.
/// </para>
/// </remarks>
public readonly record struct BoundingBox(double MinLng, double MinLat, double MaxLng, double MaxLat)
{
    private const int Parts = 4;

    /// <summary>Parses and validates, or throws the 400 the front end can act on.</summary>
    public static BoundingBox Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            throw Invalid("bbox is required: minLng,minLat,maxLng,maxLat in EPSG:4326.", raw);
        }

        var parts = raw.Split(',');

        if (parts.Length != Parts)
        {
            throw Invalid($"bbox needs exactly {Parts} comma-separated numbers, got {parts.Length}.", raw);
        }

        var values = new double[Parts];

        for (var i = 0; i < Parts; i++)
        {
            // InvariantCulture, not the server's: a machine set to vi-VN parses "10,97" as a decimal
            // and the comma separator makes that ambiguous anyway. The wire format is invariant.
            if (!double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out values[i])
                || !double.IsFinite(values[i]))
            {
                throw Invalid($"bbox value {i + 1} is not a finite number: '{parts[i].Trim()}'.", raw);
            }
        }

        var box = new BoundingBox(values[0], values[1], values[2], values[3]);

        if (box.MinLng >= box.MaxLng || box.MinLat >= box.MaxLat)
        {
            throw Invalid("bbox must be minLng,minLat,maxLng,maxLat with min strictly below max.", raw);
        }

        if (box.MinLng < -180 || box.MaxLng > 180 || box.MinLat < -90 || box.MaxLat > 90)
        {
            throw Invalid("bbox is outside the valid range of EPSG:4326.", raw);
        }

        return box;
    }

    private static LuxMapException Invalid(string message, string? raw)
        => new(
            ErrorCodes.ValidationFailed,
            HttpStatusCode.BadRequest,
            message,
            new Dictionary<string, object?> { ["bbox"] = raw });
}
