using LuxMap.Modules.Faults;
using LuxMap.Shared.Modularity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LuxMap.Modules.WorkOrders;

/// <summary>
/// WorkOrders module — WorkOrder, ExternalUnit, RepairEvidence (BE-21..BE-24).
/// Inspection and repair assignments (BE-23); evidence remains BE-24.
/// </summary>
public sealed class WorkOrdersModule : ILuxMapModule
{
    public string Name => "WorkOrders";

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<WorkOrderService>();
        services.AddScoped<WorkOrderPoleService>();
        services.AddScoped<IActiveWorkOrderLookup, ActiveWorkOrderLookup>();
    }
}
