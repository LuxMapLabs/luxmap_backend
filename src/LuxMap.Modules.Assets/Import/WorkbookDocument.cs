using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using LuxMap.Shared.Contracts.Errors;
using LuxMap.Shared.Http;

namespace LuxMap.Modules.Assets.Import;

/// <summary>Bounded SAX reader. Asset sheets are identified by name; only bounded rows and referenced shared strings are retained.</summary>
internal sealed class WorkbookDocument
{
    public const int MaxRows = 5000;
    public const int MaxColumns = 200;
    public const int MaxCellLength = 2000;

    /// <summary>
    /// Sum of the zip entries' declared uncompressed sizes. The row / column / cell limits are checked while reading, but
    /// <c>OpenXmlReader.GetText</c> materialises a whole text node before its length can be checked, so a 10 MB upload that
    /// inflates to gigabytes must be refused before any part is opened. A real 5 000-row inventory is a few MB unzipped.
    /// </summary>
    public const long MaxUncompressedBytes = 100L * 1024 * 1024;

    internal sealed record Value(string Text, string? Type);
    internal sealed record SheetRow(int Number, Dictionary<string, string?> Cells);
    public Dictionary<ImportKind, List<SheetRow>> Sheets { get; } = [];
    private List<SheetRow> Rows = [];
    private string? sheetName;
    public List<WorkbookRowError> Errors { get; } = [];

    public static WorkbookDocument Read(Stream stream)
    {
        try
        {
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true))
            {
                if (zip.Entries.Sum(entry => entry.Length) > MaxUncompressedBytes)
                {
                    throw new LuxMapException(ErrorCodes.ValidationFailed, HttpStatusCode.BadRequest,
                        "The workbook expands to more than 100 MB; it is not an asset inventory.");
                }
            }

