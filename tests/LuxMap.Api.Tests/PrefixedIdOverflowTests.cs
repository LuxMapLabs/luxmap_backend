using LuxMap.Modules.Assets.Entities;
using LuxMap.Persistence;
using LuxMap.Shared.Contracts;
using LuxMap.Shared.Contracts.Enums;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;

namespace LuxMap.Api.Tests;

/// <summary>
/// Contract section 0.3: <i>"Khi vượt ngưỡng chữ số, ID dài ra tự nhiên — cột thứ 10000 là
/// POLE-10000. Không có cắt bớt, không có tràn số."</i>
/// <para>
/// This was broken from BE-06 until it was found here. PostgreSQL's
/// <c>lpad(string, length, fill)</c> <b>TRUNCATES</b> when the string is longer than
/// <c>length</c> — it does not return the longer string — so the original default produced:
/// </para>
/// <code>
/// lpad('9999',  4, '0') = '9999'   → POLE-9999
/// lpad('10000', 4, '0') = '1000'   → POLE-1000  ← collided with pole number 1000
/// </code>
/// <para>
/// A hard <c>23505</c>, so nothing corrupt was ever written — but the insert died, and one failed
/// statement aborts the whole surrounding transaction, so a BE-12 CSV import would have failed as a
/// batch rather than a row. It bit at the 1000th row for a 3-digit prefix, the 10000th for 4 digits,
/// and the 1000000th for <c>FRM</c>/<c>DET</c> — reachable, since one sweep produces hundreds of
/// frames a night.
/// </para>
/// <para>
/// These tests drive the REAL column default, not a hand-written expression: they move the sequence
/// to the edge and insert. <see cref="AssetSchemaFixture"/> restores <c>pole_id_seq</c> afterwards.
/// </para>
/// </summary>
[Collection(nameof(AssetSchemaCollection))]
public class PrefixedIdOverflowTests(AssetSchemaFixture fixture)
{
    [Fact]
    public async Task Ids_past_the_padding_width_grow_instead_of_truncating()
    {
        // 9999 is the last value that still fits the padding width; the next three cross it.
        await SetPoleSequenceAsync(9998);

        var ids = new List<string>
        {
            await InsertPoleAsync(),
            await InsertPoleAsync(),
            await InsertPoleAsync(),
            await InsertPoleAsync(),
        };

        Assert.Equal(["POLE-9999", "POLE-10000", "POLE-10001", "POLE-10002"], ids);
    }

    [Fact]
    public async Task The_ten_thousandth_pole_does_not_collide_with_the_one_thousandth()
    {
        // Any five-digit value used to truncate to its first four digits, so 30000 collided with
        // 3000 exactly as 10000 collided with 1000. The 30000 range keeps this clear of the 10000
        // range the test above occupies.
        Assert.True(
            AssetSchemaFixture.SyntheticPoleCount < 3000,
            "This test claims the 3000 and 30000 ids for itself; raising the synthetic seed past "
            + "3000 would overlap it. Move the range rather than deleting the assertion.");

        // Both rows are created here, so the test does not lean on which ids the fixture's bulk seed
        // happened to take.
        await SetPoleSequenceAsync(2999);
        var lowerId = await InsertPoleAsync();

        // Under the old default this insert produced 'POLE-3000' and died on the primary key. The
        // assertion is a formality — the insert above is the real check.
        await SetPoleSequenceAsync(29999);
        var higherId = await InsertPoleAsync();

        Assert.Equal("POLE-3000", lowerId);
        Assert.Equal("POLE-30000", higherId);
        Assert.NotEqual(lowerId, higherId);
    }

