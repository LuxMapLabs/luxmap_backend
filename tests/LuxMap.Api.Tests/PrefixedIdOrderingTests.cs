using LuxMap.Modules.Assets.Entities;
using LuxMap.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LuxMap.Api.Tests;

/// <summary>
/// Contract section 0.3, the ordering half: an id width is a MINIMUM, so a listing must not sort
/// display ids as text.
/// </summary>
/// <remarks>
/// <para>
/// <c>PrefixedIdOverflowTests</c> covers GENERATION — that the 10000th pole is <c>POLE-10000</c>
/// rather than a truncation. This covers READING them back in order, which is a separate failure:
/// generation can be perfect and a listing still present <c>POLE-10000</c> between
/// <c>POLE-1000</c> and <c>POLE-9999</c>.
/// </para>
/// <para>
/// 🔴 <b>Why the tiebreaker is not a detail.</b> The published order is <c>created_at</c>, but
/// PostgreSQL <c>now()</c> is TRANSACTION-START time, so every row of one import shares a single
/// value — all 103 mock poles do. <c>created_at</c> therefore orders nothing within a batch, and the
/// tiebreaker is what actually decides. A tiebreaker on the display id alone reintroduces exactly
/// the bug section 0.3 forbids.
/// </para>
/// <para>
/// Ids are written EXPLICITLY with raw SQL rather than by moving a sequence. Rewinding
/// <c>segment_id_seq</c> past live rows is the poisoning that cost this repo a run of 36 red tests
/// and then 76 (CLAUDE.md, the id-collision note), and nothing here needs generation to be involved
/// — only the ordering is under test.
/// </para>
/// <para>
/// Segments rather than poles on purpose: the pole width boundary 9999/10000 is claimed by
/// <c>PrefixedIdOverflowTests</c> and, unlike a decade, it cannot be moved to a free range. The
/// segment boundary 999/1000 is the same rule on a sequence nothing else contends for.
/// </para>
/// </remarks>
[Collection(nameof(AssetDatabaseCollection))]
public sealed class PrefixedIdOrderingTests(AssetSchemaFixture fixture)
{
    private static readonly string[] Straddling = ["SEG-999", "SEG-1000", "SEG-1001", "SEG-1002"];

    [Fact]
    public async Task A_listing_orders_ids_past_the_padding_width_numerically_not_as_text()
    {
        await RequireFreeAsync();

        try
        {
            await WriteStraddlingSegmentsAsync();

            // Precondition, asserted rather than assumed: one statement, one created_at. If this
            // stopped holding, the test would be measuring created_at and not the tiebreaker.
            var stamps = await fixture.QueryAsync(db => db.Set<RoadSegment>().IgnoreQueryFilters()
                .Where(segment => Straddling.Contains(segment.SegmentId))
                .Select(segment => segment.CreatedAt).Distinct().ToListAsync());

            Assert.True(stamps.Count == 1, $"Expected one shared created_at, got {stamps.Count}.");

            var ordered = await ListAsync(query => query
                .OrderBy(segment => segment.CreatedAt)
                .ThenBy(segment => segment.SegmentId.Length)
                .ThenBy(segment => segment.SegmentId));

            Assert.Equal(["SEG-999", "SEG-1000", "SEG-1001", "SEG-1002"], ordered);

            // The sabotage, asserted directly so the test above cannot be mistaken for a restatement
            // of the insert order: text order really does put the four-digit ids first.
            var asText = await ListAsync(query => query
                .OrderBy(segment => segment.CreatedAt)
                .ThenBy(segment => segment.SegmentId));

            Assert.Equal(["SEG-1000", "SEG-1001", "SEG-1002", "SEG-999"], asText);
        }
        finally
        {
            await ExecuteAsync(
                "DELETE FROM road_segment WHERE segment_id IN ('SEG-999','SEG-1000','SEG-1001','SEG-1002');");
        }
    }

    private Task<List<string>> ListAsync(
        Func<IQueryable<RoadSegment>, IOrderedQueryable<RoadSegment>> order)
        => fixture.QueryAsync(db => order(db.Set<RoadSegment>().IgnoreQueryFilters()
                .Where(segment => Straddling.Contains(segment.SegmentId)))
            .Select(segment => segment.SegmentId)
            .ToListAsync());

    /// <summary>Names the ids already held, rather than letting a raw 23505 explain nothing.</summary>
    private async Task RequireFreeAsync()
    {
        var taken = await fixture.QueryAsync(db => db.Set<RoadSegment>().IgnoreQueryFilters()
            .Where(segment => Straddling.Contains(segment.SegmentId))
            .Select(segment => segment.SegmentId)
            .ToListAsync());

        Assert.True(
            taken.Count == 0,
            $"These segment ids must be free for this test to write them, but rows hold "
            + $"{string.Join(", ", taken.Order(StringComparer.Ordinal))}. An earlier run left them "
            + "behind, or something else has started writing explicit segment ids.");
    }

    /// <summary>
    /// All four in ONE statement, so they share a <c>created_at</c> and the tiebreaker is the only
    /// thing ordering them.
    /// </summary>
    private Task WriteStraddlingSegmentsAsync()
        => ExecuteAsync($"""
            INSERT INTO road_segment
                (segment_id, segment_name, road_class, length_m, geom, commune_id, data_source)
            VALUES
                ('SEG-999',  'ordering probe 999',  'inter_village', 100,
                 ST_GeomFromText('LINESTRING(106.49 10.97, 106.50 10.98)', 4326), '{fixture.CommuneId}', 'public_imagery'),
                ('SEG-1000', 'ordering probe 1000', 'inter_village', 100,
                 ST_GeomFromText('LINESTRING(106.49 10.97, 106.50 10.98)', 4326), '{fixture.CommuneId}', 'public_imagery'),
                ('SEG-1001', 'ordering probe 1001', 'inter_village', 100,
                 ST_GeomFromText('LINESTRING(106.49 10.97, 106.50 10.98)', 4326), '{fixture.CommuneId}', 'public_imagery'),
                ('SEG-1002', 'ordering probe 1002', 'inter_village', 100,
                 ST_GeomFromText('LINESTRING(106.49 10.97, 106.50 10.98)', 4326), '{fixture.CommuneId}', 'public_imagery');
            """);

    private Task ExecuteAsync(string sql)
        => fixture.QueryAsync(async db =>
        {
            var connection = db.Database.GetDbConnection();
            await db.Database.OpenConnectionAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            return await command.ExecuteNonQueryAsync();
        });
}
