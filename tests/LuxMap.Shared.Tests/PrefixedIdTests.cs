using LuxMap.Shared.Contracts;

namespace LuxMap.Shared.Tests;

/// <summary>
/// The prefix table from Contract section 0.2. A wrong prefix or padding width corrupts every ID of
/// that entity, and Contract section 5.6 states plainly that fixing it after BE-09 means redoing all of them.
/// </summary>
public class PrefixedIdTests
{
    [Theory]
    [InlineData("POLE", 4, "POLE-0001")]
    [InlineData("FAULT", 4, "FAULT-0001")]
    [InlineData("SEG", 3, "SEG-001")]
    [InlineData("COM", 3, "COM-001")]
    [InlineData("FIX", 4, "FIX-0001")]
    [InlineData("FDR", 3, "FDR-001")]
    [InlineData("NODE", 3, "NODE-001")]
    [InlineData("SWP", 3, "SWP-001")]
    [InlineData("FRM", 6, "FRM-000001")]
    [InlineData("DET", 6, "DET-000001")]
    [InlineData("LUX", 4, "LUX-0001")]
    [InlineData("WO", 4, "WO-0001")]
    [InlineData("EVD", 4, "EVD-0001")]
    [InlineData("EXT", 3, "EXT-001")]
    [InlineData("USR", 3, "USR-001")]
    [InlineData("CLS", 3, "CLS-001")]
    public void Prefix_table_matches_contract_section_0_2(string prefix, int digits, string firstId)
    {
        var spec = PrefixedIds.All.Single(s => s.Prefix == prefix);

        Assert.Equal(digits, spec.Digits);
        Assert.Equal(firstId, spec.Format(1));
    }

    [Fact]
    public void All_sixteen_prefixes_are_declared_exactly_once()
    {
        Assert.Equal(16, PrefixedIds.All.Count);
        Assert.Equal(16, PrefixedIds.All.Select(s => s.Prefix).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(16, PrefixedIds.All.Select(s => s.SequenceName).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Default_value_sql_matches_the_form_contract_section_0_4_prescribes()
    {
        Assert.Equal(
            "luxmap_format_id('COM', nextval('commune_id_seq'), 3)",
            PrefixedIds.AdministrativeUnit.DefaultValueSql);

        Assert.Equal(
            "luxmap_format_id('POLE', nextval('pole_id_seq'), 4)",
            PrefixedIds.Pole.DefaultValueSql);
    }

    [Fact]
    public void Default_value_sql_never_pads_with_a_bare_lpad()
    {
        // `lpad(string, length, fill)` TRUNCATES when the string is longer than `length`, so
        // lpad('10000', 4, '0') is '1000' and the 10000th pole collided with the 1000th. Every
        // spec goes through luxmap_format_id, which uses greatest(digits, length(...)) instead.
        foreach (var spec in PrefixedIds.All)
        {
            Assert.DoesNotContain("LPAD", spec.DefaultValueSql, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(PrefixedIdSpec.FormatFunction, spec.DefaultValueSql, StringComparison.Ordinal);

            // nextval must appear exactly once: calling it twice would burn two values per row and
            // put the sequence out of step with the IDs actually issued.
            Assert.Equal(1, spec.DefaultValueSql.Split("nextval").Length - 1);
        }
    }

    [Fact]
    public void Id_grows_naturally_past_the_padding_width()
    {
        // Contract section 0.3: pole 10000 is POLE-10000 — no truncation, no overflow.
        //
        // ⚠️ This test passed all through BE-06 while the database was doing the opposite, because
        // Format uses PadLeft (which never truncates) and nothing exercised the SQL half. The
        // matching database-side assertions live in PrefixedIdOverflowTests, which needs a real
        // PostgreSQL and so cannot live here.
        Assert.Equal("POLE-10000", PrefixedIds.Pole.Format(10_000));
        Assert.Equal("COM-1000", PrefixedIds.AdministrativeUnit.Format(1_000));
    }

    [Fact]
    public void Sequence_names_are_snake_case_lowercase()
    {
        // Contract section 5.1: identifiers are unquoted, so they must be all lowercase.
        foreach (var spec in PrefixedIds.All)
        {
            Assert.Equal(spec.SequenceName.ToLowerInvariant(), spec.SequenceName);
            Assert.DoesNotContain(' ', spec.SequenceName);
        }
    }
}
