using LuxMap.Modules.AI.Services;
using LuxMap.Shared.Modularity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LuxMap.Modules.AI;

/// <summary>
/// AI module — artificial intelligence and computer vision processing integration shell.
/// </summary>
public sealed class AIModule : ILuxMapModule
{
    public string Name => "AI";

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<YoloOnnxService>();
    }
}
