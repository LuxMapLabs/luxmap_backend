using System.Diagnostics;

namespace LuxMap.Api.Tests;

/// <summary>
/// The acceptance criterion of BE-09: <c>EXPLAIN</c> confirms that a <c>bbox</c> query rides the GIST
/// index rather than scanning the table, and Contract section 5.4 requires it to answer in under
/// 500 ms at 2000 poles.
/// </summary>
/// <remarks>
/// The plan is captured from a REAL <c>EXPLAIN (ANALYZE)</c> against
/// <see cref="AssetSchemaFixture.SyntheticPoleCount"/> rows, not asserted from the schema. Reading the
/// index out of <c>pg_indexes</c> would only prove it exists; it would not prove the planner picks it.
/// </remarks>
[Collection(nameof(AssetSchemaCollection))]
public class SpatialIndexTests(AssetSchemaFixture fixture)
{
    /// <summary>
    /// The shape Contract section 5.3 mandates: <c>ST_Intersects</c> against an envelope, which is
    /// what BE-14 will issue for <c>GET /poles?bbox=</c>.
    /// </summary>
    private const string BboxQuery = """
        SELECT pole_id FROM pole
        WHERE commune_id = @commune
          AND ST_Intersects(geom, ST_MakeEnvelope(106.20, 10.70, 106.25, 10.75, 4326));
        """;

    [Fact]
    public async Task A_bbox_query_uses_the_gist_index_and_does_not_scan_the_table()
    {
        var plan = await fixture.ExplainAsync(BboxQuery, ("commune", fixture.CommuneId));

        // Recorded in the test output so the plan reaches the report rather than only the assertion.
        Assert.False(string.IsNullOrWhiteSpace(plan));

        Assert.Contains("ix_pole_geom", plan, StringComparison.Ordinal);
        Assert.Contains("Index Scan", plan, StringComparison.Ordinal);

        // "Seq Scan on pole" is the failure this task exists to rule out. Matching the bare words
        // "Seq Scan" would be wrong: a bitmap plan legitimately mentions other nodes.
        Assert.DoesNotContain("Seq Scan on pole", plan, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_bbox_query_answers_well_inside_the_500ms_budget()
    {
        // Warm up first: the very first query of the run pays for connection setup and plan caching,
        // which is not what section 5.4 is about.
        await fixture.ExplainAsync(BboxQuery, ("commune", fixture.CommuneId));

        var stopwatch = Stopwatch.StartNew();
        var plan = await fixture.ExplainAsync(BboxQuery, ("commune", fixture.CommuneId));
        stopwatch.Stop();

        Assert.True(
            stopwatch.ElapsedMilliseconds < 500,
            $"Contract section 5.4 requires a bbox response under 500 ms at 2000 poles; this run took "
            + $"{stopwatch.ElapsedMilliseconds} ms over {AssetSchemaFixture.SyntheticPoleCount} poles."
            + Environment.NewLine + plan);
    }

    [Fact]
    public async Task A_bbox_covering_everything_is_allowed_to_scan()
    {
        // The counterpart to the test above, and the reason it is worth writing: when a bbox selects
        // essentially every row, a sequential scan IS the cheaper plan and PostgreSQL is right to
        // choose it. Asserting "always Index Scan" would be asserting a bug.
        var plan = await fixture.ExplainAsync(
            """
            SELECT pole_id FROM pole
            WHERE commune_id = @commune
              AND ST_Intersects(geom, ST_MakeEnvelope(-180, -90, 180, 90, 4326));
            """,
            ("commune", fixture.CommuneId));

        Assert.False(string.IsNullOrWhiteSpace(plan));
    }
}
