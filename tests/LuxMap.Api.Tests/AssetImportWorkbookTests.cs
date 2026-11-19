using System.IO.Compression;
using System.Net;
using System.Text;
using DocumentFormat.OpenXml.Spreadsheet;
using LuxMap.Modules.Assets.Entities;
using LuxMap.Modules.Assets.Import;
using Microsoft.EntityFrameworkCore;

namespace LuxMap.Api.Tests;

[Collection(nameof(AssetDatabaseCollection))]
public sealed class AssetImportWorkbookTests(AssetImportFixture fixture) : AssetWorkbookTestContext(fixture)
{
    [Fact]
    public async Task All_sheets_link_assets_and_replay_without_writes_including_retired_fittings()
    {
        var sheets = Full();
        var result = await Import(sheets.Reverse().ToArray()); // Recognition and FK order do not depend on tab position.
        Committed(result);
        foreach (var kind in new[] { "segments", "cabinets", "feeders", "poles" })
            Assert.Equal(1, result.GetProperty(kind).GetProperty("inserted").GetInt32());
        Assert.Equal(2, result.GetProperty("fixtures").GetProperty("inserted").GetInt32());
        await Fixture.QueryAsync(async db =>
        {
            var p = await db.Set<Pole>().IgnoreQueryFilters().SingleAsync(p => p.CommuneId == Commune);
            var s = await db.Set<RoadSegment>().IgnoreQueryFilters().SingleAsync(p => p.CommuneId == Commune);
            var f = await db.Set<Feeder>().IgnoreQueryFilters().SingleAsync(p => p.CommuneId == Commune);
            var c = await db.Set<ElectricalCabinet>().IgnoreQueryFilters().SingleAsync(p => p.CommuneId == Commune);
            Assert.Equal(s.SegmentId, p.SegmentId); Assert.Equal(f.FeederId, p.FeederId); Assert.Equal(c.CabinetId, f.CabinetId);
            Assert.All(await db.Set<Fixture>().IgnoreQueryFilters().Where(x => x.CommuneId == Commune).ToListAsync(), x => Assert.Equal(p.PoleId, x.PoleId));
            return 0;
        });
        var before = await Snapshot();
        result = await Import(sheets);
        Committed(result);
        foreach (var kind in new[] { "segments", "cabinets", "feeders", "poles", "fixtures" })
        {
            Assert.Equal(0, result.GetProperty(kind).GetProperty("inserted").GetInt32());
            Assert.Equal(0, result.GetProperty(kind).GetProperty("updated").GetInt32());
            Assert.Equal(kind == "fixtures" ? 2 : 1, result.GetProperty(kind).GetProperty("unchanged").GetInt32());
        }
        Assert.Equal(before, await Snapshot());
    }

    [Fact]
    public async Task Last_sheet_failure_rolls_back_inserts_and_updates_and_zeroes_counts()
    {
        Committed(await Import(Full()));
        var before = await Snapshot();
        var sheets = Full();
        sheets[0].Rows[0].Cells["segment_name"] = "Must roll back";
        sheets[3].Rows.Add((9, Row(("external_ref", "NEW"), ("note", "Near bridge"))));
        sheets[4].Rows[1].Cells["lamp_watt"] = "invalid";
        var result = await Import(sheets);
        Rejected(result); Error(result, "Bóng đèn", 3, "lamp_watt", "whole number");
        Assert.Equal(before, await Snapshot());
    }

