using LuxMap.Shared.Contracts;

namespace LuxMap.Shared.Tests;

/// <summary>
/// Bảng prefix ở Contract mục 0.2. Sai prefix hay sai số chữ số của một entity là sai toàn bộ
/// ID của entity đó, và Contract mục 5.6 nói rõ sửa sau BE-09 là phải sửa lại hết.
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
            "'COM-' || LPAD(nextval('commune_id_seq')::text, 3, '0')",
            PrefixedIds.AdministrativeUnit.DefaultValueSql);

        Assert.Equal(
            "'POLE-' || LPAD(nextval('pole_id_seq')::text, 4, '0')",
            PrefixedIds.Pole.DefaultValueSql);
    }

    [Fact]
    public void Id_grows_naturally_past_the_padding_width()
    {
        // Contract mục 0.3: cột thứ 10000 là POLE-10000, không cắt bớt, không tràn.
        Assert.Equal("POLE-10000", PrefixedIds.Pole.Format(10_000));
        Assert.Equal("COM-1000", PrefixedIds.AdministrativeUnit.Format(1_000));
    }

    [Fact]
    public void Sequence_names_are_snake_case_lowercase()
    {
        // Contract mục 5.1: identifier không quote, nên phải toàn chữ thường.
        foreach (var spec in PrefixedIds.All)
        {
            Assert.Equal(spec.SequenceName.ToLowerInvariant(), spec.SequenceName);
            Assert.DoesNotContain(' ', spec.SequenceName);
        }
    }
}
