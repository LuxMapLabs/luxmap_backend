using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using DocumentFormat.OpenXml.Spreadsheet;
using LuxMap.Modules.Assets.Entities;
using Microsoft.EntityFrameworkCore;

namespace LuxMap.Api.Tests;

[Collection(nameof(AssetDatabaseCollection))]
public sealed class AssetImportWorkbookTests(AssetImportFixture fixture)
{
    private Dictionary<string, object?> Pole(string tag, int index = 1) => new()
    {
        ["commune_id"] = fixture.CommuneId, ["data_source"] = "public_imagery", ["pole_ref"] = $"{tag}-P{index}",
    };

    private List<(uint Number, Dictionary<string, object?> Cells)> Full(string tag)
    {
        var rows = new List<(uint, Dictionary<string, object?>)>();
        for (var i = 1; i <= 4; i++)
        {
            var row = Pole(tag, i);
            row["segment_ref"] = tag + "-S"; row["segment_name"] = "Route"; row["road_class"] = "inter_village";
            row["cabinet_ref"] = tag + "-C"; row["cabinet_name"] = "Cabinet";
            row["cabinet_lat"] = 10.97; row["cabinet_lng"] = 106.49;
            row["feeder_ref"] = tag + "-F"; row["feeder_name"] = "Feeder";
            if (i != 3) { row["pole_lat"] = 10.97; row["pole_lng"] = 106.49; }
            if (i != 4)
            {
                row["fixture_type"] = "led_road_lamp"; row["power_source"] = "grid";
                row["lamp_watt"] = 100; row["lamp_count"] = i == 2 ? 2 : 1;
                row["install_date"] = "2022-03-24";
            }
            rows.Add(((uint)i + 1, row));
        }
        return rows;
    }

    /// <summary>
    /// The committed templates must stay readable by the endpoint they are handed out for. The empty template is a header row
    /// the parser accepts; the example names COM-070 (outside this run's fixture commune), so its ONLY errors may be commune
    /// scope — any other error means a header, enum, date or coordinate in the example drifted from the code.
    /// </summary>
    [Fact]
    public async Task Committed_templates_parse_and_the_example_fails_only_on_commune_scope()
    {
        var directory = Path.Combine(AssetImportMockSetTests.RepositoryRoot(), "docs", "templates");

        var empty = await Import(await File.ReadAllBytesAsync(Path.Combine(directory, "assets-workbook.xlsx")));
        Assert.True(empty.GetProperty("committed").GetBoolean(), empty.ToString());
        Assert.Equal(0, empty.GetProperty("total_errors").GetInt32());

        var before = await Counts();
        var example = await Import(await File.ReadAllBytesAsync(Path.Combine(directory, "assets-workbook.example.xlsx")));
        Rejected(example);
        var errors = example.GetProperty("rows").EnumerateArray().ToList();
        Assert.NotEmpty(errors);
        Assert.All(errors, e => Assert.Equal("commune_id", e.GetProperty("column").GetString()));
        Assert.Equal(before, await Counts());
    }

    [Fact]
    public async Task Full_sheet_creates_five_kinds_and_links_them_and_keeps_unknown_GPS()
    {
        var tag = Tag();
        var result = await Import(AssetWorkbookFile.Create(Full(tag)));
        Assert.True(result.GetProperty("committed").GetBoolean());
        foreach (var (kind, count) in new[] { ("segments", 1), ("cabinets", 1), ("feeders", 1), ("poles", 4), ("fixtures", 3) })
            Assert.Equal(count, result.GetProperty(kind).GetProperty("inserted").GetInt32());
        var poles = await fixture.QueryAsync(db => db.Set<Pole>().IgnoreQueryFilters()
            .Where(p => p.ExternalRef!.StartsWith(tag)).Include(p => p.Fixtures).ToListAsync());
        Assert.Equal(4, poles.Count);
        var segmentId = Assert.Single(poles.Select(p => p.SegmentId).Distinct());
        var feederId = Assert.Single(poles.Select(p => p.FeederId).Distinct());
        Assert.NotNull(segmentId); Assert.NotNull(feederId);
        Assert.Null(poles.Single(p => p.ExternalRef == tag + "-P3").Geom);
        Assert.Empty(poles.Single(p => p.ExternalRef == tag + "-P4").Fixtures);
        Assert.Equal(2, Assert.Single(poles.Single(p => p.ExternalRef == tag + "-P2").Fixtures).LampCount);
        var cabinetId = await fixture.QueryAsync(db => db.Set<Feeder>().IgnoreQueryFilters()
            .Where(f => f.FeederId == feederId).Select(f => f.CabinetId).SingleAsync());
        Assert.Equal(tag + "-C", await fixture.QueryAsync(db => db.Set<ElectricalCabinet>().IgnoreQueryFilters()
            .Where(c => c.CabinetId == cabinetId).Select(c => c.ExternalRef).SingleAsync()));
    }

