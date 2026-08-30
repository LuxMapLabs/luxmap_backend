using LuxMap.Shared.Modularity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LuxMap.Modules.Assets;

/// <summary>
/// Assets module — Pole, Fixture, RoadSegment, Feeder, topology and bbox queries (BE-09..BE-14).
/// Empty shell as of BE-01: no entities, no endpoints yet.
/// </summary>
public sealed class AssetsModule : ILuxMapModule
{
    public string Name => "Assets";

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
    }
}
