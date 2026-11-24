using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.IO.Compression;
using System.Net;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using LuxMap.Modules.IOT.Registry;
using LuxMap.Shared.Authorization;
using LuxMap.Shared.Contracts.Errors;
using LuxMap.Shared.Http;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LuxMap.Modules.IOT;

public sealed record IotNodeImportError(string Code, string Message, string? Field = null);
public sealed record IotNodeImportItem(int Row, string? ServerId, string? FeederId, string? DeviceId,
    string Status, string? NodeId, IotNodeImportError? Error);
public sealed record IotNodeImportResult(string Sheet, int Total, int Created, int Existing, int Failed,
    IReadOnlyList<IotNodeImportItem> Items);

public sealed partial class IotDiagnosticsController
{
    /// <summary>Admin thêm nhiều node từ Excel; mỗi dòng là một transaction độc lập, trả kết quả theo số dòng.</summary>
    [HttpPost("nodes/import")]
    [Authorize(Policy = LuxMapPolicies.ManageSettings)]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(6 * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 5 * 1024 * 1024)]
    [ProducesResponseType<IotNodeImportResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status415UnsupportedMediaType)]
    public async Task<IotNodeImportResult> ImportNodesAsync([Required] IFormFile file,
        [FromServices] IServiceScopeFactory scopes, CancellationToken ct)
    {
        if (!file.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
            throw new LuxMapException(ErrorCodes.UnsupportedMediaType, HttpStatusCode.UnsupportedMediaType, "Chỉ nhận file .xlsx.");
        if (file.Length <= 0 || file.Length > 5 * 1024 * 1024)
            throw Invalid("File phải có dữ liệu và không vượt quá 5 MiB.");
        using var stream = new MemoryStream();
        await file.CopyToAsync(stream, ct);
        stream.Position = 0;
        var (sheet, rows) = IotNodeImportReader.Read(stream);
        var items = new List<IotNodeImportItem>();
        var feeders = new HashSet<string>(StringComparer.Ordinal);
        var devices = new HashSet<(string Server, string Device)>();
        foreach (var row in rows)
        {
            ct.ThrowIfCancellationRequested();
            var request = row.Request;
            if (row.Error is { } parseError)
            {
                items.Add(new(row.Number, request.ServerId, request.FeederId, request.DeviceId, "failed", null, parseError));
                continue;
            }
            var duplicate = !feeders.Add(request.FeederId) |
                !devices.Add((request.ServerId.ToLowerInvariant(), request.DeviceId));
            if (duplicate)
            {
                items.Add(new(row.Number, request.ServerId, request.FeederId, request.DeviceId, "failed", null,
                    new(ErrorCodes.ValidationFailed, "Trùng feeder hoặc thiết bị MQTT trong file.")));
                continue;
            }
            // A failed PostgreSQL transaction/tracked entity must not contaminate the next row.
            await using var scope = scopes.CreateAsyncScope();
            var services = scope.ServiceProvider;
            try
            {
                var (node, created) = await RegisterNodeCoreAsync(request,
                    services.GetRequiredService<IotDiagnosticsService>(), services.GetRequiredService<IotRegistrationStore>(),
                    services.GetRequiredService<IotNodeRegistryService>(), services.GetRequiredService<IotFeederSourceLookup>(),
                    services.GetRequiredService<IConfiguration>(), ct);
                items.Add(new(row.Number, node.ServerId, node.FeederId, node.DeviceId,
                    created ? "created" : "existing", node.NodeId, null));
            }
            catch (LuxMapException error) when (!ct.IsCancellationRequested)
            {
                items.Add(new(row.Number, request.ServerId, request.FeederId, request.DeviceId, "failed", null,
                    new(error.Code, error.Message, error.Details.GetValueOrDefault("field")?.ToString())));
            }
        }
        return new(sheet, items.Count, items.Count(i => i.Status == "created"), items.Count(i => i.Status == "existing"),
            items.Count(i => i.Status == "failed"), items);
    }
}

internal static class IotNodeImportReader
{
    internal const int MaxRows = 100;
    private static readonly string[] Columns = ["server_id", "feeder_id", "device_id", "nominal_voltage", "nominal_current_ma", "nominal_power_mw", "radius_km"];
    internal sealed record ImportRow(int Number, IotDiagnosticsController.RegisterNodeRequest Request, IotNodeImportError? Error);