    [Fact]
    public async Task Last_row_failure_rolls_back_all_five_tables_and_zeroes_counts()
    {
        var tag = Tag();
        var before = await Counts();
        var rows = Full(tag);
        rows[^1].Cells["fixture_type"] = "wrong"; // A fixture-only error after four kinds have saved.
        var result = await Import(AssetWorkbookFile.Create(rows));
        Rejected(result);
        Assert.Equal(before, await Counts());
        foreach (var kind in new[] { "segments", "cabinets", "feeders", "poles", "fixtures" })
            foreach (var count in result.GetProperty(kind).EnumerateObject()) Assert.Equal(0, count.Value.GetInt32());
    }

    [Fact]
    public async Task Identical_reimport_is_unchanged_and_preserves_stamps()
    {
        var tag = Tag(); var bytes = AssetWorkbookFile.Create(Full(tag));
        Assert.True((await Import(bytes)).GetProperty("committed").GetBoolean());
        var before = await Stamps(tag);
        var second = await Import(bytes);
        Assert.True(second.GetProperty("committed").GetBoolean());
        foreach (var kind in new[] { "segments", "cabinets", "feeders", "poles", "fixtures" })
        {
            Assert.Equal(0, second.GetProperty(kind).GetProperty("inserted").GetInt32());
            Assert.Equal(0, second.GetProperty(kind).GetProperty("updated").GetInt32());
        }
        Assert.Equal(3, second.GetProperty("fixtures").GetProperty("unchanged").GetInt32());
        Assert.Equal(before, await Stamps(tag));
    }

    [Fact]
    public async Task Different_active_fixture_is_refused_without_mutating_it()
    {
        var tag = Tag(); var rows = Full(tag);
        await Import(AssetWorkbookFile.Create(rows));
        var before = await Stamps(tag); var counts = await Counts();
        rows[0].Cells["lamp_watt"] = 150;
        var result = await Import(AssetWorkbookFile.Create(rows));
        Rejected(result); Error(result, 2, "lamp_watt", "replace a fitting through the asset form");
        Assert.Equal(before, await Stamps(tag)); Assert.Equal(counts, await Counts());
    }

    [Fact]
    public async Task Duplicate_parent_conflicts_name_the_defining_row_and_blank_repetitions_are_allowed()
    {
        var tag = Tag(); var rows = Full(tag);
        rows[1].Cells["segment_name"] = "Different";
        var result = await Import(AssetWorkbookFile.Create(rows));
        Rejected(result); Error(result, 3, "segment_name", "row 2");
        rows[1].Cells["segment_name"] = " ";
        Assert.True((await Import(AssetWorkbookFile.Create(rows))).GetProperty("committed").GetBoolean());
    }

    [Fact]
    public async Task Duplicate_poles_fail_on_every_later_occurrence()
    {
        var row = Pole(Tag());
        var result = await Import(AssetWorkbookFile.Create([(2, row), (4, row), (7, row)]));
        Rejected(result); Error(result, 4, "pole_ref", "Duplicate"); Error(result, 7, "pole_ref", "Duplicate");
    }

