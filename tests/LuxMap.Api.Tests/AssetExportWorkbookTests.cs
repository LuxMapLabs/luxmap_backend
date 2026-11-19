using System.Globalization;
using System.Net;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using DocumentFormat.OpenXml.Validation;
using LuxMap.Modules.Assets.Entities;
using LuxMap.Modules.Assets.Export;
using LuxMap.Modules.Assets.Import;
using LuxMap.Shared.Contracts.Enums;
using Microsoft.EntityFrameworkCore;

namespace LuxMap.Api.Tests;

[Collection(nameof(AssetDatabaseCollection))]
public sealed class AssetExportWorkbookTests(AssetImportFixture fixture) : AssetWorkbookTestContext(fixture)
{
    private const string Url = "/api/v1/assets/export/workbook";
    private async Task<byte[]> Export()
    {
        using var client = Client(); using var response = await client.GetAsync(Url + "?commune_id=" + Commune);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(AssetExportWorkbook.ContentType, response.Content.Headers.ContentType!.MediaType);
        Assert.Contains($"assets-{DateTime.UtcNow:yyyy-MM-dd}.xlsx", response.Content.Headers.ContentDisposition!.FileNameStar);
        return await response.Content.ReadAsByteArrayAsync();
    }
    private static Worksheet Sheet(SpreadsheetDocument book, string name)
        => ((WorksheetPart)book.WorkbookPart!.GetPartById(book.WorkbookPart.Workbook!.Sheets!.Elements<Sheet>()
            .Single(s => s.Name == name).Id!)).Worksheet!;
    private static string[] Headers(Worksheet sheet) => sheet.Descendants<Row>().First().Elements<Cell>().Select(c => c.InnerText).ToArray();
    private static List<Dictionary<string, Cell>> Rows(Worksheet sheet)
    {
        var headers = Headers(sheet);
        return sheet.Descendants<Row>().Skip(1).Select(r => r.Elements<Cell>().ToDictionary(c => headers[c.CellReference!.Value![0] - 'A'], c => c)).ToList();
    }

    [Fact]
    public async Task All_assets_round_trip_including_empty_parents_routes_and_retired_fittings_without_stamp_changes()
    {
        var sheets = Full();
        sheets[0].Rows.Add((3, Row(("external_ref", "EMPTY-ROUTE"), ("segment_name", "No poles"), ("road_class", "inter_village"))));
        sheets[1].Rows.Add((3, Row(("external_ref", "EMPTY-CAB"), ("cabinet_name", "No circuits"), ("geom_wkt", "POINT(106.50 10.98)"))));
        sheets[2].Rows.Add((3, Row(("external_ref", "EMPTY-FEEDER"), ("feeder_name", "No poles"))));
        Committed(await Import(sheets)); await SetFeederRoute("MACH-A", "LINESTRING(106.49 10.97, 106.50 10.98)");
        var before = await Snapshot();
        var bytes = await Export();
        using (var stream = new MemoryStream(bytes))
        using (var book = SpreadsheetDocument.Open(stream, false))
        {
            foreach (var name in new[] { "Tuyến đường", "Tủ điện", "Mạch điện", "Bóng đèn" }) Assert.Equal(2, Rows(Sheet(book, name)).Count);
            Assert.Contains(Rows(Sheet(book, "Bóng đèn")), r => r.ContainsKey("removed_date"));
            // WB-21: the feeder sheet carries no route; the re-import below keeps the stored one (the snapshot includes feeder.geom).
            Assert.DoesNotContain("geom_wkt", Headers(Sheet(book, "Mạch điện")));
        }
        var result = await ImportBytes(bytes); Committed(result);
        foreach (var kind in new[] { "segments", "cabinets", "feeders", "poles", "fixtures" })
        {
            Assert.Equal(kind == "poles" ? 1 : 2, result.GetProperty(kind).GetProperty("unchanged").GetInt32());
            Assert.Equal(0, result.GetProperty(kind).GetProperty("inserted").GetInt32());
            Assert.Equal(0, result.GetProperty(kind).GetProperty("updated").GetInt32());
        }
        Assert.Equal(before, await Snapshot());
    }

