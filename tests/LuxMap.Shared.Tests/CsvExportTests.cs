using System.Net;
using System.Text;
using LuxMap.Shared.Contracts.Enums;
using LuxMap.Shared.Contracts.Errors;
using LuxMap.Shared.Csv;
using LuxMap.Shared.Http;
using LuxMap.Shared.Serialization;

namespace LuxMap.Shared.Tests;

/// <summary>BE-31 — the CSV export spells every cell exactly as the JSON response does (drift W-5…W-7).</summary>
public sealed class CsvExportTests
{
    private sealed record Row(string PoleId, DataSource DataSource, DateOnly? WarrantyExpiry, int? DaysRemaining, string? Label);

    private static string Text(byte[] bytes)
    {
        Assert.Equal(Encoding.UTF8.GetPreamble(), bytes[..3]);
        return Encoding.UTF8.GetString(bytes[3..]);
    }

    [Fact]
    public void Columns_and_values_are_the_json_spelling_with_a_bom_and_crlf()
    {
        var csv = Text(CsvExport.Write(
            [new Row("POLE-0001", DataSource.CalibrationRig, new DateOnly(2027, 1, 31), -3, null)], LuxMapJsonOptions.Default));

        Assert.Equal("pole_id,data_source,warranty_expiry,days_remaining,label\r\nPOLE-0001,calibration_rig,2027-01-31,-3,\r\n", csv);
    }

    [Fact]
    public void An_empty_export_still_has_its_header()
        => Assert.Equal("pole_id,data_source,warranty_expiry,days_remaining,label\r\n",
            Text(CsvExport.Write(Array.Empty<Row>(), LuxMapJsonOptions.Default)));

    [Fact]
    public void Commas_quotes_and_line_breaks_are_quoted_and_vietnamese_survives()
    {
        var csv = Text(CsvExport.Write(
            [new Row("POLE-0002", DataSource.Field, null, null, "Đường \"Long Phước\",\nđoạn 1")], LuxMapJsonOptions.Default));

        Assert.EndsWith(",\"Đường \"\"Long Phước\"\",\nđoạn 1\"\r\n", csv);
    }

    [Theory]
    [InlineData("=1+1")]
    [InlineData("+cmd")]
    [InlineData("-2")]
    [InlineData("@SUM(A1)")]
    public void A_text_cell_a_spreadsheet_would_run_as_a_formula_is_defused(string label)
    {
        var csv = Text(CsvExport.Write([new Row("POLE-0003", DataSource.Field, null, -7, label)], LuxMapJsonOptions.Default));

        Assert.EndsWith($",-7,'{label}\r\n", csv);
    }

    [Fact]
    public void A_cell_starting_with_a_tab_or_a_carriage_return_is_defused_and_the_cr_quoted()
    {
        var tab = Text(CsvExport.Write([new Row("POLE-0004", DataSource.Field, null, null, "\tx")], LuxMapJsonOptions.Default));
        var cr = Text(CsvExport.Write([new Row("POLE-0005", DataSource.Field, null, null, "\rx")], LuxMapJsonOptions.Default));

        Assert.EndsWith(",'\tx\r\n", tab);
        Assert.EndsWith(",\"'\rx\"\r\n", cr);
    }

    [Fact]
    public void More_rows_than_the_cap_is_a_413_not_a_file()
    {
        var error = Assert.Throws<LuxMapException>(() => CsvExport.EnsureWithinLimit(CsvExport.MaxRows + 1));

        Assert.Equal(ErrorCodes.ExportTooLarge, error.Code);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, error.StatusCode);
        CsvExport.EnsureWithinLimit(CsvExport.MaxRows);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("json", false)]
    [InlineData(" CSV ", true)]
    public void Format_is_json_by_default_and_csv_on_request(string? format, bool csv)
        => Assert.Equal(csv, CsvExport.IsRequested(format));

    [Fact]
    public void Any_other_format_is_a_400_naming_the_field()
    {
        var error = Assert.Throws<LuxMapException>(() => CsvExport.IsRequested("xlsx"));

        Assert.Equal(ErrorCodes.ValidationFailed, error.Code);
        Assert.Equal("format", error.Details["field"]);
    }
}
