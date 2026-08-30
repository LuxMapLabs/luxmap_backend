using LuxMap.Shared.Modularity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LuxMap.Modules.Telemetry;

/// <summary>
/// Telemetry module — IotNode, TelemetryReading, ingest idempotent on (node_id, reading_time).
/// Empty shell as of BE-01: no entities, no endpoints yet.
/// </summary>
public sealed class TelemetryModule : ILuxMapModule
{
    public string Name => "Telemetry";

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
    }
}