    [Fact]
    public async Task Sparse_cells_and_blank_rows_preserve_real_Excel_row_numbers()
    {
        var row = Pole(Tag()); row["fixture_note"] = "requires the other fixture fields";
        var result = await Import(AssetWorkbookFile.Create([(2, new()), (7, row)]));
        Rejected(result); Error(result, 7, "install_date", "Required");
        Assert.All(result.GetProperty("rows").EnumerateArray(), e => Assert.Equal(7, e.GetProperty("row").GetInt32()));
    }

    [Fact]
    public async Task Serial_and_ISO_dates_are_accepted_but_times_and_local_dates_fail()
    {
        var tag = Tag(); var rows = Full(tag);
        rows[0].Cells["install_date"] = new DateTime(2022, 3, 24).ToOADate();
        rows[1].Cells["warranty_expiry"] = new DateTime(2024, 3, 24).ToOADate();
        var result = await Import(AssetWorkbookFile.Create(rows));
        Assert.True(result.GetProperty("committed").GetBoolean());
        var dates = await fixture.QueryAsync(db => db.Set<Fixture>().IgnoreQueryFilters()
            .Where(f => f.Pole.ExternalRef == tag + "-P1").Select(f => f.InstallDate).ToListAsync());
        Assert.Equal(new DateOnly(2022, 3, 24), Assert.Single(dates));
        rows[0].Cells["install_date"] = new DateTime(2022, 3, 24, 12, 0, 0).ToOADate();
        rows[1].Cells["install_date"] = "24/03/2022";
        result = await Import(AssetWorkbookFile.Create(rows));
        Rejected(result); Error(result, 2, "install_date", "time"); Error(result, 3, "install_date", "YYYY-MM-DD");
    }

    [Theory]
    [InlineData("10.97", null)]
    [InlineData("10,97", "106.49")]
    [InlineData("NaN", "106.49")]
    public async Task Invalid_coordinate_pairs_are_sheet_cell_errors(string lat, string? lng)
    {
        var row = Pole(Tag()); row["pole_lat"] = lat; row["pole_lng"] = lng;
        var result = await Import(AssetWorkbookFile.Create([(2, row)]));
        Rejected(result); Error(result, 2, "pole_lat", "latitude");
    }

    [Fact]
    public async Task Only_the_first_sheet_is_read()
    {
        var result = await Import(AssetWorkbookFile.Create([(2, Pole(Tag()))], garbageSecondSheet: true));
        Assert.True(result.GetProperty("committed").GetBoolean());
    }

    [Fact]
    public async Task Missing_and_duplicate_headers_are_reported_once_at_row_one()
    {
        var missing = await Import(AssetWorkbookFile.Create([], columns: ["commune_id", "data_source"]));
        Rejected(missing); Assert.Single(missing.GetProperty("rows").EnumerateArray()); Error(missing, 1, "pole_ref", "Column missing");
        var duplicate = await Import(AssetWorkbookFile.Create([], columns: ["commune_id", "data_source", "pole_ref", "pole_ref"]));
        Rejected(duplicate); Error(duplicate, 1, "pole_ref", "Duplicate column");
    }

    [Fact]
    public async Task A_foreign_commune_is_a_row_error_and_does_not_write()
    {
        var row = Pole(Tag()); row["commune_id"] = fixture.ForeignCommuneId;
        var before = await Counts();
        var result = await Import(AssetWorkbookFile.Create([(2, row)]));
        Rejected(result); Error(result, 2, "commune_id", "outside your permitted commune scope");
        Assert.Equal(before, await Counts());
    }

