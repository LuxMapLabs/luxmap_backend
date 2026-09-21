using System.Diagnostics;
using System.Text.RegularExpressions;
using LuxMap.Modules.Map.Bbox;
using LuxMap.Modules.Map.Features;
using LuxMap.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LuxMap.Api.Tests;

/// <summary>
/// BE-14 — that the bbox endpoint's REAL query rides the GIST index and answers inside 500 ms.
/// </summary>
/// <remarks>
/// <para>
/// <c>SpatialIndexTests</c> already proves a bbox query CAN use <c>ix_pole_geom</c>, but it explains
/// SQL written by hand and its own comment calls that "what BE-14 will issue" — a prediction made
/// before the endpoint existed. This class explains the SQL Entity Framework actually generates for
/// <see cref="MapQueryService.PoleQuery"/>, so the plan under test is the plan production runs.
/// </para>
/// <para>
/// 🔴 <b>Why a plan test and not a timing test alone.</b> A query that stopped using the index would
/// return exactly the same rows, and on the 2500-row fixture it might still come in under 500 ms.
/// It would then degrade with the data until nobody could say which change caused it — the failure
/// BE-10 measured at cost 62608 versus 86. Rows cannot catch that; only the plan can.
/// </para>
/// </remarks>
[Collection(nameof(AssetDatabaseCollection))]
public sealed class MapQueryPlanTests(AssetSchemaFixture fixture)
{
    [Fact]
    public async Task The_generated_bbox_query_rides_the_gist_index_and_never_scans_the_pole_table()
    {
        var plan = await ExplainPoleQueryAsync();

        Assert.Contains("ix_pole_geom", plan, StringComparison.Ordinal);
        Assert.Contains("Index Scan", plan, StringComparison.Ordinal);

        // The failure this exists to rule out. Matched on "Seq Scan on pole" rather than the bare
        // words: a bitmap plan legitimately names other nodes.
        Assert.DoesNotContain("Seq Scan on pole", plan, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_generated_bbox_query_answers_well_inside_the_500ms_budget()
    {
        // Warm up: the first query of a run pays connection setup and plan caching, which is not
        // what Contract section 6 is about.
        await ExplainPoleQueryAsync();

        var stopwatch = Stopwatch.StartNew();
        var plan = await ExplainPoleQueryAsync();
        stopwatch.Stop();

        Assert.True(
            stopwatch.ElapsedMilliseconds < 500,
            $"Contract section 6 requires a bbox response under 500 ms at 2000 poles; this run took "
            + $"{stopwatch.ElapsedMilliseconds} ms over {AssetSchemaFixture.SyntheticPoleCount} poles."
            + Environment.NewLine + plan);
    }

    /// <summary>
    /// 🔴 The bbox predicate must name the column BARE — no <c>ST_Transform</c> around it.
    /// </summary>
    /// <remarks>
    /// This is the textual half of the guarantee, and it is worth having beside the plan test
    /// because it fails with a readable message. <c>ST_Transform(geom, 3405)</c> makes the predicate
    /// a FUNCTION of the indexed column, so the index stops applying while the rows stay identical
    /// (BE-10, rule 2). It would also put EPSG:3405 in the query path, which rule 3 forbids.
    /// </remarks>
    [Fact]
    public async Task The_bbox_predicate_compares_the_raw_4326_column_and_transforms_nothing()
    {
        var sql = await PoleSqlAsync();

        Assert.Contains("ST_Intersects(p.geom,", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("ST_Transform", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("3405", sql, StringComparison.Ordinal);
    }

    /// <summary>The calibration rig is excluded unless named — Contract section 1.6, in the SQL.</summary>
    /// <remarks>
    /// Asserted on the generated SQL rather than on rows because the fixture holds no calibration
    /// poles: a row-level test would pass just as well with the filter deleted.
    /// </remarks>
    [Fact]
    public async Task The_default_query_excludes_the_calibration_rig_in_sql()
    {
        Assert.Contains("data_source <> 'calibration_rig'", await PoleSqlAsync(), StringComparison.Ordinal);

        var explicitly = await PoleSqlAsync(new PoleMapQuery
        {
            Bbox = Box,
            DataSource = [LuxMap.Shared.Contracts.Enums.DataSource.CalibrationRig],
        });

        Assert.DoesNotContain("<> 'calibration_rig'", explicitly, StringComparison.Ordinal);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────────────────

    /// <summary>A box around the fixture's synthetic poles.</summary>
    private static BoundingBox Box => BoundingBox.Parse("106.20,10.70,106.25,10.75");

    private Task<string> PoleSqlAsync() => PoleSqlAsync(new PoleMapQuery { Bbox = Box });

    private Task<string> PoleSqlAsync(PoleMapQuery query)
        => fixture.QueryAsync(db => Task.FromResult(
            new MapQueryService(db).PoleQuery(query).ToQueryString()));

    private async Task<string> ExplainPoleQueryAsync()
        => await fixture.ExplainAsync(Literal(await PoleSqlAsync()));

    /// <summary>
    /// Replaces EF's parameters with literals so <c>EXPLAIN</c> can run the statement as written.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Substituted TEXTUALLY rather than bound by name because EF's generated parameter names carry
    /// a counter (<c>ef_filter__IsSystemWide2</c>) that an EF upgrade may renumber. Matching the
    /// stable prefix keeps the test from breaking for a reason unrelated to what it asserts.
    /// </para>
    /// <para>
    /// The commune filter is given the SCOPED form — <c>false OR commune_id = ANY(...)</c> — not
    /// <c>true</c>. A system-wide caller would leave <c>ST_Intersects</c> as the only predicate,
    /// which is the easiest possible plan; production callers are scoped, so the plan under test
    /// should be theirs.
    /// </para>
    /// </remarks>
    private string Literal(string sql)
    {
        // ToQueryString prefixes the parameter values as -- comments. Harmless to EXPLAIN, but
        // dropped so a failure message shows the statement and nothing else.
        var statement = string.Join(
            Environment.NewLine,
            sql.Split(Environment.NewLine).Where(line => !line.StartsWith("--", StringComparison.Ordinal)));

        statement = Regex.Replace(statement, @"@ef_filter__IsSystemWide\w*", "false");
        statement = Regex.Replace(
            statement, @"@ef_filter__CommuneIds\w*", $"ARRAY['{fixture.CommuneId}']");

        return statement.Replace(
            "@envelope",
            "ST_MakeEnvelope(106.20, 10.70, 106.25, 10.75, 4326)",
            StringComparison.Ordinal);
    }
}
