using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;
using NetTopologySuite.Geometries;

namespace LuxMap.Persistence;

/// <summary>
/// The ONLY sanctioned way to measure a distance in this codebase (BE-10).
/// </summary>
/// <remarks>
/// Every other route is banned at compile time by <c>BannedSymbols.txt</c> (RS0030), because on a
/// 4326 column they all answer in DEGREES rather than metres: NetTopologySuite's
/// <c>Geometry.Distance</c> and Npgsql's <c>EF.Functions.Distance</c> /
/// <c>EF.Functions.IsWithinDistance</c> / <c>EF.Functions.DistanceKnn</c>. The measured gap between
/// the degree answer and the metre answer for two adjacent poles is a factor of about 109,290 — large
/// enough that no plausible unit check would let it through, and precisely why it must be unavailable
/// rather than merely discouraged.
/// </remarks>
public static class SpatialFunctions
{
    /// <summary>
    /// Planar distance in metres between two geometries, computed by PostGIS in EPSG:3405.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>Query-only.</b> This method has no C# body — it is a marker that
    /// <see cref="SpatialFunctionModelBuilderExtensions.HasLuxMapSpatialFunctions"/> maps onto
    /// <c>ST_Distance(ST_Transform(a, 3405), ST_Transform(b, 3405))</c>. Calling it outside an
    /// <see cref="IQueryable{T}"/> throws, and so does a query EF failed to translate and fell back to
    /// evaluating on the client — which is the point: a silent client-side fallback would compute
    /// nothing at all here, so it is made loud.
    /// <para>
    /// Both parameters are non-nullable by design (BE-10). The scope is pole↔pole and pole↔segment,
    /// whose geometry columns are <c>NOT NULL</c>; <c>feeder.geom</c> is nullable and deliberately out
    /// of scope, so no caller has to reason about a null distance.
    /// </para>
    /// <para>
    /// The result is a distance on the PROJECTION PLANE — see <see cref="SpatialConstants.SridVn2000"/>
    /// for what that costs and why it is accepted.
    /// </para>
    /// </remarks>
    public static double DistanceMeters(Geometry a, Geometry b)
        => throw new InvalidOperationException(
            $"{nameof(SpatialFunctions)}.{nameof(DistanceMeters)} is usable only inside a LINQ query, "
            + "where EF Core translates it to ST_Distance(ST_Transform(a, 3405), ST_Transform(b, 3405)). "
            + "It has no client-side implementation on purpose: computing it in .NET would need a "
            + "reprojection this project does not ship, and returning a 4326 degree value instead "
            + "would be wrong by roughly five orders of magnitude.");
}

/// <summary>
/// Registers <see cref="SpatialFunctions"/> on the model. Called from
/// <see cref="LuxMapDbContext.OnModelCreating"/>.
/// </summary>
public static class SpatialFunctionModelBuilderExtensions
{
    /// <summary>
    /// Maps <see cref="SpatialFunctions.DistanceMeters"/> to a PostGIS expression.
    /// </summary>
    /// <remarks>
    /// <c>HasTranslation</c> rather than a real database function (BE-10 constraint): the SQL tree is
    /// built here, so NO <c>CREATE FUNCTION</c> and NO migration are needed, and the whole feature is
    /// a code change that can be reverted without touching a deployed schema.
    /// <para>
    /// The type mappings are passed explicitly instead of left to inference. An absent mapping is the
    /// failure mode this API is notorious for: EF emits syntactically valid SQL with the wrong literal
    /// form and the error surfaces far from its cause.
    /// </para>
    /// </remarks>
    public static ModelBuilder HasLuxMapSpatialFunctions(
        this ModelBuilder modelBuilder,
        IRelationalTypeMappingSource typeMappingSource)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        ArgumentNullException.ThrowIfNull(typeMappingSource);

        var doubleMapping = typeMappingSource.FindMapping(typeof(double))
            ?? throw new InvalidOperationException("No relational type mapping for double.");
        var intMapping = typeMappingSource.FindMapping(typeof(int))
            ?? throw new InvalidOperationException("No relational type mapping for int.");

        var method = typeof(SpatialFunctions).GetMethod(nameof(SpatialFunctions.DistanceMeters))
            ?? throw new InvalidOperationException(
                $"{nameof(SpatialFunctions.DistanceMeters)} was renamed without updating its mapping.");

        modelBuilder
            .HasDbFunction(method)
            .HasTranslation(args => new SqlFunctionExpression(
                "ST_Distance",
                [ToVn2000(args[0], intMapping), ToVn2000(args[1], intMapping)],
                // ST_Distance yields NULL when either operand is NULL. The parameters are
                // non-nullable in C#, but a query can still reach a nullable column, so the
                // conservative flag is the correct one — it only ever adds a null check.
                nullable: true,
                argumentsPropagateNullability: [true, true],
                typeof(double),
                doubleMapping));

        return modelBuilder;
    }

    /// <summary>
    /// <c>ST_Transform(geometry, 3405)</c>, keeping the argument's own CLR type and mapping so the
    /// result stays a geometry as far as EF is concerned.
    /// </summary>
    /// <remarks>
    /// The SRID is the second argument and is a plain integer, so it must NOT propagate nullability —
    /// a constant can never make the result null, and claiming it could would make EF wrap the call
    /// in a pointless null check.
    /// </remarks>
    private static SqlExpression ToVn2000(SqlExpression geometry, RelationalTypeMapping intMapping)
        => new SqlFunctionExpression(
            "ST_Transform",
            [
                geometry,
                new SqlConstantExpression(SpatialConstants.SridVn2000, typeof(int), intMapping),
            ],
            nullable: true,
            argumentsPropagateNullability: [true, false],
            geometry.Type,
            geometry.TypeMapping);
}
