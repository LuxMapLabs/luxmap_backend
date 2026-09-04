namespace LuxMap.Persistence;

public static class SpatialConstants
{
    /// <summary>
    /// WGS84. Contract section 0: the API always returns EPSG:4326 and every geometry column is
    /// stored in this SRID.
    /// </summary>
    public const int Srid = 4326;

    /// <summary>
    /// VN-2000 / UTM zone 48N. Contract section 0: internal database and report export ONLY,
    /// never returned from the API. Declared here so nobody types a magic number; the one place it is
    /// used is <see cref="SpatialFunctions.DistanceMeters"/>, inside the SQL tree.
    /// </summary>
    /// <remarks>
    /// <b>Why leaking a 3405 coordinate through the API is dangerous rather than merely wrong.</b>
    /// The VN-2000↔WGS84 datum shift is a 7-parameter Helmert transform; measured against EPSG:32648
    /// (the same UTM zone with no datum shift) at the study site it moves a point by
    /// <b>226.15 m</b> — 196.76 m of easting and 111.49 m of northing. On the front end's map that is
    /// enough to place a pole on a different road, yet still small enough to look approximately right,
    /// so nobody would question it. The same shift is almost a rigid translation, so it very nearly
    /// cancels when two points are subtracted: over a 35 m pole spacing the RELATIVE error is
    /// <b>52 µm (1.5 ppm)</b>. Absolute coordinates are ruined; distances are not.
    /// <para>
    /// <b>What a 3405 distance actually is.</b> A distance on the PROJECTION PLANE, which runs about
    /// <b>73 ppm SHORT</b> of the distance on the ellipsoid — 2.57 mm over 35 m, 7.70 mm over 105 m.
    /// That is the UTM grid scale factor, not an error: the study site sits roughly 162 km east of the
    /// zone 48 central meridian at 105°E, where the k0 = 0.9996 scaling has not yet been undone. It is
    /// a difference of DEFINITION between plane and ellipsoid distance, it is accepted, and it must
    /// not be "corrected" by switching to the geography type.
    /// </para>
    /// <para>
    /// For scale, the failure this ordering guards against: <c>ST_Distance</c> on raw 4326 returns
    /// DEGREES, off by a factor of roughly 109,290 — four orders of magnitude beyond either figure
    /// above.
    /// </para>
    /// </remarks>
    public const int SridVn2000 = 3405;
}