    [Fact]
    public async Task Padding_below_the_width_is_unchanged()
    {
        // Checked against rows the DEFAULT actually produced, rather than by inserting at a low
        // sequence value: the fixture's bulk seed already occupies 1..2500, so those IDs are taken.
        // The Theory below covers the expression itself across every width.
        var padded = await fixture.QueryAsync(db => db.Set<Pole>()
            .IgnoreQueryFilters()
            .Where(pole => pole.CommuneId == fixture.CommuneId)
            .OrderBy(pole => pole.PoleId)
            .Select(pole => pole.PoleId)
            .FirstAsync());

        // Whatever the sequence stood at, a value under 1000 must come back zero-padded to four.
        Assert.Matches(@"^POLE-\d{4,}$", padded);
        Assert.All(
            await fixture.QueryAsync(db => db.Set<Pole>()
                .IgnoreQueryFilters()
                .Where(pole => pole.CommuneId == fixture.CommuneId)
                .Select(pole => pole.PoleId)
                .ToListAsync()),
            id => Assert.Matches(@"^POLE-\d{4,}$", id));
    }

    [Theory]
    [InlineData(1L, "POLE-0001")]
    [InlineData(999L, "POLE-0999")]
    [InlineData(1000L, "POLE-1000")]
    [InlineData(9999L, "POLE-9999")]
    [InlineData(10000L, "POLE-10000")]
    [InlineData(123456L, "POLE-123456")]
    public async Task The_database_and_the_csharp_formatter_agree_at_every_width(long value, string expected)
    {
        // PrefixedIdSpec.Format uses PadLeft, which never truncates, so the C# side was always
        // right — the database was the half that disagreed. Tests and assertions build IDs through
        // Format, so the two must not drift apart again.
        Assert.Equal(expected, PrefixedIds.Pole.Format(value));

        var fromDatabase = await fixture.QueryAsync(async db =>
        {
            var connection = db.Database.GetDbConnection();
            await db.Database.OpenConnectionAsync();
            await using var command = connection.CreateCommand();
            command.CommandText =
                $"SELECT {PrefixedIdSpec.FormatFunction}('POLE', @value, 4);";

            var parameter = command.CreateParameter();
            parameter.ParameterName = "value";
            parameter.Value = value;
            command.Parameters.Add(parameter);

            return (string)(await command.ExecuteScalarAsync())!;
        });

        Assert.Equal(expected, fromDatabase);
    }

    [Fact]
    public async Task Every_declared_prefix_formats_through_the_same_database_function()
    {
        // All 16 rows of Contract section 0.2 share one expression, so none can be fixed and the
        // rest left behind.
        foreach (var spec in PrefixedIds.All)
        {
            // One value inside the padding width and one past it.
            long[] values = [1, (long)Math.Pow(10, spec.Digits) + 5];

            foreach (var value in values)
            {
                var fromDatabase = await FormatAsync(spec, value);
                Assert.Equal(spec.Format(value), fromDatabase);
            }
        }
    }

    private async Task<string> FormatAsync(PrefixedIdSpec spec, long value)
        => await fixture.QueryAsync(async db =>
        {
            var connection = db.Database.GetDbConnection();
            await db.Database.OpenConnectionAsync();
            await using var command = connection.CreateCommand();
            command.CommandText =
                $"SELECT {PrefixedIdSpec.FormatFunction}(@prefix, @value, @digits);";

            foreach (var (name, item) in new (string, object)[]
                     { ("prefix", spec.Prefix), ("value", value), ("digits", spec.Digits) })
            {
                var parameter = command.CreateParameter();
                parameter.ParameterName = name;
                parameter.Value = item;
                command.Parameters.Add(parameter);
            }

            return (string)(await command.ExecuteScalarAsync())!;
        });

    private Task SetPoleSequenceAsync(long value)
        => fixture.QueryAsync(async db =>
        {
            var connection = db.Database.GetDbConnection();
            await db.Database.OpenConnectionAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT setval('pole_id_seq', @value, true);";
            var parameter = command.CreateParameter();
            parameter.ParameterName = "value";
            parameter.Value = value;
            command.Parameters.Add(parameter);
            return await command.ExecuteScalarAsync();
        });

    private Task<string> InsertPoleAsync()
        => fixture.WriteAsSystemAsync(async db =>
        {
            var pole = new Pole
            {
                SegmentId = fixture.SegmentId,
                CommuneId = fixture.CommuneId,
                Geom = new Point(106.49, 10.97) { SRID = 4326 },
                DataSource = DataSource.PublicImagery,
            };

            db.Set<Pole>().Add(pole);
            await db.SaveChangesAsync();
            return pole.PoleId;
        });
}
