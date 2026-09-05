using System.Text;

namespace LuxMap.Shared.Csv;

/// <summary>
/// A parsed CSV file: a header row plus the data rows beneath it, addressed by column NAME.
/// </summary>
/// <remarks>
/// Hand-written rather than a package, deliberately. The whole surface is four behaviours the
/// spreadsheet files actually exhibit — UTF-8 BOM, CRLF, quoted fields containing the delimiter, and
/// a semicolon delimiter from a Vietnamese Windows locale — and none of them needs a dependency.
/// <para>
/// ⚠️ <c>string.Split(',')</c> is NOT an option and never was: <c>docs/templates/segments.csv</c>
/// ships a quoted <c>geom_wkt</c> cell containing two commas, so the simplest possible parser breaks
/// on the very first template file.
/// </para>
/// <para>
/// It lives in <c>LuxMap.Shared</c> with no package reference, so its tests run in an assembly that
/// needs neither a database nor Docker.
/// </para>
/// </remarks>
public sealed class CsvDocument
{
    private CsvDocument(char delimiter, IReadOnlyList<string> headers, IReadOnlyList<CsvRow> rows)
    {
        Delimiter = delimiter;
        Headers = headers;
        Rows = rows;
    }

    /// <summary>Whichever of <c>,</c> or <c>;</c> was detected. Reported so an import can say which it used.</summary>
    public char Delimiter { get; }

    /// <summary>Header names, trimmed and lower-cased. Empty when the file held no rows at all.</summary>
    public IReadOnlyList<string> Headers { get; }

    public IReadOnlyList<CsvRow> Rows { get; }

    /// <summary>Column names the file is missing, given what the caller requires.</summary>
    public IReadOnlyList<string> MissingColumns(IEnumerable<string> required)
        => [.. (required ?? []).Where(name => !Headers.Contains(name, StringComparer.Ordinal))];

    public static CsvDocument Parse(string text)
    {
        // Excel's "CSV UTF-8" writes a BOM. Left in place it becomes part of the FIRST header name,
        // so `segment_name` silently stops matching and every row reports a missing column.
        text = (text ?? string.Empty).TrimStart('﻿');

        var delimiter = SniffDelimiter(text);
        var records = ReadRecords(text, delimiter);
        if (records.Count == 0)
        {
            return new CsvDocument(',', [], []);
        }

        var headers = records[0].Fields.Select(name => name.Trim().ToLowerInvariant()).ToArray();

        var index = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < headers.Length; i++)
        {
            // First occurrence wins; a duplicated header is a file defect the caller reports, not
            // something to throw on while still reading.
            index.TryAdd(headers[i], i);
        }

        var rows = records.Skip(1)
            .Where(record => record.Fields.Length > 1 || record.Fields[0].Trim().Length > 0)
            .Select(record => new CsvRow(record.Line, index, record.Fields))
            .ToArray();

        return new CsvDocument(delimiter, headers, rows);
    }

    /// <summary>
    /// Counts delimiters OUTSIDE quotes on the first line, so the two commas inside a quoted
    /// <c>LINESTRING(...)</c> cell can never outvote the real separators.
    /// </summary>
    private static char SniffDelimiter(string text)
    {
        int commas = 0, semicolons = 0;
        var quoted = false;

        foreach (var c in text)
        {
            if (c == '"')
            {
                quoted = !quoted;
            }
            else if (!quoted && (c == '\n' || c == '\r'))
            {
                break;
            }
            else if (!quoted && c == ',')
            {
                commas++;
            }
            else if (!quoted && c == ';')
            {
                semicolons++;
            }
        }

        return semicolons > commas ? ';' : ',';
    }

    /// <summary>
    /// One pass over the text producing whole records. RFC 4180: quotes protect the delimiter AND
    /// newlines, and <c>""</c> inside a quoted field is one literal quote.
    /// </summary>
    private static List<(string[] Fields, int Line)> ReadRecords(string text, char delimiter)
    {
        var records = new List<(string[], int)>();
        var fields = new List<string>();
        var cell = new StringBuilder();
        var quoted = false;
        var line = 1;
        var recordLine = 1;

        void EndField() { fields.Add(cell.ToString()); cell.Clear(); }

        void EndRecord()
        {
            EndField();
            records.Add(([.. fields], recordLine));
            fields.Clear();
        }

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];

            if (quoted)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"')
                {
                    cell.Append('"');
                    i++;
                }
                else if (c == '"')
                {
                    quoted = false;
                }
                else
                {
                    cell.Append(c);
                    if (c == '\n')
                    {
                        line++;
                    }
                }
            }
            else if (c == '"')
            {
                quoted = true;
            }
            else if (c == delimiter)
            {
                EndField();
            }
            else if (c == '\n' || c == '\r')
            {
                // CRLF is one break, not two: swallow the LF that follows a CR.
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                {
                    i++;
                }

                EndRecord();
                line++;
                recordLine = line;
            }
            else
            {
                cell.Append(c);
            }
        }

        if (cell.Length > 0 || fields.Count > 0)
        {
            EndRecord();
        }

        return records;
    }
}

/// <summary>One data row. Cells are addressed by column name; a missing or blank cell reads as null.</summary>
public sealed class CsvRow(int lineNumber, IReadOnlyDictionary<string, int> columns, string[] cells)
{
    /// <summary>1-based line in the FILE, so an error message points where the person can look.</summary>
    public int LineNumber { get; } = lineNumber;

    /// <summary>
    /// Trimmed cell value, or <c>null</c> when absent or blank.
    /// </summary>
    /// <remarks>
    /// Blank collapses to null on purpose: a trailing <c>\r</c> from a CRLF file, an empty
    /// <c>removed_date</c>, and a cell of spaces all mean "no value", and every caller would
    /// otherwise repeat the same three checks.
    /// </remarks>
    public string? this[string column]
    {
        get
        {
            if (!columns.TryGetValue(column, out var i) || i >= cells.Length)
            {
                return null;
            }

            var value = cells[i].Trim();
            return value.Length == 0 ? null : value;
        }
    }
}
