using System.Globalization;
using System.Net;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using LuxMap.Modules.Assets.Entities;
using LuxMap.Modules.Assets.Import;
using LuxMap.Persistence;
using LuxMap.Shared.Contracts.Enums;
using LuxMap.Shared.Contracts.Errors;
using LuxMap.Shared.Http;
using Microsoft.EntityFrameworkCore;

namespace LuxMap.Modules.Assets.Export;

/// <summary>WB-7…WB-11 (SELF-SIGNED): a current inventory in the first-sheet import vocabulary.</summary>
public sealed class AssetExportWorkbook(LuxMapDbContext db)
{
    public const string ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public async Task<byte[]> WriteAsync(IReadOnlyList<string>? communes, CancellationToken ct)
    {
        var query = db.Set<Pole>().AsNoTracking();
        if (communes is not null) query = query.Where(p => communes.Contains(p.CommuneId));
        EnsureWithinLimit(await query.CountAsync(ct));
        var poles = await query.Include(p => p.Segment).Include(p => p.Feeder)
            .Include(p => p.Fixtures.Where(f => f.RemovedDate == null))
            .OrderBy(p => p.CommuneId).ThenBy(p => p.Segment == null || p.Segment.ExternalRef == null)
            .ThenBy(p => p.Segment == null ? null : p.Segment.ExternalRef)
            .ThenBy(p => p.CreatedAt).ThenBy(p => p.PoleId.Length).ThenBy(p => p.PoleId)
            .Take(WorkbookDocument.MaxRows + 1).ToListAsync(ct);
        // Also guard growth between the count and the bounded read.
        EnsureWithinLimit(poles.Count);
        var cabinetIds = poles.Where(p => p.Feeder?.CabinetId != null).Select(p => p.Feeder!.CabinetId!).Distinct().ToArray();
        var cabinets = await db.Set<ElectricalCabinet>().AsNoTracking().Where(c => cabinetIds.Contains(c.CabinetId))
            .ToDictionaryAsync(c => c.CabinetId, ct);

        using var stream = new MemoryStream();
        using (var package = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook, true))
        {
            var book = package.AddWorkbookPart();
            book.Workbook = new Workbook(new BookViews(new WorkbookView()), new Sheets());
            var styles = book.AddNewPart<WorkbookStylesPart>();
            styles.Stylesheet = new Stylesheet(
                new NumberingFormats(new NumberingFormat { NumberFormatId = 164, FormatCode = "yyyy-mm-dd" }) { Count = 1 },
                new Fonts(new Font()) { Count = 1 },
                new Fills(new Fill(new PatternFill { PatternType = PatternValues.None }),
                    new Fill(new PatternFill { PatternType = PatternValues.Gray125 })) { Count = 2 },
                new Borders(new Border()) { Count = 1 },
                new CellStyleFormats(new CellFormat()) { Count = 1 },
                new CellFormats(new CellFormat(), new CellFormat { NumberFormatId = 164, ApplyNumberFormat = true }) { Count = 2 });
            styles.Stylesheet.Save();

            var excluded = new List<object?[]>();
            var part = book.AddNewPart<WorksheetPart>();
            using (var writer = OpenXmlWriter.Create(part))
            {
                writer.WriteStartElement(new Worksheet());
                writer.WriteElement(new SheetViews(new SheetView(new Pane
                {
                    VerticalSplit = 1, TopLeftCell = "A2", ActivePane = PaneValues.BottomLeft, State = PaneStateValues.Frozen,
                }) { WorkbookViewId = 0 }));
                writer.WriteStartElement(new SheetData());
                WriteRow(writer, 1, WorkbookColumns.All.Select(c => (object?)c.Name));
                uint number = 2;
                foreach (var pole in poles)
                {
                    ct.ThrowIfCancellationRequested();
                    var reasons = new List<string>();
                    if (pole.ExternalRef is null) reasons.Add("Cột thiếu mã vĩnh viễn; gán mã cột qua form tài sản.");
                    if (pole.FeederId is not null && pole.Feeder?.ExternalRef is null)
                        reasons.Add("Mạch của cột thiếu mã vĩnh viễn; gán mã mạch qua form tài sản.");
                    if (reasons.Count > 0)
                    {
                        excluded.Add([pole.PoleId, pole.CommuneId, string.Join(" ", reasons)]);
                        continue;
                    }
                    var segment = pole.Segment?.ExternalRef is null ? null : pole.Segment;
                    var feeder = pole.Feeder;
                    var cabinet = feeder?.CabinetId is { } id ? cabinets.GetValueOrDefault(id) : null;
                    if (cabinet?.ExternalRef is null) cabinet = null;
                    var fitting = pole.Fixtures.SingleOrDefault();
                    WriteRow(writer, number++, WorkbookColumns.All.Select(c => Value(c.Name, pole, segment, feeder, cabinet, fitting)));
                }
                writer.WriteEndElement();
                writer.WriteElement(Validations());
                writer.WriteEndElement();
            }
            book.Workbook.Sheets!.Append(new Sheet { Name = "import", SheetId = 1, Id = book.GetIdOfPart(part) });
            if (excluded.Count > 0)
            {
                var other = book.AddNewPart<WorksheetPart>();
                using var writer = OpenXmlWriter.Create(other);
                writer.WriteStartElement(new Worksheet());
                writer.WriteStartElement(new SheetData());
                WriteRow(writer, 1, ["pole_id", "commune_id", "reason"]);
                for (var i = 0; i < excluded.Count; i++) WriteRow(writer, (uint)i + 2, excluded[i]);
                writer.WriteEndElement();
                writer.WriteEndElement();
                book.Workbook.Sheets.Append(new Sheet { Name = "Không xuất", SheetId = 2, Id = book.GetIdOfPart(other) });
            }
            book.Workbook.Save();
        }
        return stream.ToArray();
    }

    private static void EnsureWithinLimit(int count)
    {
        if (count > WorkbookDocument.MaxRows)
            throw new LuxMapException(ErrorCodes.ExportTooLarge, HttpStatusCode.RequestEntityTooLarge,
                $"At most {WorkbookDocument.MaxRows} poles can be exported. Filter by commune_id.",
                new Dictionary<string, object?> { ["rows"] = count, ["max_rows"] = WorkbookDocument.MaxRows });
    }

    private static object? Value(string column, Pole p, RoadSegment? s, Feeder? f, ElectricalCabinet? c, Fixture? lamp)
        => column switch
        {
            "commune_id" => p.CommuneId, "data_source" => WireEnum.Name(p.DataSource),
            "segment_ref" => s?.ExternalRef, "segment_name" => s?.SegmentName,
            "road_class" => s is null ? null : WireEnum.Name(s.RoadClass), "segment_length_m" => s?.LengthM,
            "cabinet_ref" => c?.ExternalRef, "cabinet_name" => c?.CabinetName,
            "cabinet_lat" => c?.Geom.Y, "cabinet_lng" => c?.Geom.X,
            "feeder_ref" => f?.ExternalRef, "feeder_name" => f?.FeederName,
            "cabinet_source" => f?.CabinetSource is { } cs ? WireEnum.Name(cs) : null,
            "pole_ref" => p.ExternalRef, "pole_lat" => p.Geom?.Y, "pole_lng" => p.Geom?.X,
            "near_sensitive_poi" => p.NearSensitivePoi ? "true" : "false", "pole_note" => p.Note,
            "feeder_source" => p.FeederSource is { } fs ? WireEnum.Name(fs) : null,
            "fixture_type" => lamp is null ? null : WireEnum.Name(lamp.FixtureType),
            "power_source" => lamp is null ? null : WireEnum.Name(lamp.PowerSource),
            "lamp_watt" => lamp?.LampWatt, "lamp_count" => lamp?.LampCount,
            "install_date" => lamp?.InstallDate, "warranty_expiry" => lamp?.WarrantyExpiry, "fixture_note" => lamp?.Note,
            _ => throw new InvalidOperationException($"No export value for workbook column {column}."),
        };

    private static void WriteRow(OpenXmlWriter writer, uint number, IEnumerable<object?> values)
    {
        writer.WriteStartElement(new Row { RowIndex = number });
        var index = 0;
        foreach (var value in values)
        {
            var reference = $"{(char)('A' + index++)}{number}"; // WB-2 has exactly 26 columns (A…Z).
            if (value is null or "") continue;
            var cell = new Cell { CellReference = reference };
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
                cell.DataType = CellValues.InlineString;
                cell.InlineString = new InlineString(new Text((string)value) { Space = SpaceProcessingModeValues.Preserve });
            }
            writer.WriteElement(cell);
        }
        writer.WriteEndElement();
    }

    private static DataValidations Validations()
    {
        var result = new DataValidations();
        for (var i = 0; i < WorkbookColumns.All.Count; i++)
        {
            var values = WorkbookColumns.All[i].Name switch
            {
                "data_source" => Choices<DataSource>(), "road_class" => Choices<RoadClass>(),
                "fixture_type" => Choices<FixtureType>(), "power_source" => Choices<PowerSource>(),
                "cabinet_source" or "feeder_source" => Choices<TopologySource>(), "near_sensitive_poi" => "true,false",
                _ => null,
            };
            if (values is null) continue;
            var letter = (char)('A' + i);
            result.Append(new DataValidation(new Formula1($"\"{values}\""))
            {
                Type = DataValidationValues.List, AllowBlank = true, ShowErrorMessage = true,
                SequenceOfReferences = new ListValue<StringValue> { InnerText = $"{letter}2:{letter}{WorkbookDocument.MaxRows + 1}" },
            });
        }
        result.Count = (uint)result.ChildElements.Count;
        return result;
    }

    private static string Choices<T>() where T : struct, Enum => string.Join(',', Enum.GetValues<T>().Select(WireEnum.Name));
}
