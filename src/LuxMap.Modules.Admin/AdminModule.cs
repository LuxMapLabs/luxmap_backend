using LuxMap.Shared.Modularity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LuxMap.Modules.Admin;

/// <summary>
/// Admin module — Catalogue, thresholds, nodes, model versions, dashboards (BE-28..BE-35).
/// First endpoints: communes and their map frames (COMMUNE-BBOX, <c>/admin/communes</c>).
/// </summary>
public sealed class AdminModule : ILuxMapModule
{
    public string Name => "Admin";

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<Communes.CommuneAdminService>();
    }
}
