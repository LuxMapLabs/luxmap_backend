using LuxMap.Shared.Modularity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LuxMap.Modules.Assets;

/// <summary>
/// Module Assets — Pole, Fixture, RoadSegment, Feeder, topology và truy vấn bbox (BE-09..BE-14).
/// Khung rỗng ở BE-01: chưa có entity, chưa có endpoint.
/// </summary>
public sealed class AssetsModule : ILuxMapModule
{
    public string Name => "Assets";

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
    }
}