    [Theory]
    [InlineData("x.xls")]
    [InlineData("x.csv")]
    [InlineData("x.xlsx")]
    public async Task Unsupported_extensions_and_disguised_text_are_415(string name)
    {
        var response = await Upload(Encoding.UTF8.GetBytes("not zip"), name);
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        Assert.Contains("Upload a .xlsx workbook.", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Invalid_zip_and_non_OpenXml_zip_are_400()
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
        using (var writer = new StreamWriter(zip.CreateEntry("hello.txt").Open())) await writer.WriteAsync("hello");
        foreach (var bytes in new[] { stream.ToArray(), new byte[] { 0x50, 0x4b, 3, 4, 0, 0 } })
        {
            var response = await Upload(bytes);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains("VALIDATION_FAILED", await response.Content.ReadAsStringAsync());
        }
    }

    [Fact]
    public async Task A_workbook_that_inflates_past_the_cap_is_400_before_parsing()
    {
        // A valid workbook plus one highly compressible entry: a few hundred KB on the wire, > 100 MB unzipped.
        using var stream = new MemoryStream();
        stream.Write(AssetWorkbookFile.Create([(2u, Pole(Tag()))]));
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Update, true))
        using (var entry = zip.CreateEntry("xl/media/padding.bin", CompressionLevel.SmallestSize).Open())
        {
            var block = new byte[1024 * 1024];
            for (var i = 0; i < 101; i++) await entry.WriteAsync(block);
        }