    [Fact]
    public async Task Export_sheets_headers_types_dropdowns_and_named_ranges_match_committed_template()
    {
        Committed(await Import(Full()));
        using var stream = new MemoryStream(await Export()); using var book = SpreadsheetDocument.Open(stream, false);
        Assert.Empty(new OpenXmlValidator().Validate(book));
        using var template = SpreadsheetDocument.Open(Path.Combine(AssetImportMockSetTests.RepositoryRoot(), "docs/templates/assets-workbook.xlsx"), false);
        string[] Names(SpreadsheetDocument b) => b.WorkbookPart!.Workbook!.Sheets!.Elements<Sheet>().Select(s => s.Name!.Value!).ToArray();
        Assert.Equal(Names(template), Names(book));
        Assert.Equal(new[] { "Hướng dẫn", "Tuyến đường", "Tủ điện", "Mạch điện", "Cột điện", "Bóng đèn" }, Names(book));
        foreach (var layout in WorkbookSheets.All)
        {
            var sheet = Sheet(book, layout.Name); var original = Sheet(template, layout.Name);
            Assert.Equal(layout.Columns, Headers(sheet)); Assert.Equal(Headers(original), Headers(sheet));
            Assert.Equal(original.Descendants<DataValidation>().Select(v => (v.SequenceOfReferences!.InnerText, v.Formula1!.Text)),
                sheet.Descendants<DataValidation>().Select(v => (v.SequenceOfReferences!.InnerText, v.Formula1!.Text)));
            foreach (var row in Rows(sheet))
                foreach (var (column, cell) in row)
                    if (column.Contains("ref", StringComparison.Ordinal))
                    {
                        Assert.Equal(CellValues.InlineString, cell.DataType!.Value);
                        var format = book.WorkbookPart!.WorkbookStylesPart!.Stylesheet!.CellFormats!.Elements<CellFormat>().ElementAt((int)cell.StyleIndex!.Value);
                        Assert.Equal(49U, format.NumberFormatId!.Value);
                    }
        }
        // The dropdown sources must match; built-in names (_xlnm.*, e.g. the template's autofilters) are presentation only.
        static IEnumerable<(string?, string)> Ranges(DocumentFormat.OpenXml.Spreadsheet.Workbook workbook)
            => workbook.DefinedNames!.Elements<DefinedName>().Where(x => !x.Name!.Value!.StartsWith("_xlnm.", StringComparison.Ordinal))
                .Select(x => (x.Name!.Value, x.Text));
        Assert.Equal(Ranges(template.WorkbookPart!.Workbook!), Ranges(book.WorkbookPart!.Workbook!));
        var pole = Rows(Sheet(book, "Cột điện")).Single();
        Assert.Equal("007", pole["external_ref"].InnerText);
        Assert.Equal("POINT (106.4900123456789 10.9700123456789)", pole["geom_wkt"].InnerText);
        Assert.Equal("true", pole["near_sensitive_poi"].InnerText);
        Assert.Equal("inter_village", Rows(Sheet(book, "Tuyến đường")).Single()["road_class"].InnerText);
        var lamp = Rows(Sheet(book, "Bóng đèn")).Single(r => !r.ContainsKey("removed_date"));
        Assert.Equal("led_road_lamp", lamp["fixture_type"].InnerText); Assert.Equal("grid", lamp["power_source"].InnerText);
        Assert.Equal(CellValues.Number, lamp["lamp_watt"].DataType!.Value);
        Assert.Equal(CellValues.Number, lamp["install_date"].DataType!.Value);
        Assert.Equal(new DateTime(2022, 3, 24).ToOADate(), double.Parse(lamp["install_date"].InnerText, CultureInfo.InvariantCulture));
        Assert.Equal("yyyy-mm-dd", book.WorkbookPart!.WorkbookStylesPart!.Stylesheet!.NumberingFormats!.Elements<NumberingFormat>().Single().FormatCode!.Value);
        foreach (var row in Sheet(template, "Hướng dẫn").Descendants<Row>())
            Assert.Contains(row.InnerText, Sheet(book, "Hướng dẫn").InnerText);
        var guide = Sheet(book, "Hướng dẫn").InnerText;
        Assert.Contains("File nhiều xã không nạp lại được", guide); Assert.Contains("data_source", guide);
        // Guide source counts include both retired and active fittings.
        Assert.Contains(Sheet(book, "Hướng dẫn").Descendants<Row>(), r => r.Elements<Cell>().Select(c => c.InnerText)
            .SequenceEqual(new[] { "public_imagery", "1", "1", "1", "2" }));
    }

    [Fact]
    public async Task Unexportable_assets_of_every_kind_are_listed_with_reasons_and_never_detached()
    {
        Committed(await Import(Full()));
        await Fixture.QueryAsync(async db =>
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE road_segment SET external_ref = NULL WHERE commune_id = {Commune}");
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE electrical_cabinet SET external_ref = NULL WHERE commune_id = {Commune}");
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE feeder SET external_ref = NULL WHERE commune_id = {Commune}");
            return 0;
        });
        var before = await Snapshot();
        using (var stream = new MemoryStream(await Export()))
        using (var book = SpreadsheetDocument.Open(stream, false))
        {
            var excluded = Rows(Sheet(book, "Không xuất"));
            Assert.Equal(new[] { "kind", "id", "commune_id", "reason" }, Headers(Sheet(book, "Không xuất")));
            Assert.Equal(new[] { "segments", "cabinets", "feeders", "poles" }, excluded.Select(x => x["kind"].InnerText));
            Assert.All(excluded, r => { Assert.Equal(Commune, r["commune_id"].InnerText); Assert.NotEmpty(r["reason"].InnerText); });
            Assert.Empty(Rows(Sheet(book, "Cột điện")));
        }
        Committed(await ImportBytes(await Export())); Assert.Equal(before, await Snapshot());
        await Fixture.QueryAsync(db => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE pole SET external_ref = NULL WHERE commune_id = {Commune}"));
        using var secondStream = new MemoryStream(await Export()); using var second = SpreadsheetDocument.Open(secondStream, false);
        Assert.Equal(2, Rows(Sheet(second, "Không xuất")).Count(x => x["kind"].InnerText == "fixtures"));
        Assert.Empty(Rows(Sheet(second, "Bóng đèn")));
    }

