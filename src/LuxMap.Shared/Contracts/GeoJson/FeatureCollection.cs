using System.Text.Json.Serialization;

namespace LuxMap.Shared.Contracts.GeoJson;

/// <summary>
/// A GeoJSON <c>FeatureCollection</c>, the shape every map endpoint returns (Contract section 1.5).
/// </summary>
/// <remarks>
/// <para>
/// Coordinates are plain <c>double</c> arrays rather than NetTopologySuite types ON PURPOSE. This
/// assembly holds cross-cutting contracts and carries no packages; pulling NTS in here would put a
/// spatial library into every assembly that references Shared, including the two test projects that
/// are deliberately free of infrastructure. The caller projects its geometry to arrays.
/// </para>
/// <para>
/// ⚠️ <b>Order is <c>[lng, lat]</c></b> — GeoJSON's, and the reverse of how people say it. Getting it
/// backwards puts Vietnamese poles in Somalia, which is at least obvious; a subtler swap inside one
/// commune would not be.
/// </para>
/// </remarks>
public sealed record FeatureCollection<TProperties>
{
    [JsonPropertyOrder(-3)]
    public string Type => "FeatureCollection";

    /// <summary>
    /// Carried because <c>mock-poles.geojson</c> carries it, and the mock is the reference the
    /// Contract points at for this shape. Not a standard GeoJSON member; informational only.
    /// </summary>
    [JsonPropertyOrder(-2)]
    public string CrsNote => "EPSG:4326 (lng, lat)";

    [JsonPropertyOrder(-1)]
    public required IReadOnlyList<Feature<TProperties>> Features { get; init; }
}

/// <summary>One feature. <b>No <c>id</c> member</b> — Contract section 1.5 uses <c>properties.&lt;x&gt;_id</c>.</summary>
/// <remarks>
/// The omission is deliberate and worth keeping: <c>feature.id</c> is where MapLibre and most
/// examples put the key, so adding it is the natural mistake. The front end binds
/// <c>properties.pole_id</c>, and two places holding the same key is how they start disagreeing.
/// </remarks>
public sealed record Feature<TProperties>
{
    [JsonPropertyOrder(-3)]
    public string Type => "Feature";

    [JsonPropertyOrder(-2)]
    public required Geometry Geometry { get; init; }

    [JsonPropertyOrder(-1)]
    public required TProperties Properties { get; init; }
}

/// <summary>
/// A <c>Point</c> or a <c>LineString</c>, the only two shapes Branch C stores.
/// </summary>
/// <remarks>
/// One type rather than a hierarchy: <c>coordinates</c> is <c>[lng, lat]</c> for a point and a list
/// of those for a line, and <c>System.Text.Json</c> would need a custom converter to pick a subtype
/// on the way out for no gain a consumer can see.
/// </remarks>
public sealed record Geometry
{
    public required string Type { get; init; }

    /// <summary><c>double[]</c> for a Point, <c>double[][]</c> for a LineString.</summary>
    public required object Coordinates { get; init; }

    public static Geometry Point(double lng, double lat)
        => new() { Type = "Point", Coordinates = new[] { lng, lat } };

    public static Geometry LineString(IEnumerable<(double Lng, double Lat)> points)
        => new() { Type = "LineString", Coordinates = points.Select(p => new[] { p.Lng, p.Lat }).ToArray() };
}