        var response = await Upload(stream.ToArray());
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("expands to more than 100 MB", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task More_than_5000_data_rows_are_rejected_before_writing()
    {
        var tag = Tag(); var before = await Counts();
        var result = await Import(AssetWorkbookFile.Create(Enumerable.Range(1, 5001).Select(i => ((uint)i + 1, Pole(tag, i)))));
        Rejected(result); Assert.Contains("5000", result.GetProperty("rows")[0].GetProperty("message").GetString());
        Assert.Equal(before, await Counts());
    }

    [Fact]
    public async Task Literal_workbook_route_returns_workbook_shape_instead_of_unknown_kind()
    {
        var result = await Import(AssetWorkbookFile.Create([]));
        Assert.True(result.GetProperty("committed").GetBoolean());
        Assert.Equal(0, result.GetProperty("poles").GetProperty("inserted").GetInt32());
    }

    [Fact]
    public async Task Parent_errors_use_sheet_names_and_suppress_cascading_reference_errors()
    {
        var row = Pole(Tag()); row["segment_ref"] = "missing-definition";
        var result = await Import(AssetWorkbookFile.Create([(2, row)]));
        Rejected(result); Error(result, 2, "segment_name", "Required");
        Assert.DoesNotContain(result.GetProperty("rows").EnumerateArray(), e => e.GetProperty("message").GetString()!.Contains("matches nothing"));
        Assert.DoesNotContain(result.GetProperty("rows").EnumerateArray(), e => e.GetProperty("column").GetString() == "external_ref");
        row["segment_ref"] = null; row["pole_ref"] = null;
        result = await Import(AssetWorkbookFile.Create([(2, row)]));
        Error(result, 2, "pole_ref", "Required");
    }

    [Fact]
    public async Task Shared_strings_booleans_and_cached_formulas_are_read_as_values()
    {
        var row = Pole(Tag());
        row["data_source"] = new AssetWorkbookFile.Cached("0", CellValues.SharedString);
        row["near_sensitive_poi"] = true;
        row["fixture_type"] = "led_road_lamp"; row["power_source"] = "grid";
        row["lamp_watt"] = new AssetWorkbookFile.Cached("100.0", CellValues.Number, "50+50");
        row["install_date"] = "2022-03-24";
        Assert.True((await Import(AssetWorkbookFile.Create([(2, row)], sharedStrings: ["public_imagery"]))).GetProperty("committed").GetBoolean());
    }

    [Fact]
    public async Task Excel_error_cells_overlong_cells_and_too_many_columns_are_rejected()
    {
        var row = Pole(Tag()); row["pole_note"] = new AssetWorkbookFile.Cached("#N/A", CellValues.Error);
        var result = await Import(AssetWorkbookFile.Create([(2, row)]));
        Rejected(result); Error(result, 2, "pole_note", "Excel cell error");
        row["pole_note"] = new string('x', 2001);
        result = await Import(AssetWorkbookFile.Create([(2, row)]));
        Rejected(result); Error(result, 2, "pole_note", "2000");
        row["pole_note"] = new AssetWorkbookFile.Cached("0", CellValues.SharedString);
        result = await Import(AssetWorkbookFile.Create([(2, row)], sharedStrings: [new string('x', 2001)]));
        Rejected(result); Error(result, 2, "pole_note", "2000");
        Rejected(await Import(AssetWorkbookFile.Create([], columns: Enumerable.Range(0, 201).Select(i => "col" + i).ToArray())));
    }

    private async Task<int[]> Counts() => await fixture.QueryAsync(async db => new[]
    {
        await db.Set<RoadSegment>().IgnoreQueryFilters().CountAsync(s => s.CommuneId == fixture.CommuneId),
        await db.Set<ElectricalCabinet>().IgnoreQueryFilters().CountAsync(s => s.CommuneId == fixture.CommuneId),
        await db.Set<Feeder>().IgnoreQueryFilters().CountAsync(s => s.CommuneId == fixture.CommuneId),
        await db.Set<Pole>().IgnoreQueryFilters().CountAsync(s => s.CommuneId == fixture.CommuneId),
        await db.Set<Fixture>().IgnoreQueryFilters().CountAsync(s => s.CommuneId == fixture.CommuneId),
    });

    private async Task<string[]> Stamps(string tag) => await fixture.QueryAsync(async db =>
    {
        var poles = await db.Set<Pole>().IgnoreQueryFilters().Where(p => p.ExternalRef!.StartsWith(tag)).Include(p => p.Fixtures).ToListAsync();
        var segments = await db.Set<RoadSegment>().IgnoreQueryFilters().Where(s => s.ExternalRef!.StartsWith(tag)).ToListAsync();
        var cabinets = await db.Set<ElectricalCabinet>().IgnoreQueryFilters().Where(c => c.ExternalRef!.StartsWith(tag)).ToListAsync();
        var feeders = await db.Set<Feeder>().IgnoreQueryFilters().Where(f => f.ExternalRef!.StartsWith(tag)).ToListAsync();
        return poles.OrderBy(p => p.ExternalRef).SelectMany(p => new[] { $"{p.PoleId}/{p.UpdatedBy}/{p.UpdatedAt:O}" }
            .Concat(p.Fixtures.OrderBy(f => f.FixtureId).Select(f => $"{f.FixtureId}/{f.UpdatedBy}/{f.UpdatedAt:O}/{f.LampWatt}")))
            .Concat(segments.OrderBy(s => s.ExternalRef).Select(s => $"{s.SegmentId}/{s.UpdatedBy}/{s.UpdatedAt:O}"))
            .Concat(cabinets.OrderBy(c => c.ExternalRef).Select(c => $"{c.CabinetId}/{c.UpdatedBy}/{c.UpdatedAt:O}"))
            .Concat(feeders.OrderBy(f => f.ExternalRef).Select(f => $"{f.FeederId}/{f.UpdatedBy}/{f.UpdatedAt:O}")).ToArray();
    });

    private async Task<JsonElement> Import(byte[] bytes)
    {
        using var response = await Upload(bytes);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{response.StatusCode}: {text}");
        return JsonDocument.Parse(text).RootElement;
    }
    private async Task<HttpResponseMessage> Upload(byte[] bytes, string name = "inventory.xlsx")
    {
        var client = await fixture.ManagerClientAsync();
        using var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent(bytes), "file", name);
        return await client.PostAsync("/api/v1/assets/import/workbook", content);
    }
    private static void Rejected(JsonElement result) => Assert.False(result.GetProperty("committed").GetBoolean());
    private static void Error(JsonElement result, int row, string column, string message)
        => Assert.Contains(result.GetProperty("rows").EnumerateArray(), e => e.GetProperty("row").GetInt32() == row
            && e.GetProperty("column").GetString() == column && e.GetProperty("message").GetString()!.Contains(message, StringComparison.Ordinal));
    private static string Tag() => "WB" + Guid.NewGuid().ToString("N")[..10];
}
