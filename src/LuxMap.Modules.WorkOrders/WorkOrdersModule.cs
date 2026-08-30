using LuxMap.Shared.Modularity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LuxMap.Modules.WorkOrders;

/// <summary>
/// WorkOrders module — WorkOrder, ExternalUnit, RepairEvidence (BE-21..BE-24).
/// Empty shell as of BE-01: no entities, no endpoints yet.
/// </summary>
public sealed class WorkOrdersModule : ILuxMapModule
{
    public string Name => "WorkOrders";

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
    }
}
