using LuxMap.Shared.Modularity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LuxMap.Modules.WorkOrders;

/// <summary>
/// Module WorkOrders — WorkOrder, ExternalUnit, RepairEvidence (BE-21..BE-24).
/// Khung rỗng ở BE-01: chưa có entity, chưa có endpoint.
/// </summary>
public sealed class WorkOrdersModule : ILuxMapModule
{
    public string Name => "WorkOrders";

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
    }
}
