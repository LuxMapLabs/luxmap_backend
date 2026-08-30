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
    /// never returned from the API. Declared here so nobody types a magic number; no conversion
    /// code exists yet — that is out of scope for BE-03.
    /// </summary>
    public const int SridVn2000 = 3405;
}
