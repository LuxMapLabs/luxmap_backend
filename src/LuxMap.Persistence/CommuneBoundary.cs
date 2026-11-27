using NetTopologySuite.Geometries;

namespace LuxMap.Persistence;

/// <summary>How a point sits against its own commune's outline (COMMUNE-BOUNDARY-GUARD D-1/D-2).</summary>
/// <remarks>
/// Three outcomes, not two: "no outline on file" must stay distinguishable from "outside the outline", because the
/// API answers a different <c>reason</c> for each and refusing work in a commune nobody has drawn yet is a different
/// message from refusing a position that is genuinely elsewhere.
/// </remarks>
public enum BoundaryVerdict
{
    /// <summary>Inside the outline, or exactly on its edge — zero tolerance, <see cref="Geometry.Covers"/> semantics.</summary>
    Inside,

    /// <summary>The commune HAS an outline and the point lies outside it.</summary>
    Outside,

    /// <summary>The commune has no outline at all.</summary>
    Missing,
}

/// <summary>
/// The ONE rule deciding whether a point asset may stand where it is (COMMUNE-BOUNDARY-GUARD D-6).
/// </summary>
/// <remarks>
/// Pure, so every write path evaluates exactly the same predicate: the CRUD services read the outline by primary key,
/// the importers read the outlines of a whole file in one query, and both call <see cref="Verdict"/>. There is deliberately
/// no <c>HasDbFunction</c> and no SQL translation to keep in step — the outline is a handful of vertices read from one row,
/// not a spatial search, so <c>ST_Covers</c> in PostGIS would buy nothing but a second definition.
/// <para>
/// A point on the boundary counts as inside: <see cref="Geometry.Covers"/> is the zero-tolerance "contains or touches"
/// predicate, unlike <see cref="Geometry.Contains"/>, which excludes the edge. Both geometries are EPSG:4326 and the
/// comparison stays in 4326 — this is topology, not distance, so there is nothing to reproject.
/// </para>
/// </remarks>
public static class CommuneBoundary
{
    public static BoundaryVerdict Verdict(MultiPolygon? boundary, Point point)
        => boundary is null
            ? BoundaryVerdict.Missing
            : boundary.Covers(point) ? BoundaryVerdict.Inside : BoundaryVerdict.Outside;

    /// <summary>
    /// D-10: a position changed when X or Y differs — the pattern <c>PoleRelocation.Measured.Unchanged</c> uses. NTS
    /// <see cref="Point.Equals(Geometry)"/> also weighs SRID and coordinate ordering, which is not what "moved" means here.
    /// </summary>
    public static bool Moved(Point? current, Point next) => current is null || current.X != next.X || current.Y != next.Y;
}
