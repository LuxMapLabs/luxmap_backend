using System.Globalization;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace LuxMap.Api.Tests;

/// <summary>Small typed workbook builder, including sparse cells and explicit Excel row numbers.</summary>
internal static class AssetWorkbookFile
{
    internal sealed record Cached(string Value, CellValues Type, string? Formula = null);
    public static readonly string[] Columns = ["commune_id", "data_source", "segment_ref", "segment_name", "road_class",
        "segment_length_m", "cabinet_ref", "cabinet_name", "cabinet_lat", "cabinet_lng", "feeder_ref", "feeder_name",
        "cabinet_source", "pole_ref", "pole_lat", "pole_lng", "near_sensitive_poi", "pole_note", "feeder_source",
        "fixture_type", "power_source", "lamp_watt", "lamp_count", "install_date", "warranty_expiry", "fixture_note"];

    public static byte[] Create(IEnumerable<(uint Number, Dictionary<string, object?> Cells)> rows,
        string[]? columns = null, bool garbageSecondSheet = false, string[]? sharedStrings = null)
    {
        columns ??= Columns;
        using var stream = new MemoryStream();
        using (var package = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook, true))
        {
            var book = package.AddWorkbookPart();
            book.Workbook = new Workbook(new Sheets());
            if (sharedStrings is not null)
            {
                var shared = book.AddNewPart<SharedStringTablePart>();
                shared.SharedStringTable = new SharedStringTable(sharedStrings.Select(s => new SharedStringItem(new Text(s))));
                shared.SharedStringTable.Save();
            }
            var part = book.AddNewPart<WorksheetPart>();
            using (var writer = OpenXmlWriter.Create(part))
            {
                writer.WriteStartElement(new Worksheet());
                writer.WriteStartElement(new SheetData());
                WriteRow(writer, 1, columns.Select((c, i) => (i, (object?)c)));
                foreach (var row in rows)
                    WriteRow(writer, row.Number, columns.Select((c, i) => (i, row.Cells.GetValueOrDefault(c))).Where(c => c.Item2 is not null));
                writer.WriteEndElement();
                writer.WriteEndElement();
            }
            book.Workbook.Sheets!.Append(new Sheet { Name = "import", SheetId = 1, Id = book.GetIdOfPart(part) });
            if (garbageSecondSheet)
            {
                var other = book.AddNewPart<WorksheetPart>();
                other.Worksheet = new Worksheet(new SheetData(new Row(new Cell(new InlineString(new Text("garbage"))) { DataType = CellValues.InlineString }) { RowIndex = 1 }));
                other.Worksheet.Save();
                book.Workbook.Sheets.Append(new Sheet { Name = "ignored", SheetId = 2, Id = book.GetIdOfPart(other) });
            }
            book.Workbook.Save();
        }
        return stream.ToArray();
    }

    private static void WriteRow(OpenXmlWriter writer, uint number, IEnumerable<(int Index, object? Value)> cells)
    {
        writer.WriteStartElement(new Row { RowIndex = number });
        foreach (var (index, value) in cells)
        {
            var name = "";
            for (var n = index + 1; n > 0; n = (n - 1) / 26) name = (char)('A' + (n - 1) % 26) + name;
            var cell = new Cell { CellReference = name + number };
            if (value is Cached cached)
            {
                cell.DataType = cached.Type;
                if (cached.Formula is not null) cell.CellFormula = new CellFormula(cached.Formula);
                cell.CellValue = new CellValue(cached.Value);
            }
            else if (value is bool flag)
            {
                cell.DataType = CellValues.Boolean;
                cell.CellValue = new CellValue(flag ? "1" : "0");
            }
            else if (value is int or double)
            {
                cell.DataType = CellValues.Number;
                cell.CellValue = new CellValue(Convert.ToString(value, CultureInfo.InvariantCulture)!);
            }
            else
            {
                cell.DataType = CellValues.InlineString;
                cell.InlineString = new InlineString(new Text(value?.ToString() ?? ""));
            }
            writer.WriteElement(cell);
        }
        writer.WriteEndElement();
    }
}
