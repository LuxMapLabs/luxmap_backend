using LuxMap.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LuxMap.Api.Tests;

/// <summary>
/// ⚠️ A DEFECT IN BE-06, found while building BE-09. Not yet fixed — fixing it changes
/// <c>PrefixedIdSpec.DefaultValueSql</c> and needs a migration that rewrites the DEFAULT of all 16
/// ID columns, which is outside this task's scope.
/// <para>
/// PostgreSQL's <c>lpad(string, length, fill)</c> <b>TRUNCATES</b> when the string is already longer
/// than <c>length</c>. It does not simply return the longer string, which is what the comment on
/// <c>PrefixedIdSpec.DefaultValueSql</c> currently claims. So once the sequence passes the padding
/// width the generated IDs start colliding with IDs already issued:
/// </para>
/// <code>
/// lpad('9999',  4, '0') = '9999'   → POLE-9999
/// lpad('10000', 4, '0') = '1000'   → POLE-1000  ← collides with pole number 1000
/// lpad('10005', 4, '0') = '1000'   → POLE-1000  ← and with every other 5-digit value
/// </code>
/// <para>
/// This directly contradicts Contract section 0.3: <i>"Khi vượt ngưỡng chữ số, ID dài ra tự nhiên —
/// cột thứ 10000 là POLE-10000. Không có cắt bớt, không có tràn số."</i>
/// </para>
/// <para>
/// It bites every entity, only at different sizes: the 1000th row for a 3-digit prefix
/// (SEG, FDR, COM, NODE, SWP, EXT, USR, CLS), the 10000th for 4 digits (POLE, FIX, FAULT, LUX, WO,
/// EVD), the 1000000th for 6 digits (FRM, DET). <c>SurveyFrame</c> is the one most likely to get
/// there: a survey sweep produces hundreds of frames a night.
/// </para>
/// <para>
/// The failure mode is a hard <c>23505 duplicate key</c>, so no corrupt data is written — but the
/// insert fails, and inside a transaction one failed statement aborts the whole transaction.
/// </para>
/// <para>
/// The fix is one expression: <c>LPAD(v::text, GREATEST(digits, LENGTH(v::text)), '0')</c>, wrapped
/// in a small SQL function so <c>nextval</c> is still evaluated exactly once. Verified by hand
/// against PostgreSQL 17: it yields <c>POLE-10000</c> and <c>POLE-123456</c> as section 0.3 requires.
/// (<c>to_char(v, 'FM0000')</c> is NOT a fix — it returns <c>####</c> on overflow.)
/// </para>
/// </summary>
[Collection(nameof(AssetSchemaCollection))]
public class PrefixedIdOverflowTests(AssetSchemaFixture fixture)
{
    [Fact(Skip = "Known BE-06 defect, awaiting a decision — see this class's summary. "
                 + "Un-skip together with the PrefixedIdSpec fix.")]
    public async Task Ids_past_the_padding_width_grow_instead_of_truncating()
    {
        var generated = await fixture.QueryAsync(async db =>
        {
            var connection = db.Database.GetDbConnection();
            await db.Database.OpenConnectionAsync();
            await using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT 'POLE-' || LPAD(v::text, 4, '0') FROM (VALUES (1000), (10000)) AS t(v);";

            var values = new List<string>();
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                values.Add(reader.GetString(0));
            }

            return values;
        });

        // Contract section 0.3: the 10000th pole is POLE-10000, and it must not collide with the
        // 1000th. Today both come out as POLE-1000.
        Assert.Equal("POLE-1000", generated[0]);
        Assert.Equal("POLE-10000", generated[1]);
    }

    [Fact]
    public async Task The_proposed_fix_produces_the_ids_contract_section_0_3_describes()
    {
        // Not a fix, just evidence that the replacement expression behaves: this documents WHAT to
        // change to, and stays green either way.
        var generated = await fixture.QueryAsync(async db =>
        {
            var connection = db.Database.GetDbConnection();
            await db.Database.OpenConnectionAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT 'POLE-' || LPAD(v::text, GREATEST(4, LENGTH(v::text)), '0')
                FROM (VALUES (1), (1000), (9999), (10000), (123456)) AS t(v);
                """;

            var values = new List<string>();
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                values.Add(reader.GetString(0));
            }

            return values;
        });

        Assert.Equal(
            ["POLE-0001", "POLE-1000", "POLE-9999", "POLE-10000", "POLE-123456"],
            generated);
    }
}
