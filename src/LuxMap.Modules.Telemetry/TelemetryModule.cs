using LuxMap.Modules.Assets;
using LuxMap.Shared.Modularity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LuxMap.Modules.Telemetry;

/// <summary>
/// Telemetry module — the cabinet devices (<c>iot_node</c>) and which relay switches which feeder
/// (<c>feeder_control</c>), BE-14b. TelemetryReading and ingest (IOT-09/10) are still to come.
/// </summary>
public sealed class TelemetryModule : ILuxMapModule
{
    public string Name => "Telemetry";

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection(IotOptions.SectionName).Get<IotOptions>() ?? new IotOptions();

        // A non-positive threshold STOPS startup rather than marking every device offline.
        options.Validate();

        services.AddSingleton(options);

        var lighting = configuration.GetSection(Lighting.LightingOptions.SectionName).Get<Lighting.LightingOptions>()
            ?? new Lighting.LightingOptions();
        lighting.Validate();
        services.AddSingleton(lighting);
        services.AddScoped<Lighting.LightingCommandService>();

        // LC-12: the MQTT device channel. Options are always bound (the broker callback reads its key); the transport runs, and
        // the options must be complete, only when it is THE channel (M-10).
        var mqtt = configuration.GetSection(Mqtt.MqttOptions.SectionName).Get<Mqtt.MqttOptions>() ?? new Mqtt.MqttOptions();
        services.AddSingleton(mqtt);
        services.AddSingleton<Mqtt.LightingScopeFactory>();
        services.AddSingleton<Mqtt.MqttLightingHandler>();
        if (lighting.Channel == Lighting.LightingOptions.Mqtt)
        {
            mqtt.Validate();
            services.AddHostedService<Mqtt.MqttLightingChannel>();
        }

        if (mqtt.AdminUrl is not null)
        {
            services.AddHttpClient<Mqtt.IMqttBrokerAdmin, Mqtt.EmqxBrokerAdmin>(client => client.Timeout = TimeSpan.FromSeconds(5));
        }
        else
        {
            services.AddSingleton<Mqtt.IMqttBrokerAdmin, Mqtt.NoMqttBrokerAdmin>();
        }
        services.AddScoped<ICabinetDeviceLookup, CabinetDeviceLookup>();
        services.AddScoped<Registry.IotNodeRegistryService>();
    }
}
