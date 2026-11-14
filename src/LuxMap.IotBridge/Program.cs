using System.Text.Json;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<DeviceStore>();
builder.Services.AddSingleton<GatewayCredentials>();
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
if (app.Environment.IsDevelopment())
{
app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "LuxMap API v1");
    options.DocumentTitle = "LuxMap - Điều khiển ESP32";
});

}

app.Use(async (context, next) =>
{
    var path = context.Request.Path;
    if ((path.StartsWithSegments("/api/luxmap/devices") ||
         path.StartsWithSegments("/api/luxmap/gateways")) &&
        !GatewayCredentials.MatchesAdminKey(app.Configuration["Mqtt:BridgeKey"],
            context.Request.Headers["X-Iot-Bridge-Key"].ToString()))
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return;
    }
    await next(context);
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
    if (request.AutoSchedule is { } schedule && (mode != "AUTO" || !schedule.IsValid()))
        return Results.BadRequest(new { error = "AUTO schedule needs two distinct minutes between 0 and 1439." });
    if (request.CommandId is not null && !Guid.TryParseExact(request.CommandId, "N", out _))
        return Results.BadRequest(new { error = "Invalid command ID." });
    var desired = new LampCommand(mode, mode == "AUTO" ? null : command,
        CommandId: request.CommandId, AutoSchedule: request.AutoSchedule);
    try
    {
        var saved = await mqtt.SetCommandAsync(deviceId, desired, cancellationToken);
        return Results.Ok(saved);
    }
    catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
}).WithSummary("Điều khiển đèn: ON / OFF / AUTO")
  .WithDescription("Nhập deviceId LUXMAP-P001, chọn ví dụ ON, OFF hoặc AUTO, bấm Try it out và Execute. " +
      "MANUAL giữ trạng thái bật/tắt. Lịch AUTO mặc định dùng cảm biến từ 06:00 đến hết 18:00. " +
      "Dưới 50 lux bật, trên 50 lux tắt sau một mẫu, đúng 50 lux giữ trạng thái. " +
      "Từ 18:01 đến 05:59, AUTO luôn bật. Gửi autoSchedule với dayStartMinute và nightStartMinute " +
      "(phút tính từ 00:00 theo giờ RTC của node) để đổi lịch. API LuxMap dùng auto_schedule theo định dạng HH:mm.")
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
app.MapGet("/api/luxmap/gateways/routes", (HttpContext context, MqttBroker mqtt,
    GatewayCredentials credentials, IConfiguration configuration) =>
{
    if (!GatewayCredentials.MatchesAdminKey(configuration["Mqtt:ProvisioningKey"],
        context.Request.Headers["X-Iot-Provisioning-Key"].ToString())) return Results.Unauthorized();
    return Results.Ok(mqtt.Gateways.OrderBy(route => route.Key, StringComparer.Ordinal)
        .Select(route => new { gatewayId = route.Key, deviceIds = route.Value, credentialIssued = credentials.IsIssued(route.Key) })
        .ToArray());
}).WithTags("MQTT");

app.MapPut("/api/luxmap/gateways/{gatewayId}/route", (string gatewayId, GatewayRouteRequest request,
    HttpContext context, MqttBroker mqtt, IConfiguration configuration) =>
{
    if (!GatewayCredentials.MatchesAdminKey(configuration["Mqtt:ProvisioningKey"],
        context.Request.Headers["X-Iot-Provisioning-Key"].ToString())) return Results.Unauthorized();
    if (!DeviceRules.ValidId(gatewayId) || request.DeviceId is null || !DeviceRules.ValidId(request.DeviceId))
        return Results.BadRequest();
    return mqtt.RegisterGateway(gatewayId, request.DeviceId) ? Results.NoContent() : Results.Conflict();
}).WithTags("MQTT");
app.MapGet("/api/luxmap/gateways/by-device/{deviceId}/authorization", (string deviceId,
    HttpContext context, MqttBroker mqtt, GatewayCredentials credentials, IConfiguration configuration) =>
{
    if (!GatewayCredentials.MatchesAdminKey(configuration["Mqtt:ProvisioningKey"],
        context.Request.Headers["X-Iot-Provisioning-Key"].ToString())) return Results.Unauthorized();
    var authorized = mqtt.Gateways.Any(route => route.Value.Contains(deviceId) && credentials.IsIssued(route.Key));
    return authorized ? Results.NoContent() : Results.NotFound();
}).WithTags("MQTT");

app.MapPost("/api/luxmap/gateways/{gatewayId}/credential", (string gatewayId, HttpContext context,
    MqttBroker mqtt, GatewayCredentials credentials, IConfiguration configuration) =>
{
    context.Response.Headers.CacheControl = "no-store";
    if (!GatewayCredentials.MatchesAdminKey(configuration["Mqtt:ProvisioningKey"],
        context.Request.Headers["X-Iot-Provisioning-Key"].ToString())) return Results.Unauthorized();
    if (!mqtt.Gateways.TryGetValue(gatewayId, out var nodes)) return Results.NotFound();
    var secret = credentials.Issue(gatewayId);
    if (secret is null) return Results.Conflict(new { error = "Credential already issued." });
    return Results.Ok(new { clientId = gatewayId, username = gatewayId, password = secret, nodeIds = nodes });
}).WithTags("MQTT").WithSummary("Cấp secret một lần cho gateway đã đăng ký");
app.Run();

record GatewayRouteRequest(string? DeviceId);
