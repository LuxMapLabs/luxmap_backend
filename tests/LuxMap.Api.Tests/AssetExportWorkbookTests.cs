using System.Globalization;
using System.Net;
using System.Text.Json;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using DocumentFormat.OpenXml.Validation;
using LuxMap.Modules.Assets.Entities;
using LuxMap.Modules.Assets.Export;
using LuxMap.Modules.Assets.Import;
using LuxMap.Modules.Identity.Auth;
using LuxMap.Modules.Identity.Entities;
using LuxMap.Modules.Identity.Seeding;
using LuxMap.Persistence;
using LuxMap.Shared.Contracts.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LuxMap.Api.Tests;

[Collection(nameof(AssetDatabaseCollection))]
public sealed class AssetExportWorkbookTests(AssetImportFixture fixture) : IAsyncLifetime
{
    private const string Path = "/api/v1/assets/export/workbook";
    private string commune = null!;
    private string otherCommune = null!;

    public async Task InitializeAsync()
    {
        (commune, otherCommune) = await fixture.QueryAsync(async db =>
        {
            var a = new AdministrativeUnit { Name = $"export-a-{Guid.NewGuid():N}" };
            var b = new AdministrativeUnit { Name = $"export-b-{Guid.NewGuid():N}" };
            db.AddRange(a, b);
            await db.SaveChangesAsync();
            return (a.CommuneId, b.CommuneId);
        });
    }

