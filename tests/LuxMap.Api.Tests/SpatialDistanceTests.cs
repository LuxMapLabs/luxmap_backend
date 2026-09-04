using LuxMap.Modules.Assets.Entities;
using LuxMap.Persistence;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;

namespace LuxMap.Api.Tests;

/// <summary>
/// BE-10 — <see cref="SpatialFunctions.DistanceMeters"/> against a real PostGIS database.
/// </summary>
/// <remarks>
/// These live here rather than in <c>LuxMap.Persistence.Tests</c> on purpose: that assembly and
/// <c>LuxMap.Shared.Tests</c> run without a database, and keeping that boundary intact is worth more
/// than the tidiness of filing the test next to the code. The whole question here — does PostGIS
/// return metres — cannot be asked without PostGIS.
/// <para>
/// The expected values are the ones measured during the BE-10 survey against
/// <c>imresamu/postgis:17-3.5</c> (PROJ 9.1.1), so a change in the projection pipeline shows up as a
/// failing test rather than as a quietly different number.
/// </para>
/// </remarks>
[Collection(nameof(AssetSchemaCollection))]
public class SpatialDistanceTests(AssetSchemaFixture fixture)
{
    /// <summary>POLE-0001 in <c>mocks/mock-poles.geojson</c>.</summary>
    private static Point PoleOne => new(106.49, 10.969973) { SRID = SpatialConstants.Srid };

    /// <summary>POLE-0002 — the neighbouring pole on SEG-001.</summary>
    private static Point PoleTwo => new(106.49032, 10.969973) { SRID = SpatialConstants.Srid };

    /// <summary>Measured: 34.97298392273707 m.</summary>
    private const double PoleOneToTwoMetres = 34.973;

    /// <summary>Measured: 0.0003200000000020964 — the SAME pair, left in 4326.</summary>
    private const double PoleOneToTwoDegrees = 0.00032;

    [Fact]
    public async Task The_distance_between_two_real_poles_comes_back_in_metres()
    {
        var metres = await fixture.QueryAsync(db => db.Set<Pole>()
            .IgnoreQueryFilters()
            .Select(_ => SpatialFunctions.DistanceMeters(PoleOne, PoleTwo))
            .FirstAsync());

        // ±0.01 m. Tight enough that a degree answer (0.00032) or a kilometre answer could never
        // pass, loose enough to survive a PROJ patch release.
        Assert.InRange(metres, PoleOneToTwoMetres - 0.01, PoleOneToTwoMetres + 0.01);
    }

    [Fact]
    public async Task The_same_pair_measured_in_raw_4326_is_off_by_five_orders_of_magnitude()
    {
        // Raw SQL, because every .NET route to this number is banned by RS0030 — which is the point.
        // The regression this guards against is someone "simplifying" DistanceMeters into a plain
        // ST_Distance: the result stays a double, stays positive, stays plausible, and is wrong by
        // a factor of about 109,290.
        var degrees = await fixture.QueryAsync(db => db.Database
            .SqlQueryRaw<double>(
                """
                SELECT ST_Distance(
                         ST_SetSRID(ST_MakePoint(106.49,    10.969973), 4326),
                         ST_SetSRID(ST_MakePoint(106.49032, 10.969973), 4326)) AS "Value"
                """)
            .SingleAsync());

        Assert.Equal(PoleOneToTwoDegrees, degrees, precision: 8);

        var metres = await fixture.QueryAsync(db => db.Set<Pole>()
            .IgnoreQueryFilters()
            .Select(_ => SpatialFunctions.DistanceMeters(PoleOne, PoleTwo))
            .FirstAsync());

        var ratio = metres / degrees;
        Assert.InRange(ratio, 109_000, 109_600);
    }

    [Fact]
    public async Task It_composes_into_where_and_order_by_without_falling_back_to_the_client()
    {
        // The fixture lays its 2500 poles on a 0.01 degree grid starting at this corner, so the
        // origin sits on a pole and the radius takes in its immediate neighbours.
        var origin = new Point(106.20, 10.70) { SRID = SpatialConstants.Srid };

        var ids = await fixture.QueryAsync(db =>
        {
            var query = db.Set<Pole>()
                .IgnoreQueryFilters()
                .Where(pole => SpatialFunctions.DistanceMeters(pole.Geom, origin) < 2000)
                .OrderBy(pole => SpatialFunctions.DistanceMeters(pole.Geom, origin))
                .Select(pole => pole.PoleId);

            // Asserted on the SQL, not on the result: a client-side fallback would still return rows
            // — after throwing from the method body — whereas a translated query proves the whole
            // expression reached PostGIS.
            var sql = query.ToQueryString();
            Assert.Contains("ST_Distance", sql, StringComparison.Ordinal);
            Assert.Contains("ST_Transform", sql, StringComparison.Ordinal);
            Assert.Contains(SpatialConstants.SridVn2000.ToString(), sql, StringComparison.Ordinal);

            return query.Take(5).ToListAsync();
        });

        Assert.NotEmpty(ids);
    }

    [Fact]
    public async Task A_bare_distance_predicate_cannot_use_the_gist_index_but_a_bbox_prefilter_can()
    {
        const string Origin = "ST_Transform(ST_SetSRID(ST_MakePoint(106.20, 10.70), 4326), 3405)";

        // Stage one on its own: ST_Transform makes the indexed expression a function of the column,
        // and `< 500` is not an indexable operator either way. This MUST scan.
        var withoutPrefilter = await fixture.ExplainAsync(
            $"""
             SELECT pole_id FROM pole
             WHERE ST_Distance(ST_Transform(geom, 3405), {Origin}) < 500;
             """);

        Assert.DoesNotContain("ix_pole_geom", withoutPrefilter, StringComparison.Ordinal);
        Assert.Contains("Seq Scan on pole", withoutPrefilter, StringComparison.Ordinal);

        // The two-stage convention BE-13/BE-14/BE-29 must follow: a bbox on the untransformed 4326
        // column narrows the candidate set through the index, and the metre test then runs over what
        // is left.
        var withPrefilter = await fixture.ExplainAsync(
            $"""
             SELECT pole_id FROM pole
             WHERE ST_Intersects(geom, ST_MakeEnvelope(106.19, 10.69, 106.21, 10.71, 4326))
               AND ST_Distance(ST_Transform(geom, 3405), {Origin}) < 500;
             """);

        Assert.Contains("ix_pole_geom", withPrefilter, StringComparison.Ordinal);
        Assert.DoesNotContain("Seq Scan on pole", withPrefilter, StringComparison.Ordinal);
    }
}
