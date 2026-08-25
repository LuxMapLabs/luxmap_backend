using LuxMap.Shared.Modularity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LuxMap.Modules.Telemetry;

/// <summary>
/// Module Telemetry — IotNode, TelemetryReading, ingest idempotent theo (node_id, reading_time).
/// Khung rỗng ở BE-01: chưa có entity, chưa có endpoint.
/// </summary>
public sealed class TelemetryModule : ILuxMapModule
{
    public string Name => "Telemetry";

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
    }
}