            stream.Position = 0;
            using var package = SpreadsheetDocument.Open(stream, false);
            var book = package.WorkbookPart ?? throw new InvalidDataException("Missing workbook.");
            var document = new WorkbookDocument();
            foreach (var sheet in book.Workbook?.Sheets?.Elements<Sheet>() ?? [])
            {
                var name = (sheet.Name?.Value ?? "").Trim().Normalize(NormalizationForm.FormC);
                var layout = WorkbookSheets.All.SingleOrDefault(s => s.Name == name);
                if (layout is null && name != "IoT") continue;
                var sheetId = sheet.Id?.Value ?? throw new InvalidDataException("Missing sheet relationship.");
                var part = book.GetPartById(sheetId) as WorksheetPart
                    ?? throw new InvalidDataException("Not a worksheet.");
                document.sheetName = name;
                document.Rows = [];
                if (layout is not null && document.Sheets.ContainsKey(layout.Kind))
                {
                    document.Errors.Add(new(name, 1, null, "Duplicate asset sheet."));
                    continue;
                }
                document.ReadSheet(part, book.SharedStringTablePart, layout);
                if (layout is not null) document.Sheets[layout.Kind] = document.Rows;
            }
            if (document.Sheets.Count == 0)
                document.Errors.Add(new(null, 1, null, "No asset sheet found"));
            return document;
        }
        catch (Exception ex) when (ex is OpenXmlPackageException or InvalidDataException or System.Xml.XmlException
                                   or ArgumentException or InvalidOperationException or System.IO.FileFormatException or FormatException or OverflowException)
        {
            throw new LuxMapException(ErrorCodes.ValidationFailed, HttpStatusCode.BadRequest, "Not a readable .xlsx workbook.");
        }
    }

    private void ReadSheet(WorksheetPart part, SharedStringTablePart? strings, WorkbookSheets.Layout? layout)
    {
        var errorStart = Errors.Count;
        var raw = new List<(int Number, Dictionary<int, Value> Cells)>();
        var needed = new HashSet<int>();
        using (var reader = OpenXmlReader.Create(part))
        {
            Dictionary<int, Value>? cells = null;
            int row = 0, column = 0, nextColumn = 0;
            string? type = null;
            var text = new StringBuilder();
            while (reader.Read())
            {
                if (reader.IsStartElement && reader.ElementType == typeof(Row))
                {
                    var number = Attribute(reader, "r");
                    row = number is null ? row + 1 : int.Parse(number, CultureInfo.InvariantCulture);
                    if (row < 1) throw new InvalidDataException("Invalid row number.");
                    cells = [];
                    nextColumn = 0;
                }
                else if (reader.IsStartElement && reader.ElementType == typeof(Cell))
                {
                    column = ColumnIndex(Attribute(reader, "r"), nextColumn);
                    nextColumn = column + 1;
                    if (column >= MaxColumns || nextColumn > MaxColumns)
                    {
                        Errors.Add(new(sheetName, 1, null, "A worksheet may have at most 200 columns."));
                        return;
                    }
                    type = Attribute(reader, "t");
                    text.Clear();
                }
                else if (reader.IsStartElement && (reader.ElementType == typeof(CellValue) || reader.ElementType == typeof(Text)))
                {
                    text.Append(reader.GetText());
                    if (text.Length > MaxCellLength)
                    {
                        var headers = raw.FirstOrDefault(r => r.Number == 1).Cells;
                        var headerStrings = ReadStrings(strings, needed, raw);
                        var name = headers is not null && headers.TryGetValue(column, out var headerCell)
                            ? Resolve(headerCell, headerStrings) : null;
                        Errors.Add(new(sheetName, row, name, "A cell may have at most 2000 characters."));
                        return;
                    }
                }
                else if (reader.IsEndElement && reader.ElementType == typeof(Cell))
                {
                    var value = text.ToString().Trim();
                    if (type == "s" && int.TryParse(value, out var index)) needed.Add(index);
                    if (cells is null) throw new InvalidDataException("Cell is outside a row.");
                    if (value.Length > 0) cells[column] = new(value, type);
                }
                else if (reader.IsEndElement && reader.ElementType == typeof(Row))
                {
                    // Shared strings may resolve to whitespace: the final nonempty count is checked below.
                    if (row == 1 || cells!.Count > 0) raw.Add((row, cells!));
                    if (raw.Count > MaxRows + 1 && needed.Count == 0)
                    {
                        Errors.Add(new(sheetName, 1, null, "A worksheet may have at most 5000 data rows."));
                        return;
                    }
                    // Bound retained raw rows even for a sheet made entirely of shared whitespace.
                    // Resolve shared strings before reading when the bounded window fills.
                    if (raw.Count > MaxRows + 1)
                    {
                        var resolved = ReadStrings(strings, needed, raw);
                        if (Errors.Count > errorStart) return;
                        raw.RemoveAll(r => r.Number != 1 && r.Cells.Values.All(v => Resolve(v, resolved).Length == 0));
                        needed.Clear();
                        foreach (var cell in raw.SelectMany(r => r.Cells.Values).Where(v => v.Type == "s"))
                            needed.Add(int.Parse(cell.Text, CultureInfo.InvariantCulture));
                        if (raw.Count > MaxRows + 1)
                        {
                            Errors.Add(new(sheetName, 1, null, "A worksheet may have at most 5000 data rows."));
                            return;
                        }
                    }
                }
            }
        }
        var shared = ReadStrings(strings, needed, raw);
        if (Errors.Count > errorStart) return;
        if (layout is null)
        {
            var dataRow = raw.FirstOrDefault(r => r.Number != 1 && r.Cells.Values.Any(v => Resolve(v, shared).Length > 0));
            if (dataRow.Number != 0)
                Errors.Add(new(sheetName, dataRow.Number, null, "IoT import is not supported yet"));
            return;
        }
        var header = raw.FirstOrDefault(r => r.Number == 1).Cells;
        if (header is null)
        {
            Errors.Add(new(sheetName, 1, null, "Column missing from the header row."));
            return;
        }
        var columns = header.ToDictionary(p => p.Key, p => Resolve(p.Value, shared));
        foreach (var duplicate in columns.Values.Where(v => v.Length > 0).GroupBy(v => v, StringComparer.Ordinal).Where(g => g.Count() > 1))
            Errors.Add(new(sheetName, 1, duplicate.Key, "Duplicate column in the header row."));
        foreach (var (index, name) in columns)
        {
            if (name is "commune_id" or "data_source"
                || (!layout.Columns.Contains(name, StringComparer.Ordinal)
                    && raw.Any(r => r.Number != 1 && r.Cells.TryGetValue(index, out var v) && Resolve(v, shared).Length > 0)))
                Errors.Add(new(sheetName, 1, name, $"Column '{name}' is not supported in this sheet."));
        }
        // A nonempty cell without a header must not silently disappear either.
        if (raw.Any(r => r.Number != 1 && r.Cells.Any(c => !columns.ContainsKey(c.Key) && Resolve(c.Value, shared).Length > 0)))
            Errors.Add(new(sheetName, 1, null, "A nonempty column has no header."));
        var missing = layout.Required.Except(columns.Values, StringComparer.Ordinal).ToArray();
        if (missing.Length > 0) Errors.Add(new(sheetName, 1, string.Join(", ", missing), "Column missing from the header row."));
        if (Errors.Count > errorStart) return;
        foreach (var (number, cells) in raw.Where(r => r.Number != 1))
        {
            if (cells.Values.All(v => Resolve(v, shared).Length == 0)) continue;
            if (Rows.Count == MaxRows)
            {
                Errors.Add(new(sheetName, 1, null, "A worksheet may have at most 5000 data rows."));
                return;
            }
            var values = new Dictionary<string, string?>(StringComparer.Ordinal);
            foreach (var (index, name) in columns.Where(p => p.Value.Length > 0))
            {
                if (!cells.TryGetValue(index, out var cell)) continue;
                var value = Resolve(cell, shared);
                if (cell.Type == "e") Errors.Add(new(sheetName, number, name, $"Excel cell error: {value}."));
                if (cell.Type == "b") value = value == "1" ? "true" : value == "0" ? "false" : value;
                if (cell.Type is null or "n")
                {
                    if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var numeric) && double.IsFinite(numeric))
                    {
                        if (name is "install_date" or "removed_date" or "warranty_expiry")
                        {
                            if (numeric != Math.Truncate(numeric)) Errors.Add(new(sheetName, number, name, "A date serial must not contain a time."));
                            else
                            {
                                try { value = DateTime.FromOADate(numeric).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture); }
                                catch (ArgumentException) { Errors.Add(new(sheetName, number, name, "Invalid date serial.")); }
                            }
                        }
                        else value = numeric.ToString("G17", CultureInfo.InvariantCulture);
                    }
                }
                values[name] = value.Length == 0 ? null : value;
            }
            Rows.Add(new(number, values));
        }
    }

    private Dictionary<int, string> ReadStrings(SharedStringTablePart? part, HashSet<int> needed,
        List<(int Number, Dictionary<int, Value> Cells)> raw)
    {
        var values = new Dictionary<int, string>();
        if (part is null) return values;
        using var reader = OpenXmlReader.Create(part);
        var text = new StringBuilder();
        var index = -1;
        while (reader.Read())
        {
            if (reader.IsStartElement && reader.ElementType == typeof(SharedStringItem)) { index++; text.Clear(); }
            else if (reader.IsStartElement && reader.ElementType == typeof(Text))
            {
                if (!needed.Contains(index)) continue;
                text.Append(reader.GetText());
                if (text.Length > MaxCellLength)
                {
                    var location = raw.FirstOrDefault(r => r.Cells.Values.Any(v => v.Type == "s" && v.Text == index.ToString(CultureInfo.InvariantCulture)));
                    var column = location.Cells?.FirstOrDefault(p => p.Value.Type == "s" && p.Value.Text == index.ToString(CultureInfo.InvariantCulture)).Key;
                    var header = raw.FirstOrDefault(r => r.Number == 1).Cells;
                    string? name = null;
                    if (column is not null && header is not null && header.TryGetValue(column.Value, out var cell))
                    {
                        if (cell.Type != "s") name = cell.Text;
                        else if (int.TryParse(cell.Text, out var headerIndex)) values.TryGetValue(headerIndex, out name);
                    }
                    Errors.Add(new(sheetName, location.Number == 0 ? 1 : location.Number, name, "A shared string may have at most 2000 characters."));
                    return values;
                }
            }
            else if (reader.IsEndElement && reader.ElementType == typeof(SharedStringItem) && needed.Contains(index))
                values[index] = text.ToString().Trim();
        }
        return values;
    }

    private static string Resolve(Value value, Dictionary<int, string> shared)
        => value.Type != "s" ? value.Text
            : int.TryParse(value.Text, out var index) && shared.TryGetValue(index, out var text) ? text
            : throw new InvalidDataException("Invalid shared string reference.");

    private static string? Attribute(OpenXmlReader reader, string name)
        => reader.Attributes.FirstOrDefault(a => a.LocalName == name).Value;

    private static int ColumnIndex(string? reference, int fallback)
    {
        if (reference is null) return fallback;
        var index = 0;
        foreach (var c in reference.TakeWhile(char.IsLetter))
        {
            if (c is < 'A' or > 'Z' || index > MaxColumns) return MaxColumns;
            index = index * 26 + c - 'A' + 1;
        }
        return index == 0 ? throw new InvalidDataException("Invalid cell reference.") : index - 1;
    }
}
