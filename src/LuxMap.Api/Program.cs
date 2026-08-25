using LuxMap.Modules.Admin;
using LuxMap.Modules.Assets;
using LuxMap.Modules.Faults;
using LuxMap.Modules.Identity;
using LuxMap.Modules.Survey;
using LuxMap.Modules.Telemetry;
using LuxMap.Modules.WorkOrders;
using LuxMap.Shared.Modularity;

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

// Mỗi module tự đăng ký service của mình.
builder.Services.AddLuxMapModules(builder.Configuration, modules);

var app = builder.Build();

app.UseHttpsRedirection();

// BE-01 dựng khung: chưa module nào gắn endpoint.
app.MapLuxMapModules(modules);

app.Run();
