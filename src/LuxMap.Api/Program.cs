using LuxMap.Modules.Admin;
using LuxMap.Modules.Assets;
using LuxMap.Modules.Faults;
using LuxMap.Modules.Identity;
using LuxMap.Modules.Survey;
using LuxMap.Modules.Telemetry;
using LuxMap.Modules.WorkOrders;
using LuxMap.Persistence;
using LuxMap.Shared.Modularity;
using LuxMap.Shared.Serialization;

// BE-03 — nạp .env ở thư mục gốc repo (đi ngược lên từ thư mục làm việc) để cổng và
// mật khẩu chỉ khai một chỗ, dùng chung với docker compose của BE-02.
DotNetEnv.Env.TraversePath().Load();

var builder = WebApplication.CreateBuilder(args);

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

// BE-03 — EF Core + Npgsql + NetTopologySuite, một DbContext dùng chung.
builder.Services.AddLuxMapPersistence(
    LuxMapConnectionString.FromEnvironment(),
    modules.Select(module => module.GetType().Assembly));

// Mỗi module tự đăng ký service của mình.
builder.Services.AddLuxMapModules(builder.Configuration, modules);

var app = builder.Build();

app.UseHttpsRedirection();

// BE-01 dựng khung: chưa module nào gắn endpoint.
app.MapLuxMapModules(modules);

app.Run();