    internal static (string Sheet, IReadOnlyList<ImportRow> Rows) Read(Stream stream)
    {
        try
        {
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true))
                if (zip.Entries.Count > 256 || zip.Entries.Sum(e => e.Length) > 25 * 1024 * 1024)
                    throw Bad("File Excel vượt giới hạn giải nén 25 MiB hoặc 256 thành phần.");
            stream.Position = 0;
            using var package = SpreadsheetDocument.Open(stream, false, new OpenSettings { MaxCharactersInPart = 1_000_000 });
            var book = package.WorkbookPart ?? throw Bad("Không có workbook.");
            var workbook = book.Workbook ?? throw Bad("Không có workbook.");
            var sheets = workbook.Sheets?.Elements<Sheet>().ToArray() ?? [];
            var sheet = sheets.FirstOrDefault(s => string.Equals(s.Name?.Value, "Nodes", StringComparison.OrdinalIgnoreCase))
                ?? sheets.FirstOrDefault() ?? throw Bad("File không có sheet.");
            if (book.GetPartById(sheet.Id?.Value ?? "") is not WorksheetPart part) throw Bad("Sheet không hợp lệ.");
            var strings = book.SharedStringTablePart?.SharedStringTable?.Elements<SharedStringItem>().Select(s => s.InnerText).ToArray() ?? [];
            var worksheet = part.Worksheet ?? throw Bad("Không có worksheet.");
            var rows = worksheet.GetFirstChild<SheetData>()?.Elements<Row>().ToArray() ?? [];
            if (rows.Length == 0 || rows.Length > 1001) throw Bad("Sheet rỗng hoặc quá nhiều dòng định dạng.");
            var header = Cells(rows[0], strings).Where(c => !string.IsNullOrWhiteSpace(c.Value)).ToDictionary();
            if (rows[0].RowIndex?.Value != 1 || rows[0].Elements<Cell>().Any(c => c.CellFormula is not null) ||
                header.Values.Any(v => !Columns.Contains(v, StringComparer.OrdinalIgnoreCase)) ||
                header.Values.Distinct(StringComparer.OrdinalIgnoreCase).Count() != header.Count ||
                Columns.Take(6).Any(c => !header.Values.Contains(c, StringComparer.OrdinalIgnoreCase)))
                throw Bad("Dòng 1 phải có server_id, feeder_id, device_id, nominal_voltage, nominal_current_ma, nominal_power_mw; radius_km là tùy chọn.");
            var result = new List<ImportRow>();
            foreach (var row in rows.Skip(1))
            {
                if (row.RowIndex?.Value is not > 1) throw Bad("Số dòng Excel không hợp lệ.");
                var cells = Cells(row, strings);
                if (cells.Values.All(string.IsNullOrWhiteSpace)) continue;
                if (result.Count >= MaxRows) throw Bad("Mỗi file nhận tối đa 100 dòng node.");
                var fields = header.ToDictionary(h => h.Value, h => cells.GetValueOrDefault(h.Key, ""), StringComparer.OrdinalIgnoreCase);
                IotNodeImportError? error = null;
                string Text(string key) => fields.GetValueOrDefault(key, "").Trim();
                double Number(string key, bool optional = false)
                {
                    var value = Text(key);
                    if (optional && value.Length == 0) return 0;
                    if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) &&
                        double.IsFinite(number) && (optional ? number is >= 0 and <= 1000 : number > 0)) return number;
                    error ??= new(ErrorCodes.ValidationFailed, optional ? "Bán kính phải từ 0 đến 1000 km." : "Giá trị danh định phải là số dương, dùng dấu chấm thập phân.", key);
                    return 0;
                }
                var request = new IotDiagnosticsController.RegisterNodeRequest {
                    ServerId = Text("server_id"), FeederId = Text("feeder_id"), DeviceId = Text("device_id"),
                    NominalVoltage = Number("nominal_voltage"), NominalCurrentMa = Number("nominal_current_ma"),
                    NominalPowerMw = Number("nominal_power_mw"), RadiusKm = Number("radius_km", true),
                };
                if (cells.Any(c => !header.ContainsKey(c.Key) && !string.IsNullOrWhiteSpace(c.Value))) error ??= new(ErrorCodes.ValidationFailed, "Có dữ liệu ở cột không có tiêu đề.");
                if (row.Elements<Cell>().Any(c => c.CellFormula is not null)) error = new(ErrorCodes.ValidationFailed, "Không nhận công thức Excel; hãy dán giá trị.");
                result.Add(new(checked((int)row.RowIndex!.Value), request, error));
            }
            if (result.Count == 0) throw Bad("File không có dòng node.");
            return (sheet.Name?.Value ?? "", result);
        }
        catch (Exception ex) when (ex is OpenXmlPackageException or InvalidDataException or System.Xml.XmlException or
            ArgumentException or InvalidOperationException or System.IO.FileFormatException or FormatException or OverflowException)
        {
            throw Bad("File .xlsx không đọc được hoặc có cấu trúc không hợp lệ.");
        }
    }

    private static Dictionary<int, string> Cells(Row row, string[] strings)
    {
        var result = new Dictionary<int, string>();
        var next = 0;
        foreach (var cell in row.Elements<Cell>())
        {
            var index = 0;
            var letters = cell.CellReference?.Value?.TakeWhile(char.IsAsciiLetter).ToArray() ?? [];
            foreach (var letter in letters) index = checked(index * 26 + char.ToUpperInvariant(letter) - 'A' + 1);
            index = letters.Length == 0 ? next : index - 1;
            if (index >= 32 || !result.TryAdd(index, "")) throw Bad("Sheet có quá nhiều cột hoặc cột trùng.");
            next = index + 1;
            var text = cell.DataType?.Value == CellValues.InlineString ? cell.InlineString?.InnerText ?? "" : cell.CellValue?.Text ?? "";
            if (cell.DataType?.Value == CellValues.SharedString)
                text = int.TryParse(text, out var id) && id >= 0 && id < strings.Length ? strings[id] : throw Bad("Shared string không hợp lệ.");
            if (text.Length > 2000) throw Bad("Giá trị ô quá dài.");
            result[index] = text.Trim();
        }
        return result;
    }

    private static LuxMapException Bad(string message) => new(ErrorCodes.ValidationFailed, HttpStatusCode.BadRequest, message);
}
