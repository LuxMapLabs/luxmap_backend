using System.Globalization;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace LuxMap.Api.Tests;

/// <summary>Small typed workbook builder, including sparse cells and explicit Excel row numbers.</summary>
internal static class AssetWorkbookFile
{
    internal sealed record Cached(string Value, CellValues Type, string? Formula = null);
    internal sealed record Sheet(string Name, string[] Columns, List<(uint Number, Dictionary<string, object?> Cells)> Rows);
    public static byte[] Create(params Sheet[] sheets) => WithSharedStrings(null, sheets);

    public static byte[] WithSharedStrings(string[]? strings, params Sheet[] sheets)
    {
        using var stream = new MemoryStream();
        using (var package = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook, true))
        {
            var book = package.AddWorkbookPart();
            book.Workbook = new Workbook(new Sheets());
            if (strings is not null)
            {
                var shared = book.AddNewPart<SharedStringTablePart>();
                shared.SharedStringTable = new SharedStringTable(strings.Select(s => new SharedStringItem(new Text(s))));
                shared.SharedStringTable.Save();
            }
            foreach (var sheet in sheets)
            {
                var part = book.AddNewPart<WorksheetPart>();
                using (var writer = OpenXmlWriter.Create(part))
                {
                    writer.WriteStartElement(new Worksheet());
                    writer.WriteStartElement(new SheetData());
                    WriteRow(writer, 1, sheet.Columns.Select((c, i) => (i, (object?)c)));
                    foreach (var row in sheet.Rows)
                        WriteRow(writer, row.Number, sheet.Columns.Select((c, i) => (i, row.Cells.GetValueOrDefault(c))).Where(c => c.Item2 is not null));
                    writer.WriteEndElement();
                    writer.WriteEndElement();
                }
                book.Workbook.Sheets!.Append(new DocumentFormat.OpenXml.Spreadsheet.Sheet { Name = sheet.Name,
                    SheetId = (uint)book.Workbook.Sheets.ChildElements.Count + 1, Id = book.GetIdOfPart(part) });
            }
            book.Workbook.Save();
        }
        return stream.ToArray();
    }

    public static Sheet Asset(LuxMap.Modules.Assets.Import.ImportKind kind, params Dictionary<string, object?>[] rows)
    {
        var layout = LuxMap.Modules.Assets.Import.WorkbookSheets.All.Single(s => s.Kind == kind);
        return new(layout.Name, layout.Columns, rows.Select((r, i) => ((uint)i + 2, r)).ToList());
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
