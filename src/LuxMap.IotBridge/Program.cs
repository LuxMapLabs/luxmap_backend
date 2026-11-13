using System.Text.Json;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<DeviceStore>();
builder.Services.AddSingleton<MqttBroker>();
builder.Services.AddHostedService(provider => provider.GetRequiredService<MqttBroker>());
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "LuxMap API",
        Version = "v1",
        Description = "Nhận dữ liệu ESP32 và điều khiển đèn. Chọn POST command để bật, tắt hoặc AUTO. " +
            "Lệnh được chuyển qua MQTT; phản hồi 200 xác nhận server lưu và phát lệnh, chưa xác nhận relay."
    });
    options.OperationFilter<CommandExamples>();
});
var app = builder.Build();
// Available for the normal `dotnet run` command, including Production on the LAN.
app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "LuxMap API v1");
    options.DocumentTitle = "LuxMap - Điều khiển ESP32";
});

app.MapGet("/", () => Results.Ok(new { service = "LuxMap API", status = "running" }));
var api = app.MapGroup("/api/luxmap/devices").WithTags("LuxMap devices");
api.AddEndpointFilter(async (context, next) =>
{
    if (context.HttpContext.Request.RouteValues["deviceId"] is string id &&
        !DeviceRules.ValidId(id))
        return Results.BadRequest(new { error = "deviceId: 1-64 characters, letters, digits, '-' or '_'." });
    return await next(context);
});

api.MapGet("/", (DeviceStore store) => Results.Ok(store.ReadAll()))
    .WithSummary("Danh sách thiết bị").Produces<DeviceState[]>();
api.MapGet("/{deviceId}", (string deviceId, DeviceStore store) =>
    store.Read(deviceId) is { } device ? Results.Ok(device) : Results.NotFound())
    .WithSummary("Xem trạng thái và dữ liệu thiết bị").Produces<DeviceState>().Produces(404);
// An unknown device starts in AUTO, matching the firmware's startup mode.
api.MapGet("/{deviceId}/command", (string deviceId, DeviceStore store) =>
    Results.Ok(store.Read(deviceId)?.DesiredCommand ?? new LampCommand("AUTO", null, "initial-auto", 0)))
    .WithSummary("Đọc lệnh đang chờ ESP32 thực hiện").Produces<LampCommand>();

api.MapPost("/{deviceId}/command", async (string deviceId, CommandRequest request, MqttBroker mqtt, CancellationToken cancellationToken) =>
{
    var mode = request.Mode?.Trim().ToUpperInvariant();
    var command = request.Command?.Trim().ToUpperInvariant();
    if (mode != "AUTO" && mode != "MANUAL")
        return Results.BadRequest(new { error = "mode must be AUTO or MANUAL." });
    if (mode == "MANUAL" && command != "ON" && command != "OFF")
        return Results.BadRequest(new { error = "MANUAL requires command ON or OFF." });
    var desired = new LampCommand(mode, mode == "AUTO" ? null : command);
    try
    {
        var saved = await mqtt.SetCommandAsync(deviceId, desired, cancellationToken);
        return Results.Ok(saved);
    }
    catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
}).WithSummary("Điều khiển đèn: ON / OFF / AUTO")
  .WithDescription("Nhập deviceId LUXMAP-P001, chọn ví dụ ON, OFF hoặc AUTO, bấm Try it out và Execute. " +
      "MANUAL giữ trạng thái bật/tắt. AUTO từ 06:00 đến hết 18:00: dưới 50 lux bật, trên 50 lux tắt sau một mẫu; " +
      "đúng 50 lux giữ trạng thái. Từ 18:01 đến 05:59, AUTO luôn bật.")
  .Produces<LampCommand>().Produces(400);

api.MapGet("/{deviceId}/telemetry", (string deviceId, int? limit, DeviceStore store) =>
{
    if (limit is < 1 or > 1000)
        return Results.BadRequest(new { error = "limit must be between 1 and 1000." });
    var device = store.Read(deviceId);
    return device is null ? Results.NotFound() : Results.Ok(device.History.TakeLast(limit ?? 100));
}).WithSummary("Đọc lịch sử cảm biến (limit từ 1 đến 1000)")
  .Produces<IEnumerable<TelemetryReading>>().Produces(400).Produces(404);

api.MapGet("/{deviceId}/telemetry/history", (string deviceId, long? after, int? limit, DeviceStore store) =>
{
    if (after is < 0 || limit is < 1 or > 1000)
        return Results.BadRequest(new { error = "after must be nonnegative and limit must be between 1 and 1000." });
    if (store.Read(deviceId) is null) return Results.NotFound();
    return Results.Ok(store.ReadTelemetryHistory(deviceId, after ?? 0, limit ?? 100));
}).WithSummary("Đọc dữ liệu đo đã lưu, dùng nextCursor làm after để lấy mẫu tiếp theo")
  .Produces<TelemetryPage>().Produces(400).Produces(404);

app.MapGet("/api/luxmap/mqtt", (MqttBroker mqtt) => Results.Ok(new { running = mqtt.IsRunning, transport = "MQTT TCP" })).WithTags("MQTT").WithSummary("Trạng thái MQTT broker");

app.MapGet("/api/luxmap/gateways/by-device/{deviceId}/status", (string deviceId, MqttBroker mqtt) =>
    Results.Ok(new { status = mqtt.GatewayStatus(deviceId) })).WithTags("MQTT");

app.MapGet("/api/luxmap/gateways", (MqttBroker mqtt) => Results.Ok(mqtt.Gateways))
    .WithTags("MQTT").WithSummary("Danh sách gateway và node được phép chuyển tiếp");
app.Run();
