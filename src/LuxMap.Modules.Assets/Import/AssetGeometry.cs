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

            parsed.SRID = SpatialConstants.Srid;
            geometry = parsed;
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException or InvalidOperationException)
        {
            // WKTReader signals malformed input with several exception types depending on where it
            // gives up. Catching the shape of the failure keeps a typo in one cell from becoming a 500.
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
