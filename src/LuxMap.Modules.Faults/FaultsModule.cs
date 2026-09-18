using LuxMap.Shared.Modularity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LuxMap.Modules.Faults;

/// <summary>
/// Faults module — Fault, FaultCluster, status workflow and clustering (BE-18..BE-20, BE-40, BE-41).
/// As of BE-18 it owns the <c>fault</c> and <c>fault_cluster</c> schema (no <c>FaultHistory</c>: the
/// audit trail sits on the <c>fault</c> row — see <c>Fault</c>). No endpoints yet: <c>GET /faults</c>
/// (BE-40), <c>PATCH /faults/{id}</c> (BE-19) and <c>POST /faults</c> (BE-41) are still to come.
/// </summary>
public sealed class FaultsModule : ILuxMapModule
{
    public string Name => "Faults";

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
    }
}
