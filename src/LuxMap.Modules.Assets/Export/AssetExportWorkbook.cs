using System.Globalization;
using System.Net;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using LuxMap.Modules.Assets.Entities;
using LuxMap.Modules.Assets.Import;
using LuxMap.Persistence;
using LuxMap.Shared.Authorization;
using LuxMap.Shared.Contracts.Enums;
using LuxMap.Shared.Contracts.Errors;
using LuxMap.Shared.Http;
using Microsoft.EntityFrameworkCore;

namespace LuxMap.Modules.Assets.Export;

/// <summary>WB-19: complete scoped inventory in named sheets using the CSV vocabulary.</summary>
public sealed class AssetExportWorkbook(LuxMapDbContext db)
{
    public const string ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public async Task<byte[]> WriteAsync(IReadOnlyList<string>? communes, CancellationToken ct)
    {
        async Task<List<T>> Load<T>(string id) where T : class, ICommuneScoped
        {
            var query = db.Set<T>().AsNoTracking();
            if (communes is not null) query = query.Where(x => communes.Contains(x.CommuneId));
            var rows = await query.OrderBy(x => x.CommuneId).ThenBy(x => EF.Property<DateTime>(x, "CreatedAt"))
                .ThenBy(x => EF.Property<string>(x, id).Length).ThenBy(x => EF.Property<string>(x, id))
                .Take(WorkbookDocument.MaxRows + 1).ToListAsync(ct);
            if (rows.Count > WorkbookDocument.MaxRows)
                throw new LuxMapException(ErrorCodes.ExportTooLarge, HttpStatusCode.RequestEntityTooLarge,
                    $"At most {WorkbookDocument.MaxRows} assets per kind can be exported. Filter by commune_id.",
                    new Dictionary<string, object?> { ["kind"] = typeof(T).Name, ["max_rows"] = WorkbookDocument.MaxRows });
            return rows;
        }
        var segments = await Load<RoadSegment>("SegmentId");
        var cabinets = await Load<ElectricalCabinet>("CabinetId");
        var feeders = await Load<Feeder>("FeederId");
        var poles = await Load<Pole>("PoleId");
        var fixtures = await Load<Fixture>("FixtureId");
        // Parent lookups stay scoped. A missing parent code is never obtained through a filter bypass.
        var segmentIds = poles.Where(p => p.SegmentId != null).Select(p => p.SegmentId!).ToArray();
        var segmentRefs = await db.Set<RoadSegment>().AsNoTracking().Where(s => segmentIds.Contains(s.SegmentId))
            .ToDictionaryAsync(s => s.SegmentId, s => s.ExternalRef, ct);
        var cabinetIds = feeders.Where(f => f.CabinetId != null).Select(f => f.CabinetId!).ToArray();
        var cabinetRefs = await db.Set<ElectricalCabinet>().AsNoTracking().Where(c => cabinetIds.Contains(c.CabinetId))
            .ToDictionaryAsync(c => c.CabinetId, c => c.ExternalRef, ct);
        var feederIds = poles.Where(p => p.FeederId != null).Select(p => p.FeederId!).ToArray();
        var feederRefs = await db.Set<Feeder>().AsNoTracking().Where(f => feederIds.Contains(f.FeederId))
            .ToDictionaryAsync(f => f.FeederId, f => f.ExternalRef, ct);
        var poleIds = fixtures.Select(f => f.PoleId).ToArray();
        var poleRefs = await db.Set<Pole>().AsNoTracking().Where(p => poleIds.Contains(p.PoleId))
            .ToDictionaryAsync(p => p.PoleId, p => p.ExternalRef, ct);
        var excluded = new List<object?[]>();
        bool Include(string kind, string id, string commune, string? reference, string? reason = null)
        {
            if (reference is not null && reason is null) return true;
            excluded.Add([kind, id, commune, reason ?? "Thiếu external_ref; gán mã kiểm kê qua form tài sản."]);
            return false;
        }
        var rows = new Dictionary<ImportKind, List<object?[]>>
        {
            [ImportKind.Segments] = segments.Where(x => Include("segments", x.SegmentId, x.CommuneId, x.ExternalRef))
                .Select(x => new object?[] { x.ExternalRef, x.SegmentName, WireEnum.Name(x.RoadClass), x.LengthM, x.Geom?.AsText() }).ToList(),
            [ImportKind.Cabinets] = cabinets.Where(x => Include("cabinets", x.CabinetId, x.CommuneId, x.ExternalRef))
                .Select(x => new object?[] { x.ExternalRef, x.CabinetName, x.Geom.AsText() }).ToList(),
            [ImportKind.Feeders] = feeders.Where(x => Include("feeders", x.FeederId, x.CommuneId, x.ExternalRef))
                .Select(x => new object?[] { x.ExternalRef, x.FeederName,
                    x.CabinetId is null ? null : cabinetRefs.GetValueOrDefault(x.CabinetId),
                    x.CabinetSource is { } cs ? WireEnum.Name(cs) : null }).ToList(),
            [ImportKind.Poles] = poles.Where(x => Include("poles", x.PoleId, x.CommuneId, x.ExternalRef,
                    x.FeederId is not null && feederRefs.GetValueOrDefault(x.FeederId) is null ? "Mạch của cột thiếu mã kiểm kê hoặc ngoài phạm vi." : null))
                .Select(x => new object?[] { x.ExternalRef,
                    x.SegmentId is null ? null : segmentRefs.GetValueOrDefault(x.SegmentId),
                    x.FeederId is null ? null : feederRefs.GetValueOrDefault(x.FeederId),
                    x.FeederSource is { } fs ? WireEnum.Name(fs) : null, x.Geom?.AsText(),
                    x.NearSensitivePoi ? "true" : "false", x.Note }).ToList(),
            [ImportKind.Fixtures] = fixtures.Where(x => Include("fixtures", x.FixtureId, x.CommuneId,
                    poleRefs.GetValueOrDefault(x.PoleId), poleRefs.GetValueOrDefault(x.PoleId) is null ? "Cột của bộ đèn thiếu mã kiểm kê hoặc ngoài phạm vi." : null))
                .Select(x => new object?[] { poleRefs[x.PoleId], WireEnum.Name(x.FixtureType), WireEnum.Name(x.PowerSource),
                    x.LampWatt, x.InstallDate, x.LampCount, x.RemovedDate, x.WarrantyExpiry, x.Note }).ToList(),
        };
        using var stream = new MemoryStream();
        using (var package = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook, true))
        {
            var book = package.AddWorkbookPart();
            book.Workbook = new Workbook(new BookViews(new WorkbookView()), new Sheets());
            var styles = book.AddNewPart<WorkbookStylesPart>();
            styles.Stylesheet = new Stylesheet(
                new NumberingFormats(new NumberingFormat { NumberFormatId = 164, FormatCode = "yyyy-mm-dd" }) { Count = 1 },
                new Fonts(new Font(), new Font(new Bold(), new Color { Rgb = "FFFFFFFF" })) { Count = 2 },
                new Fills(new Fill(new PatternFill { PatternType = PatternValues.None }),
                    new Fill(new PatternFill { PatternType = PatternValues.Gray125 }),
                    new Fill(new PatternFill(new ForegroundColor { Rgb = "FF1E3A5F" }) { PatternType = PatternValues.Solid }),
                    new Fill(new PatternFill(new ForegroundColor { Rgb = "FF64748B" }) { PatternType = PatternValues.Solid })) { Count = 4 },
                new Borders(new Border()) { Count = 1 }, new CellStyleFormats(new CellFormat()) { Count = 1 },
                new CellFormats(new CellFormat(), new CellFormat { NumberFormatId = 164, ApplyNumberFormat = true },
                    new CellFormat { NumberFormatId = 49, ApplyNumberFormat = true },
                    new CellFormat { FontId = 1, FillId = 2, ApplyFill = true, ApplyFont = true },
                    new CellFormat { FontId = 1, FillId = 3, ApplyFill = true, ApplyFont = true },
                    new CellFormat(new Alignment { WrapText = true, Vertical = VerticalAlignmentValues.Top }) { ApplyAlignment = true }) { Count = 6 });
            styles.Stylesheet.Save();
            void Sheet(string name, IEnumerable<object?[]> content, WorkbookSheets.Layout? layout = null)
            {
                var part = book.AddNewPart<WorksheetPart>();
                using (var writer = OpenXmlWriter.Create(part))
                {
                    writer.WriteStartElement(new Worksheet());
                    writer.WriteElement(new SheetViews(new SheetView(new Pane { VerticalSplit = 1, TopLeftCell = "A2", State = PaneStateValues.Frozen }) { WorkbookViewId = 0 }));
                    if (layout is not null)
                    {
                        writer.WriteStartElement(new Columns());
                        for (var i = 0; i < layout.Columns.Length; i++)
                            writer.WriteElement(new Column { Min = (uint)i + 1, Max = (uint)i + 1,
                                Width = layout.Columns[i] is "geom_wkt" or "note" ? 60 : 25, CustomWidth = true,
                                Style = layout.Columns[i].Contains("ref", StringComparison.Ordinal) ? 2U : 0U });
                        writer.WriteEndElement();
                    }
                    if (name == "Hướng dẫn")
                        writer.WriteElement(new Columns(new Column { Min = 1, Max = 1, Width = 115, CustomWidth = true },
                            new Column { Min = 2, Max = 5, Width = 20, CustomWidth = true }));
                    writer.WriteStartElement(new SheetData());
                    uint number = 1;
                    foreach (var row in content)
                    {
                        ct.ThrowIfCancellationRequested();
                        WriteRow(writer, number, row, number == 1 ? layout : null, name == "Hướng dẫn" && row.Length == 1);
                        number++;
                    }
                    writer.WriteEndElement();
                    if (layout is not null)
                    {
                        var validations = Validations(layout);
                        if (validations.HasChildren) writer.WriteElement(validations);
                    }
                    writer.WriteEndElement();
                }
                book.Workbook.Sheets!.Append(new Sheet { Name = name,
                    SheetId = (uint)book.Workbook.Sheets.ChildElements.Count + 1, Id = book.GetIdOfPart(part) });
            }
            List<object?[]> guide = [ ["LUXMAP · MẪU NẠP TÀI SẢN CHIẾU SÁNG"],
                ["Nạp cả file một lần; chọn xã và data_source khi upload. Có lỗi: không ghi gì."],
                ["external_ref là mã kiểm kê của xã (CTA-001, TUYEN-A), không dùng mã hệ thống POLE-0001 / SEG-001."],
                ["Cột mới không GPS phải có note mô tả vị trí. Bộ đèn khớp theo cột và ngày lắp; sửa qua form."],
                ["Mỗi sheet tối đa 5000 dòng, 200 cột, 2000 ký tự/ô; file 10 MB, giải nén 100 MB."],
                ["geom_wkt: POINT(lng lat) / LINESTRING(lng lat, …). Tuyến trống: giữ hình; xoá qua form. Mạch điện không có geom_wkt."],
                ["File nhiều xã không nạp lại được; upload chỉ nhận một xã."],
                ["Nạp lại dùng một data_source; tài sản khác nguồn sẽ theo nguồn chọn khi upload (bộ đèn có sẵn không sửa)."],
                ["Nguồn: field / public_imagery / calibration_rig / simulated. Testbed không bao giờ chọn field."],
                ["Dòng 1 là header; giữ nguyên tên cột. Không đặt commune_id hoặc data_source trong sheet."],
                ["Ngày YYYY-MM-DD hoặc ngày Excel nguyên. Mã cha lấy từ dropdown; mỗi tài sản một dòng."],
                ["Bóng đèn nhận cả bộ đã tháo (removed_date). Chỉ một bộ đang dùng trên mỗi cột."],
                ["data_source", "segments", "cabinets", "poles", "fixtures"] ];
            foreach (var source in Enum.GetValues<DataSource>())
                guide.Add([WireEnum.Name(source), segments.Count(x => x.DataSource == source), cabinets.Count(x => x.DataSource == source),
                    poles.Count(x => x.DataSource == source), fixtures.Count(x => x.DataSource == source)]);
            Sheet("Hướng dẫn", guide);
            foreach (var layout in WorkbookSheets.All)
                Sheet(layout.Name, new[] { layout.Columns.Cast<object?>().ToArray() }.Concat(rows[layout.Kind]), layout);
            if (excluded.Count > 0) Sheet("Không xuất", new[] { new object?[] { "kind", "id", "commune_id", "reason" } }.Concat(excluded));
            var names = new DefinedNames();
            foreach (var (name, sheet) in new[] { ("ds_tuyen_duong", "Tuyến đường"), ("ds_tu_dien", "Tủ điện"), ("ds_mach_dien", "Mạch điện"), ("ds_cot_dien", "Cột điện") })
                names.Append(new DefinedName($"'{sheet}'!$A$2:$A$5001") { Name = name });
            book.Workbook.Append(names);
            book.Workbook.Save();
        }
        return stream.ToArray();
    }

    private static void WriteRow(OpenXmlWriter writer, uint number, IEnumerable<object?> values, WorkbookSheets.Layout? layout = null, bool wrap = false)
    {
        writer.WriteStartElement(new Row { RowIndex = number, Height = wrap ? 36 : null, CustomHeight = wrap });
        var index = 0;
        foreach (var value in values)
        {
            var reference = $"{(char)('A' + index++)}{number}";
            if (value is null or "") continue;
            var cell = new Cell { CellReference = reference, StyleIndex = number == 1 ? 3U : 0U };
            if (layout is not null && !layout.Required.Contains(layout.Columns[index - 1])) cell.StyleIndex = 4;
            if (value is DateOnly day)
            {
                cell.DataType = CellValues.Number;
                cell.StyleIndex = 1;
                cell.CellValue = new CellValue(day.ToDateTime(TimeOnly.MinValue).ToOADate().ToString("R", CultureInfo.InvariantCulture));
            }
            else if (value is double or int)
            {
                cell.DataType = CellValues.Number;
                cell.CellValue = new CellValue(value is double d ? d.ToString("R", CultureInfo.InvariantCulture)
                    : ((int)value).ToString(CultureInfo.InvariantCulture));
            }
            else
            {
                if (number != 1) cell.StyleIndex = wrap ? 5U : 2U;
                cell.DataType = CellValues.InlineString;
                cell.InlineString = new InlineString(new Text((string)value) { Space = SpaceProcessingModeValues.Preserve });
            }
            writer.WriteElement(cell);
        }
        writer.WriteEndElement();
    }

    private static DataValidations Validations(WorkbookSheets.Layout layout)
    {
        var result = new DataValidations();
        for (var i = 0; i < layout.Columns.Length; i++)
        {
            var formula = layout.Columns[i] switch
            {
                "road_class" => "\"inter_commune,inter_village\"", "fixture_type" => "\"led_road_lamp\"", "power_source" => "\"grid\"",
                "cabinet_source" or "feeder_source" => "\"verified,inferred\"", "near_sensitive_poi" => "\"true,false\"", "lamp_count" => "\"1,2,3,4\"",
                "segment_external_ref" => "ds_tuyen_duong", "cabinet_external_ref" => "ds_tu_dien",
                "feeder_external_ref" => "ds_mach_dien", "pole_external_ref" => "ds_cot_dien", _ => null,
            };
            if (formula is null) continue;
            var letter = (char)('A' + i);
            result.Append(new DataValidation(new Formula1(formula)) { Type = DataValidationValues.List, AllowBlank = true,
                ShowErrorMessage = true, SequenceOfReferences = new ListValue<StringValue> { InnerText = $"{letter}2:{letter}5001" } });
        }
        result.Count = (uint)result.ChildElements.Count;
        return result;
    }
}
