using LuxMap.Shared.Modularity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LuxMap.Modules.IOT;

public sealed class IotModule : ILuxMapModule
{
    public string Name => "IOT";

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<IotOptions>(configuration.GetSection("IotDiagnostics"));
        services.AddHttpClient<IotDiagnosticsService>(client => client.Timeout = TimeSpan.FromSeconds(5))
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
    }
}