    public async Task DisposeAsync()
    {
        await fixture.QueryAsync(async db =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            await db.Database.ExecuteSqlRawAsync("SET LOCAL luxmap.audit_purge = 'on'");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM audit_event WHERE commune_id = {commune} OR commune_id = {otherCommune}");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM fixture WHERE commune_id = {commune} OR commune_id = {otherCommune}");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM pole WHERE commune_id = {commune} OR commune_id = {otherCommune}");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM feeder WHERE commune_id = {commune} OR commune_id = {otherCommune}");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM electrical_cabinet WHERE commune_id = {commune} OR commune_id = {otherCommune}");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM road_segment WHERE commune_id = {commune} OR commune_id = {otherCommune}");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM administrative_unit WHERE commune_id = {commune} OR commune_id = {otherCommune}");
            await transaction.CommitAsync();
            return 0;
        });
    }

    [Fact]
    public async Task Untouched_export_reimports_all_five_kinds_without_writes_or_stamp_changes()
    {
        await Import(AssetWorkbookFile.Create(Full()));
        var before = await Stamps();
        using var client = Client();
        using var response = await client.GetAsync(Path + "?commune_id=" + commune);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await Import(await response.Content.ReadAsByteArrayAsync());
        foreach (var (kind, unchanged) in new[] { ("segments", 1), ("cabinets", 1), ("feeders", 1), ("poles", 4), ("fixtures", 3) })
        {
            Assert.Equal(0, result.GetProperty(kind).GetProperty("inserted").GetInt32());
            Assert.Equal(0, result.GetProperty(kind).GetProperty("updated").GetInt32());
            Assert.Equal(unchanged, result.GetProperty(kind).GetProperty("unchanged").GetInt32());
        }
        Assert.Equal(before, await Stamps());
    }

    /// <summary>
    /// The sheet has no feeder route column, so a re-import must not read that as "no route" and clear a route drawn
    /// elsewhere (WB-5). Before the fix PlanFeedersAsync assigned the always-null workbook geometry to existing feeders.
    /// </summary>
    [Fact]
    public async Task Reimport_keeps_a_drawn_feeder_route_and_counts_the_feeder_unchanged()
    {
        await Import(AssetWorkbookFile.Create(Full()));
        await fixture.QueryAsync(db => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE feeder SET geom = ST_GeomFromText('LINESTRING(106.49 10.97, 106.50 10.98)', 4326) WHERE commune_id = {commune}"));
        var before = await Stamps();

        using var client = Client();
        using var response = await client.GetAsync(Path + "?commune_id=" + commune);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await Import(await response.Content.ReadAsByteArrayAsync());

        Assert.Equal(1, result.GetProperty("feeders").GetProperty("unchanged").GetInt32());
        Assert.Equal(0, result.GetProperty("feeders").GetProperty("updated").GetInt32());
        Assert.Equal(before, await Stamps());
        Assert.True(await fixture.QueryAsync(db => db.Set<Feeder>().IgnoreQueryFilters()
            .Where(f => f.CommuneId == commune).AllAsync(f => f.Geom != null)));
    }

    [Fact]
    public async Task Cells_preserve_types_values_precision_blanks_and_template_dropdowns()
    {
        await Import(AssetWorkbookFile.Create(Full()));
        var bytes = await Export();
        using var stream = new MemoryStream(bytes);
        using var book = SpreadsheetDocument.Open(stream, false);
        Assert.Empty(new OpenXmlValidator().Validate(book));
        var sheet = First(book);
        var rows = Rows(sheet);
        var first = rows.Single(r => r["pole_ref"].InnerText == "007");
        var expected = Full()[0].Cells;
        foreach (var (name, value) in expected)
        {
            if (name is "install_date" or "warranty_expiry")
            {
                var cell = first[name];
                Assert.Equal(CellValues.Number, cell.DataType!.Value);
                Assert.Equal(value, DateTime.FromOADate(double.Parse(cell.InnerText, CultureInfo.InvariantCulture)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                var format = book.WorkbookPart!.WorkbookStylesPart!.Stylesheet!.CellFormats!
                    .Elements<CellFormat>().ElementAt((int)cell.StyleIndex!.Value);
                Assert.Equal("yyyy-mm-dd", book.WorkbookPart!.WorkbookStylesPart!.Stylesheet!.NumberingFormats!
                    .Elements<NumberingFormat>().Single(f => f.NumberFormatId == format.NumberFormatId).FormatCode!.Value);
            }
            else if (value is double d)
            {
                Assert.Equal(CellValues.Number, first[name].DataType!.Value);
                Assert.Equal(d, double.Parse(first[name].InnerText, CultureInfo.InvariantCulture));
            }
            else if (value is int n)
            {
                Assert.Equal(CellValues.Number, first[name].DataType!.Value);
                Assert.Equal(n, int.Parse(first[name].InnerText, CultureInfo.InvariantCulture));
            }
            else
            {
                Assert.Equal(CellValues.InlineString, first[name].DataType!.Value);
                Assert.Equal(value is bool flag ? (flag ? "true" : "false") : value, first[name].InnerText);
                Assert.Null(first[name].CellFormula);
            }
        }
        var stored = await fixture.QueryAsync(db => db.Set<Pole>().IgnoreQueryFilters()
            .Where(p => p.CommuneId == commune && p.ExternalRef == "007").Select(p => p.Geom).SingleAsync());
        Assert.Equal(stored!.Y, double.Parse(first["pole_lat"].InnerText, CultureInfo.InvariantCulture));
        Assert.Equal(stored.X, double.Parse(first["pole_lng"].InnerText, CultureInfo.InvariantCulture));
        var noGps = rows.Single(r => r["pole_ref"].InnerText == "009");
        Assert.False(noGps.ContainsKey("pole_lat")); Assert.False(noGps.ContainsKey("pole_lng"));
        var noFixture = rows.Single(r => r["pole_ref"].InnerText == "010");
        Assert.All(WorkbookColumns.All.Skip(19), c => Assert.False(noFixture.ContainsKey(c.Name)));
        Assert.All(sheet.Descendants<Cell>(), c => Assert.NotEmpty(c.InnerText));
        Assert.Equal("false", noFixture["near_sensitive_poi"].InnerText);
        var pane = Assert.Single(sheet.Descendants<Pane>());
        Assert.Equal(PaneStateValues.Frozen, pane.State!.Value); Assert.Equal(1d, pane.VerticalSplit!.Value);
        using var template = SpreadsheetDocument.Open(Template(), false);
        var templateValidations = First(template).Descendants<DataValidation>().ToDictionary(v => v.SequenceOfReferences!.InnerText!);
        var actualValidations = sheet.Descendants<DataValidation>().ToDictionary(v => v.SequenceOfReferences!.InnerText!);
        Assert.Equal(7, actualValidations.Count);
        foreach (var (range, validation) in templateValidations)
            Assert.Equal(validation.Formula1!.Text, actualValidations[range].Formula1!.Text);
    }

    [Fact]
    public async Task Missing_pole_or_feeder_refs_are_reported_after_the_import_sheet_and_never_detached()
    {
        var rows = Full();
        await Import(AssetWorkbookFile.Create(rows));
        await fixture.QueryAsync(async db =>
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE pole SET external_ref = NULL WHERE commune_id = {commune} AND external_ref = '007'");
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE feeder SET external_ref = NULL WHERE commune_id = {commune}");
            // Only the first two poles are on the now-code-less feeder.
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE pole SET feeder_id = NULL, feeder_source = NULL WHERE commune_id = {commune} AND external_ref IN ('009', '010')");
            return 0;
        });
        var before = await fixture.QueryAsync(db => db.Set<Pole>().IgnoreQueryFilters().Where(p => p.CommuneId == commune)
            .OrderBy(p => p.PoleId).Select(p => new { p.PoleId, p.ExternalRef, p.FeederId }).ToListAsync());
        var bytes = await Export();
        using (var stream = new MemoryStream(bytes))
        using (var book = SpreadsheetDocument.Open(stream, false))
        {
            Assert.Equal(new[] { "import", "Không xuất" }, book.WorkbookPart!.Workbook!.Sheets!.Elements<Sheet>().Select(s => s.Name!.Value));
            // One import = one created_at, so the tiebreak is the pole ID — which EF does not hand out in sheet order.
            var expected = before.Where(p => p.ExternalRef is "009" or "010")
                .OrderBy(p => p.PoleId.Length).ThenBy(p => p.PoleId, StringComparer.Ordinal).Select(p => p.ExternalRef);
            Assert.Equal(expected, Rows(First(book)).Select(r => r["pole_ref"].InnerText));
            var skipped = (WorksheetPart)book.WorkbookPart!.GetPartById(book.WorkbookPart.Workbook!.Sheets!.Elements<Sheet>().Last().Id!);
            var reports = Rows(skipped.Worksheet!);
            Assert.Equal(2, reports.Count);
            foreach (var pole in before.Where(p => p.ExternalRef is null or "008"))
            {
                var report = reports.Single(r => r["pole_id"].InnerText == pole.PoleId);
                Assert.Equal(commune, report["commune_id"].InnerText);
                Assert.Contains("form tài sản", report["reason"].InnerText);
                Assert.Contains("Mạch", report["reason"].InnerText);
                if (pole.ExternalRef is null) Assert.Contains("Cột", report["reason"].InnerText);
            }
        }
        await Import(bytes);
        Assert.Equal(before, await fixture.QueryAsync(db => db.Set<Pole>().IgnoreQueryFilters().Where(p => p.CommuneId == commune)
            .OrderBy(p => p.PoleId).Select(p => new { p.PoleId, p.ExternalRef, p.FeederId }).ToListAsync()));
    }

    [Fact]
    public async Task Code_less_segments_and_cabinets_leave_blank_groups_and_keep_links_on_reimport()
    {
        await Import(AssetWorkbookFile.Create(Full()));
        await fixture.QueryAsync(async db =>
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE road_segment SET external_ref = NULL WHERE commune_id = {commune}");
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE electrical_cabinet SET external_ref = NULL WHERE commune_id = {commune}");
            return 0;
        });
        var before = await Stamps();
        var bytes = await Export();
        using (var stream = new MemoryStream(bytes))
        using (var book = SpreadsheetDocument.Open(stream, false))
        {
            Assert.Single(book.WorkbookPart!.Workbook!.Sheets!.Elements<Sheet>());
            Assert.All(Rows(First(book)), row =>
            {
                Assert.All(WorkbookColumns.All.Skip(2).Take(8), c => Assert.False(row.ContainsKey(c.Name)));
                Assert.Equal("002", row["feeder_ref"].InnerText);
                Assert.Equal("verified", row["cabinet_source"].InnerText);
            });
        }
        var result = await Import(bytes);
        foreach (var kind in new[] { "segments", "cabinets", "feeders", "poles", "fixtures" })
        {
            Assert.Equal(0, result.GetProperty(kind).GetProperty("inserted").GetInt32());
            Assert.Equal(0, result.GetProperty(kind).GetProperty("updated").GetInt32());
        }
        Assert.Equal(before, await Stamps());
    }

    [Fact]
    public async Task Retired_fittings_are_omitted_even_when_the_pole_has_no_active_fitting()
    {
        await Import(AssetWorkbookFile.Create(Full()));
        await fixture.QueryAsync(async db =>
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE fixture SET removed_date = DATE '2025-01-01' WHERE commune_id = {commune} AND pole_id IN (SELECT pole_id FROM pole WHERE commune_id = {commune} AND external_ref = '007')");
            return 0;
        });
        // Installing again leaves both a retired and a current fitting on this pole.
        var replacement = Full()[0].Cells; replacement["lamp_watt"] = 150;
        await Import(AssetWorkbookFile.Create([(2, replacement)]));
        await fixture.QueryAsync(async db =>
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE fixture SET removed_date = DATE '2025-01-01' WHERE commune_id = {commune} AND pole_id IN (SELECT pole_id FROM pole WHERE commune_id = {commune} AND external_ref = '008')");
            return 0;
        });
        using var stream = new MemoryStream(await Export());
        using var book = SpreadsheetDocument.Open(stream, false);
        var rows = Rows(First(book));
        Assert.Equal("150", rows.Single(r => r["pole_ref"].InnerText == "007")["lamp_watt"].InnerText);
        var retiredOnly = rows.Single(r => r["pole_ref"].InnerText == "008");
        Assert.All(WorkbookColumns.All.Skip(19), c => Assert.False(retiredOnly.ContainsKey(c.Name)));
        Assert.Single(book.WorkbookPart!.Workbook!.Sheets!.Elements<Sheet>());
    }

    [Fact]
    public async Task Rows_order_by_commune_segment_then_creation_and_numeric_id_including_the_width_boundary()
    {
        // Prefer POLE-9999/10000. PrefixedIdOverflowTests retains them until collection teardown;
        // in that case choose the next free digit boundary without touching the shared sequence.
        long boundary = 10_000;
        string low, high;
        while (true)
        {
            low = "POLE-" + (boundary - 1).ToString(CultureInfo.InvariantCulture);
            high = "POLE-" + boundary.ToString(CultureInfo.InvariantCulture);
            if (!await fixture.QueryAsync(db => db.Set<Pole>().IgnoreQueryFilters()
                    .AnyAsync(p => p.PoleId == low || p.PoleId == high))) break;
            boundary *= 10;
        }
        var rows = Full();
        rows[0].Cells["segment_ref"] = "Z";
        rows[1].Cells["segment_ref"] = "A";
        rows[2].Cells["segment_ref"] = null;
        rows[3].Cells["segment_ref"] = "A";
        rows[3].Cells["commune_id"] = otherCommune;
        await Import(AssetWorkbookFile.Create(rows), [commune, otherCommune]);
        await fixture.QueryAsync(async db =>
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO pole (pole_id, commune_id, external_ref, data_source)
                VALUES ({high}, {commune}, 'boundary-high', 'public_imagery'),
                       ({low}, {commune}, 'boundary-low', 'public_imagery')
                """);
            return 0;
        });
        var times = await fixture.QueryAsync(db => db.Set<Pole>().IgnoreQueryFilters()
            .Where(p => p.PoleId == low || p.PoleId == high).Select(p => p.CreatedAt).Distinct().ToListAsync());
        Assert.Single(times);
        using var client = Client(communes: [commune, otherCommune]);
        using var response = await client.GetAsync(Path + $"?commune_id={commune}&commune_id={otherCommune}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var stream = new MemoryStream(await response.Content.ReadAsByteArrayAsync());
        using var book = SpreadsheetDocument.Open(stream, false);
        var actual = Rows(First(book));
        var home = new[] { "008", "007", "009", "boundary-low", "boundary-high" };
        var expected = new[] { commune, otherCommune }.Order(StringComparer.Ordinal)
            .SelectMany(c => c == commune ? home : new[] { "010" });
        Assert.Equal(expected, actual.Select(r => r["pole_ref"].InnerText));
        Assert.Equal(actual.Select(r => r["commune_id"].InnerText).Order(StringComparer.Ordinal), actual.Select(r => r["commune_id"].InnerText));
    }

    [Fact]
    public async Task Authentication_all_four_roles_scope_and_repeated_commune_filters_are_enforced()
    {
        using var anonymous = fixture.CreateClient();
        using var unauthorized = await anonymous.GetAsync(Path);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
        using var manager = Client();
        using var forbidden = await manager.GetAsync(Path + $"?commune_id={commune}&commune_id={otherCommune}");
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.Contains("COMMUNE_FORBIDDEN", await forbidden.Content.ReadAsStringAsync());
        var foreignRow = Full()[0].Cells; foreignRow["commune_id"] = otherCommune;
        await Import(AssetWorkbookFile.Create([(2, foreignRow)]), [otherCommune]);
        foreach (var role in new[] { UserRole.Superior, UserRole.Manager, UserRole.FieldEngineer, UserRole.SystemAdmin })
        {
            using var client = Client(role);
            using var response = await client.GetAsync(Path + "?commune_id=" + commune);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var stream = new MemoryStream(await response.Content.ReadAsByteArrayAsync());
            using var book = SpreadsheetDocument.Open(stream, false);
            Assert.Empty(Rows(First(book))); // Scoped empty export still contains its header.
        }
        using var implicitScope = await manager.GetAsync(Path);
        using var implicitStream = new MemoryStream(await implicitScope.Content.ReadAsByteArrayAsync());
        using var implicitBook = SpreadsheetDocument.Open(implicitStream, false);
        Assert.Empty(Rows(First(implicitBook))); // No filter must not leak the other commune.
    }

    [Fact]
    public async Task More_than_5000_scoped_poles_returns_413_before_excluding_code_less_poles()
    {
        await fixture.QueryAsync(async db =>
        {
            var start = Random.Shared.NextInt64(1_000_000_000, 9_000_000_000);
            var ids = Enumerable.Range(1, 5001).Select(i => "POLE-" + (start + i).ToString(CultureInfo.InvariantCulture)).ToArray();
            Assert.False(await db.Set<Pole>().IgnoreQueryFilters().AnyAsync(p => ids.Contains(p.PoleId)));
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO pole (pole_id, commune_id, data_source)
                SELECT 'POLE-' || ({start} + i)::text, {commune}, 'public_imagery' FROM generate_series(1, 5001) AS i
                """);
            return 0;
        });
        using var client = Client();
        using var response = await client.GetAsync(Path);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync();
        Assert.Contains("EXPORT_TOO_LARGE", text); Assert.Contains("commune_id", text);
        // The same inventory outside the caller's narrowed scope does not count against that export.
        using var other = Client(communes: [otherCommune]);
        using var empty = await other.GetAsync(Path + "?commune_id=" + otherCommune);
        Assert.Equal(HttpStatusCode.OK, empty.StatusCode);
    }

    [Fact]
    public async Task Headers_match_the_template_and_shared_columns_and_attachment_uses_the_UTC_day()
    {
        using var client = Client();
        var before = DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        using var response = await client.GetAsync(Path);
        var after = DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(AssetExportWorkbook.ContentType, response.Content.Headers.ContentType!.MediaType);
        var disposition = response.Content.Headers.ContentDisposition!;
        Assert.Equal("attachment", disposition.DispositionType);
        Assert.Contains(disposition.FileNameStar ?? disposition.FileName!.Trim('"'), new[] { $"assets-{before}.xlsx", $"assets-{after}.xlsx" });
        using var stream = new MemoryStream(await response.Content.ReadAsByteArrayAsync());
        using var book = SpreadsheetDocument.Open(stream, false);
        using var template = SpreadsheetDocument.Open(Template(), false);
        var header = Headers(First(book));
        Assert.Equal(26, header.Length);
        Assert.Equal(WorkbookColumns.All.Select(c => c.Name), header);
        Assert.Equal(Headers(First(template)), header);
        Assert.Single(First(book).Descendants<Row>());
        Assert.Single(book.WorkbookPart!.Workbook!.Sheets!.Elements<Sheet>());
    }

    private List<(uint Number, Dictionary<string, object?> Cells)> Full()
    {
        var rows = new List<(uint, Dictionary<string, object?>)>();
        for (var i = 0; i < 4; i++)
        {
            var row = new Dictionary<string, object?>
            {
                ["commune_id"] = commune, ["data_source"] = "public_imagery",
                ["segment_ref"] = "S", ["segment_name"] = "Đường, làng", ["road_class"] = "inter_village", ["segment_length_m"] = 123,
                ["cabinet_ref"] = "001", ["cabinet_name"] = "Tủ điện", ["cabinet_lat"] = 10.971234567890123, ["cabinet_lng"] = 106.49123456789012,
                ["feeder_ref"] = "002", ["feeder_name"] = "Mạch 1", ["cabinet_source"] = "verified",
                ["pole_ref"] = (i + 7).ToString("D3", CultureInfo.InvariantCulture), ["near_sensitive_poi"] = i == 0,
                ["pole_note"] = "Gần trường, chú ý cây xanh\nTiếng Việt", ["feeder_source"] = "inferred",
            };
            if (i != 2) { row["pole_lat"] = 10.971234567890125; row["pole_lng"] = 106.49123456789013; }
            if (i != 3)
            {
                row["fixture_type"] = "led_road_lamp"; row["power_source"] = "grid";
                row["lamp_watt"] = 100; row["lamp_count"] = 2;
                row["install_date"] = "2022-03-24"; row["warranty_expiry"] = "2024-03-24";
                row["fixture_note"] = "=Bộ đôi, tiếng Việt";
            }
            rows.Add(((uint)i + 2, row));
        }
        return rows;
    }

    // Signed legacy claims keep tests isolated without changing the shared fixture account's role or commune.
    private HttpClient Client(UserRole role = UserRole.Manager, string[]? communes = null)
    {
        var user = new AppUser
        {
            UserId = fixture.ManagerUserId, Username = fixture.ManagerUsername, Email = $"{fixture.ManagerUsername}@luxmap.local",
            FullName = "Workbook export test", Role = role, PasswordAlgorithm = IdentitySeeder.PasswordAlgorithm, HasSystemWideScope = role == UserRole.SystemAdmin,
        };
        var token = fixture.Services.GetRequiredService<AccessTokenIssuer>().Issue(user,
            role == UserRole.SystemAdmin ? ["*"] : communes ?? [commune]).Token;
        var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);
        return client;
    }

    private async Task<byte[]> Export()
    {
        using var client = Client();
        using var response = await client.GetAsync(Path + "?commune_id=" + commune);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsByteArrayAsync();
    }

    private async Task<JsonElement> Import(byte[] bytes, string[]? communes = null)
    {
        using var client = Client(communes: communes);
        using var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent(bytes), "file", "inventory.xlsx");
        using var response = await client.PostAsync("/api/v1/assets/import/workbook", content);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, text);
        using var json = JsonDocument.Parse(text);
        var result = json.RootElement.Clone();
        Assert.True(result.GetProperty("committed").GetBoolean(), text);
        return result;
    }

    private async Task<string[]> Stamps() => await fixture.QueryAsync(async db =>
    {
        var p = await db.Set<Pole>().IgnoreQueryFilters().Where(p => p.CommuneId == commune).OrderBy(p => p.PoleId).ToListAsync();
        var s = await db.Set<RoadSegment>().IgnoreQueryFilters().Where(p => p.CommuneId == commune).OrderBy(p => p.SegmentId).ToListAsync();
        var c = await db.Set<ElectricalCabinet>().IgnoreQueryFilters().Where(p => p.CommuneId == commune).OrderBy(p => p.CabinetId).ToListAsync();
        var f = await db.Set<Feeder>().IgnoreQueryFilters().Where(p => p.CommuneId == commune).OrderBy(p => p.FeederId).ToListAsync();
        var l = await db.Set<Fixture>().IgnoreQueryFilters().Where(p => p.CommuneId == commune).OrderBy(p => p.FixtureId).ToListAsync();
        return p.Select(x => $"{x.PoleId}/{x.UpdatedAt:O}/{x.UpdatedBy}")
            .Concat(s.Select(x => $"{x.SegmentId}/{x.UpdatedAt:O}/{x.UpdatedBy}"))
            .Concat(c.Select(x => $"{x.CabinetId}/{x.UpdatedAt:O}/{x.UpdatedBy}"))
            .Concat(f.Select(x => $"{x.FeederId}/{x.UpdatedAt:O}/{x.UpdatedBy}"))
            .Concat(l.Select(x => $"{x.FixtureId}/{x.UpdatedAt:O}/{x.UpdatedBy}")).ToArray();
    });

    private static Worksheet First(SpreadsheetDocument book) => ((WorksheetPart)book.WorkbookPart!
        .GetPartById(book.WorkbookPart.Workbook!.Sheets!.Elements<Sheet>().First().Id!)).Worksheet!;
    private static string[] Headers(Worksheet sheet) => sheet.Descendants<Row>().First().Elements<Cell>().Select(c => c.InnerText).ToArray();
    private static List<Dictionary<string, Cell>> Rows(Worksheet sheet)
    {
        var header = Headers(sheet);
        return sheet.Descendants<Row>().Skip(1).Select(row => row.Elements<Cell>()
            .ToDictionary(c => header[c.CellReference!.Value![0] - 'A'], c => c)).ToList();
    }
    private static string Template() => System.IO.Path.Combine(AssetImportMockSetTests.RepositoryRoot(), "docs", "templates", "assets-workbook.xlsx");
}
