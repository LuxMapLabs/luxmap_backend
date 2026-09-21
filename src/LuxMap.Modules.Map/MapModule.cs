using LuxMap.Modules.Map.Features;
using LuxMap.Shared.Modularity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LuxMap.Modules.Map;

/// <summary>
/// Map module — the bbox endpoints the web and mobile maps draw from (BE-14, later BE-20).
/// </summary>
/// <remarks>
/// <para>
/// It owns NO table. Every row it returns belongs to another module; what lives here is the
/// composition, because a map feature is a pole joined to its lamp, its status and its open fault
/// count — three modules for one <c>properties</c> block.
/// </para>
/// <para>
/// ⚠️ <b>Not the same surface as <c>/assets/…</c>.</b> Those are the inventory endpoints (BE-12a):
/// paginated JSON, written to by administrators. These are read-only map layers keyed by
/// <c>bbox</c>, and Contract section 5.1 already spells them out. Two surfaces, two consumers; a
/// stocktake list answering <c>/poles</c> would take the map's route.
/// </para>
/// </remarks>
public sealed class MapModule : ILuxMapModule
{
    public string Name => "Map";

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<MapQueryService>();
    }
}
