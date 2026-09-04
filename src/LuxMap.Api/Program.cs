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
using LuxMap.Infrastructure.Storage;
using LuxMap.Persistence;
using Serilog;
using LuxMap.Shared.Modularity;
using LuxMap.Shared.Serialization;

// BE-03 — load the repository-root .env (walking up from the working directory) so the port and
// password are declared once and shared with the BE-02 docker compose stack.
DotNetEnv.Env.TraversePath().Load();

// BE-05 — a temporary logger so a startup crash still records why.
SerilogSetup.CreateBootstrapLogger();

try
{

var builder = WebApplication.CreateBuilder(args);

// BE-05 — Serilog replaces the default logging; sink configuration lives in appsettings.
builder.UseLuxMapSerilog();

// The monolith's module list. Listed explicitly rather than discovered by assembly scanning:
// the registration order stays visible, and adding a module is a one-line change right here.
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

// BE-00 — Contract v1.1 section 0 JSON conventions (snake_case, lowercase string enums, ISO 8601 UTC with Z).
builder.Services.AddLuxMapJsonConventions();

// BE-04 — /api/v1 versioning, correlation id, and mapping validation failures onto the Contract shape.
builder.Services.AddLuxMapApiConventions();

// BE-05 — Swagger plus the JWT security scheme. Only enabled when Swagger:Enabled = true.
builder.Services.AddLuxMapSwagger();

// BE-08 — token validation, role policies, and the commune scoping of Contract section 7.
builder.Services.AddLuxMapAuthorization();

// BE-03 — EF Core + Npgsql + NetTopologySuite, one shared DbContext.
builder.Services.AddLuxMapPersistence(
    LuxMapConnectionString.FromEnvironment(),
    modules.Select(module => module.GetType().Assembly));

// BE-11 — MinIO object storage. Registered by the host beside persistence rather than through a
// module: Survey (BE-15) and WorkOrders (BE-24) both use it, so it belongs to neither.
// Fails fast on missing configuration only; it does not contact MinIO at startup.
builder.Services.AddLuxMapObjectStorage(StorageOptions.FromEnvironment());

// Each module registers its own services.
builder.Services.AddLuxMapModules(builder.Configuration, modules);

var app = builder.Build();

// BE-06 — `dotnet run -- --seed`: seed, then exit without building the HTTP pipeline.
if (SeedCommand.IsRequested(args))
{
    return await SeedCommand.RunAsync(app);
}

// The order is mandatory:
//   1. CorrelationIdMiddleware pushes CorrelationId onto LogContext
//   2. Serilog's request logging runs INSIDE that scope, so the summary line carries the id
//   3. the exception handler sits inside both, turning failures into a 500 response so Serilog
//      records the real status instead of logging the exception a second time
app.UseLuxMapCorrelationId();
app.UseLuxMapRequestLogging();
app.UseLuxMapErrorHandling();

app.UseLuxMapSwagger();
app.UseHttpsRedirection();

// Must sit INSIDE UseStatusCodePages (that is, after UseLuxMapErrorHandling), otherwise 401/403
// still return an empty body instead of the Contract's error shape.
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// Modules map their own endpoints; none has any at this stage.
app.MapLuxMapModules(modules);

app.Run();

return 0;

}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "LuxMap.Api terminated unexpectedly during startup");
    throw;
}
finally
{
    Log.CloseAndFlush();
}

// Lets WebApplicationFactory build this host from the test project.
public partial class Program;
