using LuxMap.Shared.Modularity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LuxMap.Modules.Admin;

/// <summary>
/// Admin module — Catalogue, thresholds, nodes, model versions, dashboards (BE-28..BE-35).
/// Empty shell as of BE-01: no entities, no endpoints yet.
/// </summary>
public sealed class AdminModule : ILuxMapModule
{
    public string Name => "Admin";

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
    }
}
