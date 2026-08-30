using LuxMap.Shared.Modularity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LuxMap.Modules.Faults;

/// <summary>
/// Faults module — Fault, FaultType, FaultHistory, status workflow and clustering (BE-18..BE-20, BE-41).
/// Empty shell as of BE-01: no entities, no endpoints yet.
/// </summary>
public sealed class FaultsModule : ILuxMapModule
{
    public string Name => "Faults";

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
    }
}
