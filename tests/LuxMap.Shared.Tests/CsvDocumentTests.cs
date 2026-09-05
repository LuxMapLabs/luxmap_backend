using LuxMap.Shared.Csv;

namespace LuxMap.Shared.Tests;

/// <summary>
/// BE-12a — the CSV reader, against the file defects real spreadsheets actually produce.
/// </summary>
/// <remarks>
/// No database and no Docker: the parser lives in <c>LuxMap.Shared</c> with no package reference, so
/// these run in an assembly that has neither. Every case below is a trap named in
/// <c>docs/templates/README.md</c>, not a hypothetical.
/// </remarks>
public sealed class CsvDocumentTests
{
    private const string Header = "external_ref,segment_name,road_class,length_m,geom_wkt,commune_id,data_source";

    [Fact]
    public void A_utf8_BOM_does_not_become_part_of_the_first_column_name()
    {
        var document = CsvDocument.Parse("﻿" + Header + "\nA,Road,inter_commune,10,POINT(1 2),COM-001,field");

        // Without the strip the first header is "﻿external_ref", which matches nothing — and the
        // failure surfaces as "column missing" on every row, pointing nowhere near the cause.
        Assert.Equal("external_ref", document.Headers[0]);
        Assert.Equal("A", document.Rows[0]["external_ref"]);
    }

    [Fact]
    public void CRLF_line_endings_do_not_leave_a_carriage_return_on_the_last_column()
    {
        var document = CsvDocument.Parse(Header + "\r\nA,Road,inter_commune,10,POINT(1 2),COM-001,field\r\n");

        Assert.Single(document.Rows);
        Assert.Equal("field", document.Rows[0]["data_source"]);
    }

    [Fact]
    public void A_quoted_cell_keeps_the_commas_inside_it()
    {
        // This is docs/templates/segments.csv verbatim. string.Split(',') turns it into three columns
        // of nonsense, which is exactly why this parser exists.
        var document = CsvDocument.Parse(
            Header
            + "\nTUYEN-A,Tuyen A,inter_commune,1600,"
            + "\"LINESTRING(106.4900 10.9700, 106.4950 10.9705, 106.5010 10.9712)\",COM-001,public_imagery");

        Assert.Equal(
            "LINESTRING(106.4900 10.9700, 106.4950 10.9705, 106.5010 10.9712)",
            document.Rows[0]["geom_wkt"]);
        Assert.Equal("public_imagery", document.Rows[0]["data_source"]);
    }

    [Fact]
    public void A_semicolon_file_from_a_vietnamese_windows_locale_is_read_as_semicolon_separated()
    {
        var document = CsvDocument.Parse(
            Header.Replace(',', ';') + "\nA;Road;inter_commune;10;POINT(1 2);COM-001;field");

        Assert.Equal(';', document.Delimiter);
        Assert.Equal("COM-001", document.Rows[0]["commune_id"]);
    }

    [Fact]
    public void Commas_inside_a_quoted_cell_cannot_outvote_the_real_semicolon_delimiter()
    {
        // The sniff counts only UNQUOTED delimiters. Counting naively, this header-plus-row would look
        // comma-separated because of the WKT cell, and every column would shift.
        var document = CsvDocument.Parse(
            "external_ref;geom_wkt;commune_id\nA;\"LINESTRING(1 2, 3 4, 5 6)\";COM-001");

        Assert.Equal(';', document.Delimiter);
        Assert.Equal("LINESTRING(1 2, 3 4, 5 6)", document.Rows[0]["geom_wkt"]);
    }

    [Fact]
    public void A_doubled_quote_inside_a_quoted_cell_is_one_literal_quote()
    {
        var document = CsvDocument.Parse("external_ref,segment_name\nA,\"Tuyen \"\"A\"\" chinh\"");

        Assert.Equal("Tuyen \"A\" chinh", document.Rows[0]["segment_name"]);
    }

    [Fact]
    public void An_empty_file_yields_no_headers_and_no_rows_rather_than_throwing()
    {
        var document = CsvDocument.Parse(string.Empty);

        Assert.Empty(document.Headers);
        Assert.Empty(document.Rows);
    }

    [Fact]
    public void A_header_only_file_yields_headers_and_no_rows()
    {
        var document = CsvDocument.Parse(Header + "\n");

        Assert.Equal(7, document.Headers.Count);
        Assert.Empty(document.Rows);
    }

    [Fact]
    public void Missing_columns_are_reported_by_name()
    {
        var document = CsvDocument.Parse("external_ref,segment_name\nA,Road");

        Assert.Equal(
            ["commune_id", "data_source"],
            document.MissingColumns(["external_ref", "commune_id", "data_source"]));
    }

    [Fact]
    public void Line_numbers_point_at_the_line_a_person_would_open_the_file_to()
    {
        var document = CsvDocument.Parse(Header + "\nA,x,inter_commune,1,P,COM-001,field\nB,y,inter_village,2,P,COM-001,field");

        // Header is line 1, so the first data row is line 2 — what the spreadsheet shows.
        Assert.Equal(2, document.Rows[0].LineNumber);
        Assert.Equal(3, document.Rows[1].LineNumber);
    }

    [Fact]
    public void A_newline_inside_a_quoted_cell_does_not_split_the_record()
    {
        var document = CsvDocument.Parse("external_ref,note\nA,\"line one\nline two\"\nB,plain");

        Assert.Equal(2, document.Rows.Count);
        Assert.Equal("line one\nline two", document.Rows[0]["note"]);

        // The row after a wrapped cell still reports the line the person sees.
        Assert.Equal(4, document.Rows[1].LineNumber);
    }

    [Fact]
    public void Blank_and_whitespace_cells_read_as_null_so_callers_do_not_repeat_the_check()
    {
        var document = CsvDocument.Parse("a,b,c\n1,   ,");

        Assert.Equal("1", document.Rows[0]["a"]);
        Assert.Null(document.Rows[0]["b"]);
        Assert.Null(document.Rows[0]["c"]);
        Assert.Null(document.Rows[0]["column_that_does_not_exist"]);
    }

    [Fact]
    public void A_short_row_reads_as_null_rather_than_throwing_index_out_of_range()
    {
        var document = CsvDocument.Parse(Header + "\nA,Road");

        Assert.Equal("A", document.Rows[0]["external_ref"]);
        Assert.Null(document.Rows[0]["data_source"]);
    }

    [Fact]
    public void Trailing_blank_lines_do_not_become_empty_rows()
    {
        var document = CsvDocument.Parse(Header + "\nA,Road,inter_commune,10,P,COM-001,field\n\n\n");

        Assert.Single(document.Rows);
    }

    [Fact]
    public void Header_names_are_matched_case_insensitively_by_lower_casing_them()
    {
        var document = CsvDocument.Parse("External_Ref,COMMUNE_ID\nA,COM-001");

        Assert.Equal("A", document.Rows[0]["external_ref"]);
        Assert.Equal("COM-001", document.Rows[0]["commune_id"]);
    }
}
