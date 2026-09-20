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
/// <para>
/// ⚠️ <b>The probe ids are chosen against the live table, never hardcoded.</b> The fixture's block of
/// <see cref="AssetSchemaFixture.SyntheticPoleCount"/> poles does NOT start at 1 — it starts wherever
/// <c>pole_id_seq</c> happens to stand, which on a shared development database is whatever the last
/// seed left behind. Once the FO-26 mock set was loaded the sequence stood at 854, the block landed
/// on 855..3354, and the literal 3000 this test used to claim was already taken: a hard 23505 on
/// every run, alone or in a suite. Pick the range from <see cref="FreeFourDigitDecadeAsync"/>.
/// </para>
/// </summary>
[Collection(nameof(AssetDatabaseCollection))]
public class PrefixedIdOverflowTests(AssetSchemaFixture fixture)
{
    [Fact]
    public async Task Ids_past_the_padding_width_grow_instead_of_truncating()
    {
        // 9999 is the last value that still fits the padding width; the next three cross it. This
        // pair is FIXED by the width — unlike the decade below it cannot be moved to a free range, so
        // the precondition is stated rather than worked around.
        await RequireFreeAsync(9999, 10000, 10001, 10002);
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
        // 3000 exactly as 10000 collided with 1000. Both rows are created here, so the test does not
        // lean on which ids anything else happened to take.
        var lower = await FreeFourDigitDecadeAsync();
        var higher = lower * 10;

        await SetPoleSequenceAsync(lower - 1);
        var lowerId = await InsertPoleAsync();

        // Under the old default this insert produced the lower id again and died on the primary key.
        // The assertions are a formality — the insert above is the real check.
        await SetPoleSequenceAsync(higher - 1);
        var higherId = await InsertPoleAsync();

        Assert.Equal(PrefixedIds.Pole.Format(lower), lowerId);
        Assert.Equal(PrefixedIds.Pole.Format(higher), higherId);
        Assert.NotEqual(lowerId, higherId);

        // Exactly what lpad(…, 4) used to return for the higher value: its first four digits. Stating
        // the relationship keeps the pair meaningful now that the numbers are not literals.
        Assert.Equal(lowerId, higherId[..^1]);
    }

    [Fact]
    public async Task Padding_below_the_width_is_unchanged()
    {
        // Checked against rows the DEFAULT actually produced, rather than by inserting at a low
        // sequence value: the fixture's bulk seed already holds a block of 2500 consecutive numbers,
        // so those ids are taken. The Theory below covers the expression itself across every width.
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

    /// <summary>
    /// The lowest four-digit multiple of 1000 whose id AND whose ten-fold id are both free.
    /// </summary>
    /// <remarks>
    /// Width 4 means <c>lpad</c> truncated a five-digit value to its first four digits, so the pair
    /// under test is always <c>(d, d × 10)</c> with <c>d</c> of four digits. Which <c>d</c> does not
    /// matter, and choosing it here rather than writing 3000 is the whole point: the fixture's block
    /// of synthetic poles sits wherever the sequence stood when the run began, so no literal is safe.
    /// <para>
    /// 1000 is left out on purpose: its ten-fold is 10000, the padding-width boundary that
    /// <see cref="Ids_past_the_padding_width_grow_instead_of_truncating"/> claims. xUnit does not
    /// promise an order for the methods of a class, so the two must not be able to want the same id.
    /// </para>
    /// </remarks>
    private async Task<long> FreeFourDigitDecadeAsync()
    {
        foreach (var decade in new long[] { 2000, 3000, 4000, 5000, 6000, 7000, 8000, 9000 })
        {
            if ((await TakenAsync(decade, decade * 10)).Count == 0)
            {
                return decade;
            }
        }

        Assert.Fail(
            "Every four-digit decade and its ten-fold is taken, so this test has nowhere to write. "
            + "The pole table is far fuller than any test run should leave it — clear it rather than "
            + "widening the search.");
        return 0;
    }

    /// <summary>Asserts that no row holds any of <paramref name="numbers"/>, naming the ones that do.</summary>
    /// <remarks>
    /// Without this the range being occupied surfaces as a raw <c>23505</c> from deep inside
    /// <c>SaveChanges</c>, which says nothing about why — that is exactly how the 3000 collision hid.
    /// </remarks>
    private async Task RequireFreeAsync(params long[] numbers)
    {
        var taken = await TakenAsync(numbers);

        Assert.True(
            taken.Count == 0,
            $"These ids must be free for this test to write them, but rows already hold "
            + $"{string.Join(", ", taken.Order(StringComparer.Ordinal))}. The pole table is carrying "
            + "leftovers from an earlier run, or pole_id_seq has been moved far past its seeded value.");
    }

    /// <summary>The ids among <paramref name="numbers"/> that a row already holds.</summary>
    private Task<List<string>> TakenAsync(params long[] numbers)
        => fixture.QueryAsync(db =>
        {
            var ids = numbers.Select(PrefixedIds.Pole.Format).ToList();

            return db.Set<Pole>()
                .IgnoreQueryFilters()
                .Where(pole => ids.Contains(pole.PoleId))
                .Select(pole => pole.PoleId)
                .ToListAsync();
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