    [Fact]
    public async Task Committed_templates_load_with_the_commune_chosen_in_the_form()
    {
        var directory = Path.Combine(AssetImportMockSetTests.RepositoryRoot(), "docs", "templates");
        var empty = await ImportBytes(await File.ReadAllBytesAsync(Path.Combine(directory, "assets-workbook.xlsx")));
        Committed(empty); Assert.Empty(await Snapshot());
        var example = await ImportBytes(await File.ReadAllBytesAsync(Path.Combine(directory, "assets-workbook.example.xlsx")));
        Committed(example); Assert.Equal(3, example.GetProperty("poles").GetProperty("inserted").GetInt32());
        Assert.True(await Fixture.QueryAsync(db => db.Set<Pole>().IgnoreQueryFilters().Where(x => x.CommuneId == Commune).AllAsync(x => x.ExternalRef!.StartsWith("CTA-"))));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Missing_sheets_are_skipped_and_empty_or_missing_route_cells_keep_geometry(bool missingColumn)
    {
        Committed(await Import(Full())); var before = await Snapshot();
        var sheets = Full().Take(3).Where(s => s.Name != "Tủ điện").Select(s =>
        {
            s.Rows[0].Cells.Remove("geom_wkt");
            return missingColumn ? s with { Columns = s.Columns.Where(c => c != "geom_wkt").ToArray() } : s;
        }).ToArray();
        var result = await Import(sheets); Committed(result);
        Assert.Equal(1, result.GetProperty("segments").GetProperty("unchanged").GetInt32());
        Assert.Equal(1, result.GetProperty("feeders").GetProperty("unchanged").GetInt32());
        Assert.Equal(0, result.GetProperty("poles").GetProperty("unchanged").GetInt32());
        Assert.Equal(before, await Snapshot());
    }

    [Fact]
    public async Task No_asset_sheet_is_a_file_error_and_IoT_data_is_not_silently_discarded()
    {
        var guide = new AssetWorkbookFile.Sheet("Hướng dẫn", ["Help"], []);
        var result = await Import(guide); Rejected(result); Error(result, null, 1, null, "No asset sheet found");
        var asset = AssetWorkbookFile.Asset(ImportKind.Poles);
        var iot = new AssetWorkbookFile.Sheet("IoT", ["cabinet_external_ref"], []);
        Committed(await Import(guide, asset, iot));
        iot.Rows.Add((7, Row(("cabinet_external_ref", "TU-A"))));
        result = await Import(asset, iot); Rejected(result);
        Error(result, "IoT", 7, null, "IoT import is not supported yet");
        Assert.Empty(await Snapshot());
    }

    [Theory]
    [InlineData("commune_id")]
    [InlineData("data_source")]
    public async Task Form_columns_are_forbidden_even_when_blank(string column)
    {
        var sheet = AssetWorkbookFile.Asset(ImportKind.Poles);
        var result = await Import(sheet with { Columns = [.. sheet.Columns, column] });
        Rejected(result); Error(result, "Cột điện", 1, column, "not supported");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Unknown_columns_are_allowed_only_if_every_cell_is_blank(bool populated)
    {
        var sheet = AssetWorkbookFile.Asset(ImportKind.Poles, Row(("external_ref", "CTA-001"), ("note", "Near school"), ("inventory_number", populated ? "7" : null)));
        var result = await Import(sheet with { Columns = [.. sheet.Columns, "inventory_number"] });
        if (populated) { Rejected(result); Error(result, "Cột điện", 1, "inventory_number", "not supported"); Assert.Empty(await Snapshot()); }
        else Committed(result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("2")]
    [InlineData("calibration-rig")]
    public async Task Data_source_is_required_and_must_be_an_exact_wire_enum(string? source)
    {
        using var response = await Upload(AssetWorkbookFile.Create(AssetWorkbookFile.Asset(ImportKind.Poles)), source);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("data_source", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Upload_defaults_only_a_single_commune_and_rejects_foreign_communes()
    {
        var bytes = AssetWorkbookFile.Create(AssetWorkbookFile.Asset(ImportKind.Poles, Row(("external_ref", "CTA-001"), ("note", "School"))));
        using var single = await Upload(bytes); Assert.Equal(HttpStatusCode.OK, single.StatusCode);
        Assert.Single(await Snapshot());
        using var missing = await Upload(bytes, scope: [Commune, OtherCommune]);
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode); Assert.Contains("commune_id", await missing.Content.ReadAsStringAsync());
        using var foreign = await Upload(bytes, commune: OtherCommune); Assert.Equal(HttpStatusCode.Forbidden, foreign.StatusCode);
        Assert.Contains("COMMUNE_FORBIDDEN", await foreign.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Form_source_reaches_all_existing_planners()
    {
        using var response = await Upload(AssetWorkbookFile.Create(Full()), "calibration_rig", Commune);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var snapshot = await Snapshot(); Assert.Equal(6, snapshot.Length);
        Assert.All(snapshot.Where(s => !s.StartsWith("F/", StringComparison.Ordinal)), s => Assert.EndsWith("CalibrationRig", s));
    }

    [Fact]
    public async Task Only_new_poles_without_geometry_need_a_note()
    {
        var sheet = AssetWorkbookFile.Asset(ImportKind.Poles, Row(("external_ref", "CTA-001")));
        var result = await Import(sheet); Rejected(result); Error(result, "Cột điện", 2, "note", "without geom_wkt needs a note");
        sheet.Rows[0].Cells["geom_wkt"] = "POINT(106.49 10.97)";
        Committed(await Import(sheet));
        sheet.Rows[0].Cells.Remove("geom_wkt"); Committed(await Import(sheet));
        await Fixture.QueryAsync(async db =>
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO pole (commune_id, external_ref, data_source) VALUES ({Commune}, 'OLD-NO-GPS', 'public_imagery')");
            return 0;
        });
        Committed(await Import(AssetWorkbookFile.Asset(ImportKind.Poles, Row(("external_ref", "OLD-NO-GPS")))));
    }

    [Theory]
    [InlineData("fixture_type", "wrong")]
    [InlineData("power_source", "wrong")]
    [InlineData("lamp_watt", "150")]
    [InlineData("lamp_count", "2")]
    [InlineData("removed_date", "2024-01-01")]
    [InlineData("warranty_expiry", "2029-01-01")]
    [InlineData("note", "Changed")]
    public async Task Existing_fitting_changes_are_errors_without_writes(string column, string value)
    {
        Committed(await Import(Full())); var before = await Snapshot();
        var fitting = Fitting("2022-03-24"); fitting[column] = value;
        var result = await Import(AssetWorkbookFile.Asset(ImportKind.Fixtures, fitting));
        Rejected(result); Error(result, "Bóng đèn", 2, column,
            column is "fixture_type" or "power_source" ? "not one of" : "change a fitting through the asset form");
        Assert.Equal(before, await Snapshot());
    }

    [Fact]
    public async Task Blank_optional_fitting_cells_say_nothing_and_blank_count_means_one()
    {
        Committed(await Import(Full())); var before = await Snapshot();
        var fitting = Fitting("2022-03-24"); fitting.Remove("warranty_expiry"); fitting.Remove("note"); fitting.Remove("lamp_count");
        var result = await Import(AssetWorkbookFile.Asset(ImportKind.Fixtures, fitting)); Committed(result);
        Assert.Equal(1, result.GetProperty("fixtures").GetProperty("unchanged").GetInt32()); Assert.Equal(before, await Snapshot());
    }

    [Fact]
    public async Task Different_install_date_cannot_replace_an_active_fitting_and_ambiguous_history_is_rejected()
    {
        Committed(await Import(Full())); var before = await Snapshot();
        var result = await Import(AssetWorkbookFile.Asset(ImportKind.Fixtures, Fitting("2025-01-01")));
        Rejected(result); Error(result, "Bóng đèn", 2, "pole_external_ref", "already has a fitting"); Assert.Equal(before, await Snapshot());
        await Fixture.QueryAsync(async db =>
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO fixture (pole_id, commune_id, fixture_type, power_source, lamp_watt, lamp_count, slot, install_date, removed_date, data_source) SELECT pole_id, commune_id, 'led_road_lamp', 'grid', 100, 1, 1, DATE '2020-01-01', DATE '2021-01-01', 'public_imagery' FROM pole WHERE commune_id = {Commune}");
            return 0;
        });
        before = await Snapshot();
        result = await Import(AssetWorkbookFile.Asset(ImportKind.Fixtures, Fitting("2020-01-01", "2021-01-01")));
        Rejected(result); Error(result, "Bóng đèn", 2, "install_date", "ambiguous"); Assert.Equal(before, await Snapshot());
    }

    [Fact]
    public async Task Duplicate_refs_report_first_row_and_parent_failures_suppress_child_noise_in_load_order()
    {
        var sheets = Full();
        sheets[0].Rows.Add((9, sheets[0].Rows[0].Cells));
        sheets[1].Rows[0].Cells["geom_wkt"] = "invalid";
        sheets[3].Rows.Add((7, Row(("external_ref", "OTHER"), ("geom_wkt", "bad"))));
        var result = await Import(sheets.Reverse().ToArray()); Rejected(result);
        Error(result, "Tuyến đường", 9, "external_ref", "row 2");
        Error(result, "Tủ điện", 2, "geom_wkt", "WKT");
        Error(result, "Cột điện", 7, "geom_wkt", "WKT");
        Assert.DoesNotContain(result.GetProperty("rows").EnumerateArray(), e => e.GetProperty("message").GetString()!.Contains("matches nothing"));
        var names = result.GetProperty("rows").EnumerateArray().Select(e => e.GetProperty("sheet").GetString()).ToArray();
        Assert.True(new[] { "Tuyến đường", "Tủ điện", "Cột điện" }.SequenceEqual(names.Distinct()), result.GetProperty("rows").ToString());
        Assert.Empty(await Snapshot());
    }

    [Fact]
    public async Task Trimmed_NFC_sheet_names_and_sparse_Excel_rows_are_preserved()
    {
        var sheet = AssetWorkbookFile.Asset(ImportKind.Poles);
        sheet = sheet with { Name = ("  " + sheet.Name + "  ").Normalize(NormalizationForm.FormD) };
        sheet.Rows.Add((3, new())); sheet.Rows.Add((19, Row(("external_ref", "CTA-001"))));
        var result = await Import(sheet); Rejected(result); Error(result, "Cột điện", 19, "note", "needs a note");
        result = await Import(sheet with { Name = "cột điện" }); Rejected(result); Error(result, null, 1, null, "No asset sheet");
    }

    [Theory]
    [InlineData("2022-03-24", true)]
    [InlineData("03/24/2022", false)]
    [InlineData(44644, true)]
    [InlineData(44644.5, false)]
    public async Task Dates_accept_ISO_or_integer_Excel_serials_only(object value, bool valid)
    {
        var sheets = Full(); sheets[4].Rows[1].Cells["install_date"] = value;
        var result = await Import(sheets);
        if (valid) Committed(result); else { Rejected(result); Assert.Empty(await Snapshot()); }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task Bounded_reader_rejects_row_column_cell_and_Excel_errors(int problem)
    {
        var sheet = AssetWorkbookFile.Asset(ImportKind.Poles, Row(("external_ref", "CTA-001"), ("note", "School")));
        switch (problem)
        {
            case 0: sheet.Rows.Clear(); sheet.Rows.AddRange(Enumerable.Range(0, 5001).Select(i => ((uint)i + 2, Row(("external_ref", "P" + i), ("note", "School"))))); break;
            case 1: sheet = sheet with { Columns = Enumerable.Range(0, 201).Select(i => "col" + i).ToArray() }; break;
            case 2: sheet.Rows[0].Cells["note"] = new string('x', 2001); break;
            case 3: sheet.Rows[0].Cells["note"] = new AssetWorkbookFile.Cached("#N/A", CellValues.Error); break;
        }
        Rejected(await Import(sheet)); Assert.Empty(await Snapshot());
    }

    [Fact]
    public async Task Note_overwrite_warnings_identify_the_sheet_and_physical_row()
    {
        var sheet = AssetWorkbookFile.Asset(ImportKind.Poles, Row(("external_ref", "CTA-001"), ("note", "Old location")));
        Committed(await Import(sheet));
        sheet.Rows.Clear(); sheet.Rows.Add((11, Row(("external_ref", "CTA-001"), ("note", "New location"))));
        var result = await Import(sheet); Committed(result);
        var warning = Assert.Single(result.GetProperty("warnings").EnumerateArray());
        Assert.Equal("Cột điện", warning.GetProperty("sheet").GetString());
        Assert.Equal(11, warning.GetProperty("row").GetInt32()); Assert.Equal("note", warning.GetProperty("column").GetString());
        Assert.Contains("Old location", warning.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Feeder_routes_are_refused_in_the_workbook_and_a_blank_old_column_keeps_the_stored_route()
    {
        // WB-21: nothing draws feeder.geom, so a route typed into the sheet would be stored and never seen.
        var feeders = Full()[2];
        var withRoute = feeders with { Columns = [.. feeders.Columns, "geom_wkt"] };
        withRoute.Rows[0].Cells["geom_wkt"] = "LINESTRING(106.49 10.97, 106.50 10.98)";
        var result = await Import(Full()[1], withRoute);
        Rejected(result); Error(result, "Mạch điện", 1, "geom_wkt", "not supported");
        Assert.Empty(await Snapshot());

        // A file exported before WB-21 still has the column; left blank it is ignored and the route set through the form stays.
        Committed(await Import(Full()));
        await SetFeederRoute("MACH-A", "LINESTRING(106.49 10.97, 106.50 10.98)");
        var before = await Snapshot();
        withRoute.Rows[0].Cells.Remove("geom_wkt");
        result = await Import(Full()[1], withRoute); Committed(result);
        Assert.Equal(1, result.GetProperty("feeders").GetProperty("unchanged").GetInt32());
        Assert.Equal(before, await Snapshot());
        Assert.Contains(before, line => line.StartsWith("F/", StringComparison.Ordinal) && line.Contains("LINESTRING", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Required_and_duplicate_headers_are_rejected_and_errors_use_sheet_columns()
    {
        var sheet = AssetWorkbookFile.Asset(ImportKind.Cabinets);
        var result = await Import(sheet with { Columns = ["external_ref", "geom_wkt"] });
        Rejected(result); Error(result, "Tủ điện", 1, "cabinet_name", "missing");
        result = await Import(sheet with { Columns = [.. sheet.Columns, "external_ref"] });
        Rejected(result); Error(result, "Tủ điện", 1, "external_ref", "Duplicate");
    }

    [Fact]
    public async Task Shared_strings_numeric_refs_cached_formulas_and_serial_removal_dates_are_values()
    {
        var sheets = Full();
        sheets[3].Rows[0].Cells["external_ref"] = 7;
        foreach (var row in sheets[4].Rows) row.Cells["pole_external_ref"] = 7;
        sheets[3].Rows[0].Cells["note"] = new AssetWorkbookFile.Cached("0", CellValues.SharedString);
        sheets[4].Rows[0].Cells["removed_date"] = new DateTime(2021, 1, 1).ToOADate();
        sheets[4].Rows[1].Cells["lamp_watt"] = new AssetWorkbookFile.Cached("100.0", CellValues.Number, "50+50");
        // An unused long shared string from an ignored guide is outside the recognized sheet's cell limits.
        Committed(await ImportBytes(AssetWorkbookFile.WithSharedStrings(["Near bridge", new string('x', 2001)], sheets)));
        Assert.True(await Fixture.QueryAsync(db => db.Set<Pole>().IgnoreQueryFilters().AnyAsync(p => p.CommuneId == Commune && p.ExternalRef == "7" && p.Note == "Near bridge")));
        sheets[3].Rows[0].Cells["note"] = new AssetWorkbookFile.Cached("1", CellValues.SharedString);
        var before = await Snapshot();
        var result = await ImportBytes(AssetWorkbookFile.WithSharedStrings(["Near bridge", new string('x', 2001)], sheets));
        Rejected(result); Error(result, "Cột điện", 2, "note", "2000 characters"); Assert.Equal(before, await Snapshot());
    }

    [Fact]
    public async Task Invalid_zip_with_the_xlsx_signature_returns_400()
    {
        using var response = await Upload([0x50, 0x4b, 0x03, 0x04, 0, 0, 0, 0]);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Zip_expansion_cap_is_checked_before_OpenXml_parsing()
    {
        using var stream = new MemoryStream();
        stream.Write(AssetWorkbookFile.Create(AssetWorkbookFile.Asset(ImportKind.Poles)));
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Update, true))
        {
            using var target = zip.CreateEntry("large.txt", CompressionLevel.SmallestSize).Open();
            var block = new byte[1024 * 1024];
            for (var i = 0; i < 101; i++) target.Write(block);
        }
        using var response = await Upload(stream.ToArray()); Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("100 MB", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("inventory.xls")]
    [InlineData("inventory.csv")]
    [InlineData("inventory.xlsx")]
    public async Task Unsupported_files_are_415(string name)
    {
        using var response = await Upload(Encoding.UTF8.GetBytes("not a workbook"), name: name);
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
    }
}
