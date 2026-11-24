using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using LuxMap.Modules.IOT;
using LuxMap.Shared.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LuxMap.Api.Tests;

internal static class IotImportWorkbook
{
    internal static readonly string[] Headers = ["server_id", "feeder_id", "device_id", "nominal_voltage", "nominal_current_ma", "nominal_power_mw", "radius_km"];
    internal static byte[] Create(string[][] rows, string[]? headers = null, bool formula = false, bool sharedStrings = false)
    {
        using var stream = new MemoryStream();
        using (var package = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook, true))
        {
            var book = package.AddWorkbookPart();
            book.Workbook = new Workbook();
            var strings = book.AddNewPart<SharedStringTablePart>();
            strings.SharedStringTable = new SharedStringTable();
            var stringIndex = 0;
            var part = book.AddNewPart<WorksheetPart>();
            var data = new SheetData();
            var all = new[] { headers ?? Headers }.Concat(rows).ToArray();
            for (var index = 0; index < all.Length; index++)
            {
                var row = new Row { RowIndex = (uint)index + 1 };
                for (var col = 0; col < all[index].Length; col++)
                {
                    var cell = new Cell { CellReference = $"{(char)('A' + col)}{index + 1}", DataType = CellValues.InlineString,
                        InlineString = new InlineString(new Text(all[index][col])) };
                    if (sharedStrings)
                    {
                        strings.SharedStringTable.Append(new SharedStringItem(new Text(all[index][col])));
                        cell.InlineString = null;
                        cell.DataType = CellValues.SharedString;
                        cell.CellValue = new CellValue((stringIndex++).ToString(System.Globalization.CultureInfo.InvariantCulture));
                    }
                    if (index > 0 && col >= 3 && double.TryParse(all[index][col], System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var value) && double.IsFinite(value))
                    {
                        cell.InlineString = null;
                        cell.DataType = CellValues.Number;
                        cell.CellValue = new CellValue(all[index][col]);
                    }
                    if (formula && index == 1 && col == 3) cell.CellFormula = new CellFormula("2+3");
                    row.Append(cell);
                }
                data.Append(row);
            }
            part.Worksheet = new Worksheet(data);
            book.Workbook.Append(new Sheets(new Sheet { Name = "Sheet1", SheetId = 1, Id = book.GetIdOfPart(part) }));
            book.Workbook.Save();
        }
        return stream.ToArray();
    }

    internal static MultipartFormDataContent Upload(byte[] bytes, string name = "example.xlsx")
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        form.Add(file, "file", name);
        return form;
    }
}

public sealed class IotNodeImportReaderTests
{
    [Fact]
    public void Reads_example_columns_and_optional_radius_without_changing_units()
    {
        using var file = new MemoryStream(IotImportWorkbook.Create([
            ["iot-server-01", "FDR-009", "LUXMAP-P001", "5", "100", "500", "0.2"],
            ["iot-server-01", "FDR-010", "LUXMAP-P002", "5", "120", "600", ""],
            ["iot-server-02", "FDR-011", "LUXMAP-P003", "5", "150", "750", "0.3"],
        ], sharedStrings: true));
        var (sheet, rows) = IotNodeImportReader.Read(file);
        Assert.Equal("Sheet1", sheet);
        Assert.Equal(3, rows.Count);
        Assert.Equal(2, rows[0].Number);
        Assert.Equal("LUXMAP-P001", rows[0].Request.DeviceId);
        Assert.Equal(100, rows[0].Request.NominalCurrentMa);
        Assert.Equal(0.2, rows[0].Request.RadiusKm);
        Assert.Equal(0, rows[1].Request.RadiusKm);
        Assert.All(rows, row => Assert.Null(row.Error));
    }

    [Theory]
    [InlineData("0", "100", "0.2", "nominal_voltage")]
    [InlineData("5", "NaN", "0.2", "nominal_current_ma")]
    [InlineData("5", "93,55", "0.2", "nominal_current_ma")]
    [InlineData("5", "100", "1001", "radius_km")]
    public void Returns_number_errors_with_excel_row_and_column(string voltage, string current, string radius, string field)
    {
        using var file = new MemoryStream(IotImportWorkbook.Create([["server", "FDR-1", "P1", voltage, current, "500", radius]]));
        var row = Assert.Single(IotNodeImportReader.Read(file).Rows);
        Assert.Equal(2, row.Number);
        Assert.Equal(field, row.Error!.Field);
    }