    [Theory]
    [InlineData("pole", "pole_id", "POLE")]
    [InlineData("road_segment", "segment_id", "SEG")]
    [InlineData("electrical_cabinet", "cabinet_id", "CAB")]
    [InlineData("feeder", "feeder_id", "FDR")]
    [InlineData("fixture", "fixture_id", "FIX")]
    public async Task Every_kind_is_capped_before_exclusions(string table, string id, string prefix)
    {
        if (table == "fixture") Committed(await Import(Full().Take(4).ToArray()));
        await Fixture.QueryAsync(async db =>
        {
            var start = Random.Shared.NextInt64(1_000_000_000, 9_000_000_000);
            var ids = Enumerable.Range(1, 5001).Select(i => prefix + "-" + (start + i).ToString(CultureInfo.InvariantCulture)).ToArray();
            var collision = table switch
            {
                "pole" => await db.Set<Pole>().IgnoreQueryFilters().AnyAsync(x => ids.Contains(x.PoleId)),
                "road_segment" => await db.Set<RoadSegment>().IgnoreQueryFilters().AnyAsync(x => ids.Contains(x.SegmentId)),
                "electrical_cabinet" => await db.Set<ElectricalCabinet>().IgnoreQueryFilters().AnyAsync(x => ids.Contains(x.CabinetId)),
                "feeder" => await db.Set<Feeder>().IgnoreQueryFilters().AnyAsync(x => ids.Contains(x.FeederId)),
                _ => await db.Set<Fixture>().IgnoreQueryFilters().AnyAsync(x => ids.Contains(x.FixtureId)),
            };
            Assert.False(collision);
            var extra = table switch
            {
                "road_segment" => ", segment_name, road_class, data_source",
                "electrical_cabinet" => ", cabinet_name, geom, data_source",
                "feeder" => ", feeder_name", "fixture" => ", pole_id, fixture_type, power_source, lamp_watt, lamp_count, slot, install_date, removed_date, data_source",
                _ => ", data_source",
            };
            var values = table switch
            {
                "road_segment" => ", 'No poles', 'inter_village', 'public_imagery'",
                "electrical_cabinet" => ", 'No feeders', ST_GeomFromText('POINT(106.49 10.97)',4326), 'public_imagery'",
                "feeder" => ", 'No poles'",
                "fixture" => ", (SELECT pole_id FROM pole WHERE commune_id = {1}), 'led_road_lamp', 'grid', 100, 1, 1, DATE '2020-01-01', DATE '2021-01-01', 'public_imagery'",
                _ => ", 'public_imagery'",
            };
            // Closed InlineData controls SQL identifiers; actual values are bound parameters. Avoid changing shared sequences.
            var sql = $"INSERT INTO {table} ({id}, commune_id{extra}) SELECT '{prefix}-' || ({{0}} + i)::text, {{1}}{values} FROM generate_series(1, 5001) AS i";
            await db.Database.ExecuteSqlRawAsync(sql, start, Commune);
            return 0;
        });
        using var client = Client(); using var response = await client.GetAsync(Url);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode); Assert.Contains("EXPORT_TOO_LARGE", await response.Content.ReadAsStringAsync());
        using var other = Client(communes: [OtherCommune]); using var empty = await other.GetAsync(Url);
        Assert.Equal(HttpStatusCode.OK, empty.StatusCode);
    }

    [Fact]
    public async Task All_roles_authentication_scope_and_repeated_filters_are_preserved()
    {
        using (var anonymous = Fixture.CreateClient())
        using (var response = await anonymous.GetAsync(Url)) Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Committed(await Import(Full()));
        foreach (var role in Enum.GetValues<UserRole>())
        {
            using var client = Client(role); using var response = await client.GetAsync(Url + "?commune_id=" + Commune);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        using (var client = Client())
        using (var response = await client.GetAsync(Url + "?commune_id=" + OtherCommune)) Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using var wide = Client(communes: [Commune, OtherCommune]);
        using var both = await wide.GetAsync(Url + "?commune_id=" + Commune + "&commune_id=" + OtherCommune);
        Assert.Equal(HttpStatusCode.OK, both.StatusCode);
        using var stream = new MemoryStream(await both.Content.ReadAsByteArrayAsync()); using var book = SpreadsheetDocument.Open(stream, false);
        Assert.Single(Rows(Sheet(book, "Cột điện")));
    }
}
