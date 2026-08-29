using LuxMap.Modules.Admin;
using LuxMap.Modules.Assets;
using LuxMap.Modules.Faults;
using LuxMap.Modules.Identity;
using LuxMap.Modules.Survey;
using LuxMap.Modules.Telemetry;
using LuxMap.Modules.WorkOrders;
using LuxMap.Api.Authorization;
using LuxMap.Api.Http;
using LuxMap.Api.Observability;
using LuxMap.Api.OpenApi;
using LuxMap.Api.Seeding;
using LuxMap.Persistence;
using Serilog;
using LuxMap.Shared.Modularity;
using LuxMap.Shared.Serialization;

// BE-03 — nạp .env ở thư mục gốc repo (đi ngược lên từ thư mục làm việc) để cổng và
// mật khẩu chỉ khai một chỗ, dùng chung với docker compose của BE-02.
DotNetEnv.Env.TraversePath().Load();

// BE-05 — logger tạm để nếu host chết lúc khởi động thì vẫn ghi lại được lý do.
SerilogSetup.CreateBootstrapLogger();

try
{

var builder = WebApplication.CreateBuilder(args);

// BE-05 — Serilog thay logging mặc định; cấu hình sink nằm trong appsettings.
builder.UseLuxMapSerilog();

// Danh sách module của monolith. Liệt kê tường minh thay vì quét assembly:
// thứ tự đăng ký nhìn thấy được, và thêm module là sửa đúng một dòng ở đây.
ILuxMapModule[] modules =
[
    new IdentityModule(),
    new AssetsModule(),
    new SurveyModule(),
    new FaultsModule(),
    new WorkOrdersModule(),
    new TelemetryModule(),
    new AdminModule(),
];

// BE-00 — quy ước JSON của Contract v1.1 mục 0 (snake_case, enum chuỗi thường, ISO 8601 UTC hậu tố Z).
builder.Services.AddLuxMapJsonConventions();

// BE-04 — versioning /api/v1, correlation id, và map lỗi validation về hình dạng của Contract.
builder.Services.AddLuxMapApiConventions();

// BE-05 — Swagger + security scheme JWT. Chỉ bật khi Swagger:Enabled = true.
builder.Services.AddLuxMapSwagger();

// BE-08 — kiểm token, policy theo vai trò, và phạm vi địa bàn của Contract mục 7.
builder.Services.AddLuxMapAuthorization();

// BE-03 — EF Core + Npgsql + NetTopologySuite, một DbContext dùng chung.
builder.Services.AddLuxMapPersistence(
    LuxMapConnectionString.FromEnvironment(),
    modules.Select(module => module.GetType().Assembly));

// Mỗi module tự đăng ký service của mình.
builder.Services.AddLuxMapModules(builder.Configuration, modules);

var app = builder.Build();

// BE-06 — `dotnet run -- --seed`: chạy seed rồi thoát, không dựng pipeline HTTP.
if (SeedCommand.IsRequested(args))
{
    return await SeedCommand.RunAsync(app);
}

// Thứ tự bắt buộc:
//   1. CorrelationIdMiddleware đẩy CorrelationId vào LogContext
//   2. request logging của Serilog chạy BÊN TRONG scope đó nên dòng tổng kết mang id
//   3. exception handler nằm trong cùng, để lỗi thành response 500 rồi Serilog ghi
//      status thật thay vì tự log trùng một lần nữa
app.UseLuxMapCorrelationId();
app.UseLuxMapRequestLogging();
app.UseLuxMapErrorHandling();

app.UseLuxMapSwagger();
app.UseHttpsRedirection();

// Phải nằm TRONG UseStatusCodePages (tức sau UseLuxMapErrorHandling), nếu không 401/403
// vẫn ra body rỗng thay vì hình dạng lỗi của Contract.
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// Module gắn endpoint của mình; chưa module nào có endpoint ở giai đoạn này.
app.MapLuxMapModules(modules);

app.Run();

return 0;

}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "LuxMap.Api dừng bất thường lúc khởi động");
    throw;
}
finally
{
    Log.CloseAndFlush();
}

// Cho WebApplicationFactory trong test dựng được host này.
public partial class Program;
