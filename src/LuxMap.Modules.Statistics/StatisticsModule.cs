using LuxMap.Shared.Modularity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LuxMap.Modules.Statistics;

/// <summary>BE-28 — dashboard statistics. Read-only: it owns no table.</summary>
public sealed class StatisticsModule : ILuxMapModule
{
    public string Name => "Statistics";

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<StatisticsService>();
    }
}
