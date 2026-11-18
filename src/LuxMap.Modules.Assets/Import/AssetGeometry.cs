using System.Text.Json;
using LuxMap.Persistence;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;

namespace LuxMap.Modules.Assets.Import;

/// <summary>
/// Turns the geometry an import file carries into a <see cref="Geometry"/> in EPSG:4326.
/// </summary>
/// <remarks>
/// ⚠️ <b><see cref="WKTReader"/> produces SRID 0, not 4326.</b> WKT itself carries no coordinate
/// system, so the reader has nothing to read one from. An SRID-0 geometry inserts into a
/// <c>geometry(Point,4326)</c> column only because PostGIS rejects it — loudly, which is the good
/// case. The bad case is anything that compares or transforms it first. The SRID is therefore
/// stamped EXPLICITLY on every geometry leaving this class.
/// </remarks>
internal static class AssetGeometry
{
    /// <summary>Reads WKT. Returns null and a reason rather than throwing — a bad cell is one row's problem.</summary>
    public static bool TryReadWkt(string? wkt, out Geometry? geometry, out string? error)
    {
        geometry = null;
        error = null;

        if (string.IsNullOrWhiteSpace(wkt))
        {
            error = "Geometry is required.";
            return false;
        }

        try
        {
            var parsed = new WKTReader().Read(wkt);
            if (parsed is null || parsed.IsEmpty)
            {
                error = "Geometry is empty.";
                return false;
            }

            if (OutOfRange(parsed) is { } rangeError)
            {
                error = rangeError;
                return false;
            }

            parsed.SRID = SpatialConstants.Srid;
            geometry = parsed;
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException or InvalidOperationException
                                          or NetTopologySuite.IO.ParseException)
        {
            // WKTReader signals malformed input with several exception types depending on where it
            // gives up — ParseException for "POINT(abc)", which was a 500 until POLE-GEOM-NULL PR 2b relied on this path.
            // Catching the shape of the failure keeps a typo in one cell from becoming a 500.
            error = $"Not valid WKT: {exception.Message}";
            return false;
        }
    }

    /// <summary>
    /// Reads a GeoJSON <c>geometry</c> object — <c>Point</c> and <c>LineString</c> only.
    /// </summary>
    /// <remarks>
    /// Hand-parsed with <see cref="JsonDocument"/> because NetTopologySuite's core assembly ships no
    /// GeoJSON reader (its <c>IO</c> namespace has GML2, GML3 and KML), and adding
    /// <c>NetTopologySuite.IO.GeoJSON4STJ</c> would be a new package. The subset that matters here is
    /// two shapes of a coordinate array, so the parser is smaller than the dependency would be.
    /// <para>
    /// Coordinates are <c>[longitude, latitude]</c>. That is GeoJSON's order and WKT's order, and the
    /// reverse of how the pair is usually spoken aloud in Vietnamese.
    /// </para>
    /// </remarks>
    public static bool TryReadGeoJson(JsonElement geometry, out Geometry? result, out string? error)
    {
        if (!TryParseGeoJson(geometry, out result, out error))
        {
            return false;
        }

        if (OutOfRange(result!) is { } rangeError)
        {
            result = null;
            error = rangeError;
            return false;
        }

        return true;
    }

    private static bool TryParseGeoJson(JsonElement geometry, out Geometry? result, out string? error)
    {
        result = null;
        error = null;

        if (geometry.ValueKind != JsonValueKind.Object
            || !geometry.TryGetProperty("type", out var typeElement)
            || !geometry.TryGetProperty("coordinates", out var coordinates))
        {
            error = "Feature has no usable geometry object.";
            return false;
        }

        var type = typeElement.GetString();

        try
        {
            switch (type)
            {
                case "Point":
                    result = new Point(ReadCoordinate(coordinates)) { SRID = SpatialConstants.Srid };
                    return true;

                case "LineString":
                    var points = coordinates.EnumerateArray().Select(ReadCoordinate).ToArray();
                    if (points.Length < 2)
                    {
                        error = "A LineString needs at least two positions.";
                        return false;
                    }

                    result = new LineString(points) { SRID = SpatialConstants.Srid };
                    return true;

                default:
                    error = $"Geometry type '{type}' is not supported; expected Point or LineString.";
                    return false;
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or FormatException or IndexOutOfRangeException)
        {
            error = $"Geometry coordinates are malformed: {exception.Message}";
            return false;
        }
    }

    /// <summary>
    /// Every coordinate must be a real EPSG:4326 position: finite, longitude within ±180, latitude within ±90.
    /// </summary>
    /// <remarks>
    /// Neither WKTReader nor PostGIS refuses <c>POINT(10.97 106.49)</c> — a latitude of 106° — so without this a pair typed
    /// in the spoken order (latitude first) was stored as a point nowhere near the commune and every distance to it was
    /// nonsense. In Vietnam the swap always pushes the latitude past 90, so naming the likely cause is cheap and right.
    /// </remarks>
    private static string? OutOfRange(Geometry geometry)
    {
        foreach (var coordinate in geometry.Coordinates)
        {
            var (lng, lat) = (coordinate.X, coordinate.Y);
            if (!double.IsFinite(lng) || !double.IsFinite(lat))
            {
                return "Coordinates must be finite numbers.";
            }

            if (Math.Abs(lng) > 180 || Math.Abs(lat) > 90)
            {
                var swapped = Math.Abs(lat) > 90 && Math.Abs(lat) <= 180 && Math.Abs(lng) <= 90
                    ? " The pair looks swapped: longitude comes first, e.g. POINT(106.49 10.97)."
                    : string.Empty;
                return $"Coordinate ({lng.ToString(System.Globalization.CultureInfo.InvariantCulture)} "
                    + $"{lat.ToString(System.Globalization.CultureInfo.InvariantCulture)}) is outside EPSG:4326: "
                    + $"longitude must be within ±180 and latitude within ±90.{swapped}";
            }
        }

        return null;
    }

    private static Coordinate ReadCoordinate(JsonElement position)
    {
        var values = position.EnumerateArray().ToArray();
        if (values.Length < 2)
        {
            throw new FormatException("a position needs a longitude and a latitude");
        }

        return new Coordinate(values[0].GetDouble(), values[1].GetDouble());
    }
}