    [Fact]
    public void Rejects_formulas_bad_headers_unreadable_files_and_more_than_100_rows()
    {
        string[] values = ["server", "FDR-1", "P1", "5", "100", "500", "0"];
        using var formula = new MemoryStream(IotImportWorkbook.Create([values], formula: true));
        Assert.NotNull(Assert.Single(IotNodeImportReader.Read(formula).Rows).Error);
        using var headers = new MemoryStream(IotImportWorkbook.Create([values], headers: ["unknown"]));
        Assert.Throws<LuxMapException>(() => IotNodeImportReader.Read(headers));
        using var corrupt = new MemoryStream([1, 2, 3]);
        Assert.Throws<LuxMapException>(() => IotNodeImportReader.Read(corrupt));
        using var tooMany = new MemoryStream(IotImportWorkbook.Create(Enumerable.Repeat(values, 101).ToArray()));
        Assert.Throws<LuxMapException>(() => IotNodeImportReader.Read(tooMany));
    }
}

public sealed partial class IotDiagnosticsTests
{
    [Fact]
    public async Task Excel_import_requires_admin_reports_each_row_and_can_be_retried()
    {
        var bridge = new RegistrationBridge();
        using var factory = new LuxMapApiFactory();
        using var host = factory.WithWebHostBuilder(builder => {
            builder.UseSetting("IotDiagnostics:BaseUrl", "https://registration.example.test");
            builder.UseSetting("IotDiagnostics:AllowedOrigins:0", "https://registration.example.test");
            builder.UseSetting("Mqtt:ProvisioningKey", new string('a', 32));
            builder.ConfigureServices(services => services.AddHttpClient<IotDiagnosticsService>().ConfigurePrimaryHttpMessageHandler(() => bridge));
        });
        using var adminToken = await fixture.SeededClientAsync("admin", "SEED_ADMIN_PASSWORD");
        using var managerToken = await fixture.ManagerClientAsync();
        using var admin = AuthorizedClient(host.CreateClient(), adminToken);
        using var manager = AuthorizedClient(host.CreateClient(), managerToken);
        var setup = new LightingTestRig(fixture);
        var cabinet = await setup.NewCabinetAsync();
        var first = await setup.NewFeederAsync(cabinet);
        var second = await setup.NewFeederAsync(cabinet);
        var server = "import-" + Guid.NewGuid().ToString("N");
        bridge.ServerId = server;
        Assert.Equal(HttpStatusCode.Created, (await admin.PostAsJsonAsync("/api/v1/iot/servers", new {
            server_id = server, commune_id = fixture.CommuneId, location = new { lat = 13.5, lng = 109.5 },
        })).StatusCode);
        string[] Row(string feeder, string device) => [server, feeder, device, "5", "100", "500", "0.2"];
        var devicePrefix = "import-" + Guid.NewGuid().ToString("N");
        var d1 = devicePrefix + "-1";
        var d2 = devicePrefix + "-2";
        var bytes = IotImportWorkbook.Create([Row(first, d1), Row("FDR-missing", devicePrefix + "-3"), Row(second, d2), Row(first, devicePrefix + "-4")]);
        const string path = "/api/v1/iot/nodes/import";
        using (var deniedFile = IotImportWorkbook.Upload(bytes))
            Assert.Equal(HttpStatusCode.Forbidden, (await manager.PostAsync(path, deniedFile)).StatusCode);
        using var upload = IotImportWorkbook.Upload(bytes);
        using var response = await admin.PostAsync(path, upload);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(4, result.GetProperty("total").GetInt32());
        Assert.True(result.GetProperty("created").GetInt32() == 2, result.GetRawText());
        Assert.Equal(2, result.GetProperty("failed").GetInt32());
        var items = result.GetProperty("items");
        Assert.Equal(3, items[1].GetProperty("row").GetInt32());
        Assert.Equal("ASSET_NOT_FOUND", items[1].GetProperty("error").GetProperty("code").GetString());
        Assert.Equal("VALIDATION_FAILED", items[3].GetProperty("error").GetProperty("code").GetString());
        Assert.Equal(new[] { d1, d2 }, bridge.Devices);
        using var retry = IotImportWorkbook.Upload(IotImportWorkbook.Create([Row(first, d1), Row(second, d2)]));
        var retried = await (await admin.PostAsync(path, retry)).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, retried.GetProperty("created").GetInt32());
        Assert.Equal(2, retried.GetProperty("existing").GetInt32());
        Assert.Equal(items[0].GetProperty("node_id").GetString(), retried.GetProperty("items")[0].GetProperty("node_id").GetString());
        Assert.Equal(2, await fixture.QueryAsync(db => db.Set<LuxMap.Modules.IOT.Entities.IotNode>().IgnoreQueryFilters().CountAsync(n => n.CabinetId == cabinet)));
        using var invalid = IotImportWorkbook.Upload([1, 2, 3]);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsync(path, invalid)).StatusCode);
        using var wrongType = IotImportWorkbook.Upload(bytes, "example.csv");
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, (await admin.PostAsync(path, wrongType)).StatusCode);
    }
}
