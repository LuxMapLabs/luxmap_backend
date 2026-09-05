using LuxMap.Modules.Assets.Crud;
using LuxMap.Modules.Assets.Import;
using LuxMap.Shared.Modularity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LuxMap.Modules.Assets;

/// <summary>
/// Assets module — Pole, Fixture, RoadSegment, Feeder, topology and bbox queries (BE-09..BE-14).
/// </summary>
/// <remarks>
/// As of BE-12a it owns asset CRUD and bulk import. The bbox and topology endpoints of Contract
/// sections 2.1 and 2.3 arrive with BE-13 and BE-14, on their own routes — see the note on
/// <see cref="AssetsController"/> about why inventory management lives under <c>/assets/</c>.
/// </remarks>
public sealed class AssetsModule : ILuxMapModule
{
    public string Name => "Assets";

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<AssetCrudService>();
        services.AddScoped<AssetImportService>();
    }
}
