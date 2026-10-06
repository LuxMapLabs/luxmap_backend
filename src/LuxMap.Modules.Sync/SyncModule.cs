using LuxMap.Shared.Modularity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LuxMap.Modules.Sync;

/// <summary>BE-43 — the offline bundle and the offline queue for field engineers.</summary>
public sealed class SyncModule : ILuxMapModule
{
    public string Name => "Sync";

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<SyncBundleService>();
        services.AddScoped<SyncPushService>();
    }
}
