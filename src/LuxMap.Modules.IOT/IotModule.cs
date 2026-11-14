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
        services.AddScoped<IotRegistrationStore>();
        services.AddSingleton<IotFeederSourceLookup>();
        services.AddSingleton<LuxMap.Modules.Telemetry.IFeederSourceLookup>(provider => provider.GetRequiredService<IotFeederSourceLookup>());
        services.AddScoped<LuxMap.Modules.Assets.IFeederPowerLookup>(provider => provider.GetRequiredService<IotDiagnosticsService>());
        services.AddHttpClient<IotDiagnosticsService>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(5);
            if (configuration["Mqtt:BridgeKey"] is { Length: >= 32 } key)
                client.DefaultRequestHeaders.Add("X-Iot-Bridge-Key", key);
        })
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false });
    }
}
