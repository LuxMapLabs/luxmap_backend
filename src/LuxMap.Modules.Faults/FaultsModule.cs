using LuxMap.Shared.Modularity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LuxMap.Modules.Faults;

/// <summary>
/// Module Faults — Fault, FaultType, FaultHistory, luồng trạng thái và clustering (BE-18..BE-20, BE-41).
/// Khung rỗng ở BE-01: chưa có entity, chưa có endpoint.
/// </summary>
public sealed class FaultsModule : ILuxMapModule
{
    public string Name => "Faults";

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
    }
}
