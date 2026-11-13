using System.Text.Json;
using LuxMap.Persistence;
using NetTopologySuite.Geometries;
using NetTopologySuite.Operation.Valid;

namespace LuxMap.Modules.Admin.Communes;

/// <summary>
/// Reads a commune outline (COMMUNE-BOUNDARY) from a GeoJSON <c>Polygon</c> or <c>MultiPolygon</c> geometry object into a
/// <see cref="MultiPolygon"/> in EPSG:4326, or says why it cannot.
/// </summary>
/// <remarks>
/// Hand-parsed for the reason <c>AssetGeometry</c> gives: NetTopologySuite's core ships no GeoJSON reader, and the package
/// that does would be a new dependency for two shapes of nested array. A single Polygon is wrapped so the column holds one
/// type. The SRID is stamped explicitly — a geometry built here would otherwise carry SRID 0.
/// </remarks>
internal static class CommuneBoundaryReader
{
    /// <summary>
    /// An outline is drawn on every map load of the commune's users; OSM's ward outlines here are ~400 vertices. Past this the
    /// input is almost certainly not a commune outline (a whole province, an undecimated survey trace).
    /// </summary>
    public const int MaxVertices = 10_000;

    private static readonly GeometryFactory Factory = new(new PrecisionModel(), SpatialConstants.Srid);

    public static bool TryRead(JsonElement geometry, out MultiPolygon? boundary, out string? error)
    {
        boundary = null;
        error = null;

        if (geometry.ValueKind != JsonValueKind.Object
            || !geometry.TryGetProperty("type", out var typeElement) || typeElement.ValueKind != JsonValueKind.String
            || !geometry.TryGetProperty("coordinates", out var coordinates) || coordinates.ValueKind != JsonValueKind.Array)
        {
            error = "boundary must be a GeoJSON geometry object with type and coordinates.";
            return false;
        }

        try
        {
            var polygons = typeElement.GetString() switch
            {
                "Polygon" => [ReadPolygon(coordinates)],
                "MultiPolygon" => coordinates.EnumerateArray().Select(ReadPolygon).ToArray(),
                var other => throw new FormatException($"geometry type '{other}' is not supported; expected Polygon or MultiPolygon"),
            };

            if (polygons.Length == 0)
            {
                throw new FormatException("a MultiPolygon needs at least one polygon");
            }

            var result = Factory.CreateMultiPolygon(polygons);
            if (result.NumPoints > MaxVertices)
            {
                error = $"boundary has {result.NumPoints} vertices; at most {MaxVertices} are accepted.";
                return false;
            }

            var invalid = new IsValidOp(result).ValidationError;
            if (invalid is not null)
            {
                error = $"boundary is not a valid polygon: {invalid.Message} near ({invalid.Coordinate?.X}, {invalid.Coordinate?.Y}).";
                return false;
            }

            boundary = result;
            return true;
        }
        catch (Exception exception) when (exception is FormatException or InvalidOperationException or ArgumentException
                                          or IndexOutOfRangeException)
        {
            // GetDouble on a non-number is InvalidOperationException; an unclosed ring is ArgumentException from LinearRing.
            error = $"boundary coordinates are malformed: {exception.Message}";
            return false;
        }
    }

    private static Polygon ReadPolygon(JsonElement rings)
    {
        var linear = rings.EnumerateArray().Select(ReadRing).ToArray();
        return linear.Length == 0
            ? throw new FormatException("a polygon needs an outer ring")
            : Factory.CreatePolygon(linear[0], linear[1..]);
    }

    private static LinearRing ReadRing(JsonElement positions)
    {
        var points = positions.EnumerateArray().Select(ReadPosition).ToArray();
        return points.Length < 4
            ? throw new FormatException("a ring needs at least four positions, the last equal to the first")
            : Factory.CreateLinearRing(points);
    }

    /// <summary>
    /// <c>[lng, lat]</c> — GeoJSON's order. RFC 7946 §3.1.1 allows an altitude as a third number; it is accepted and dropped
    /// (the column is 2D). Anything else — a fourth value, a <c>null</c> — is refused rather than silently ignored.
    /// </summary>
    private static Coordinate ReadPosition(JsonElement position)
    {
        var values = position.EnumerateArray().ToArray();
        if (values.Length is < 2 or > 3)
        {
            throw new FormatException("a position is [longitude, latitude] with an optional altitude");
        }

        if (values.Length == 3 && values[2].ValueKind != JsonValueKind.Number)
        {
            throw new FormatException("an altitude, when given, must be a number");
        }

        var (lng, lat) = (values[0].GetDouble(), values[1].GetDouble());
        return double.IsFinite(lng) && double.IsFinite(lat) && Math.Abs(lng) <= 180 && Math.Abs(lat) <= 90
            ? new Coordinate(lng, lat)
            : throw new FormatException($"position ({lng}, {lat}) is outside EPSG:4326 (longitude first)");
    }
}
