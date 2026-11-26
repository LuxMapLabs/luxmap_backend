using LuxMap.Modules.Assets;
using LuxMap.Shared.Modularity;
using LuxMap.Shared.Versions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LuxMap.Modules.IOT;

/// <summary>Cabinet devices, lighting, firmware, offline detection and Bridge diagnostics.</summary>
public sealed class IotModule : ILuxMapModule
{
    public string Name => "IOT";

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        // BE-34: the current firmware and its append-only change history remain versioned results.
        services.AddSingleton(new VersionedResultColumn("iot_node", "iot_node", "firmware_version_id", VersionReference.Id, "data_source"));
        services.AddSingleton(new VersionedResultColumn("iot_node_firmware_change", "iot_node_firmware", "firmware_version_id", VersionReference.Id));
        // BE-26: a silent device raises node_offline under the existing scheduled job runner.
        services.AddSingleton<LuxMap.Persistence.Jobs.IScheduledJob, Jobs.NodeOfflineJob>();
        services.AddSingleton<LuxMap.Persistence.Jobs.IScheduledJob, Jobs.IotDiagnosticJob>();
        services.AddScoped<IotDiagnosticSummary>();

        var lighting = configuration.GetSection(Lighting.LightingOptions.SectionName).Get<Lighting.LightingOptions>()
            ?? new Lighting.LightingOptions();
        lighting.Validate();
        services.AddSingleton(lighting);
        services.AddScoped<Lighting.LightingCommandService>();

        // LC-12: options are always bound because the broker callback reads them on either channel.
        // Complete MQTT transport configuration is required only when MQTT is the selected channel.
        // Keep the existing HTTP/MQTT selection while the Bridge cutover is specified separately.
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
        services.AddScoped<IFeederControlLookup, FeederControlLookup>();
        services.AddScoped<Registry.IotNodeRegistryService>();

        services.Configure<IotOptions>(configuration.GetSection("IotDiagnostics"));
        services.AddScoped<IotRegistrationStore>();
        services.AddSingleton<IotFeederSourceLookup>();
        services.AddSingleton<IFeederSourceLookup>(provider => provider.GetRequiredService<IotFeederSourceLookup>());
        services.AddScoped<IFeederHealthLookup>(provider => provider.GetRequiredService<IotDiagnosticsService>());
        services.AddScoped<IFeederPowerLookup>(provider => provider.GetRequiredService<IotDiagnosticsService>());
        services.AddHttpClient<IotDiagnosticsService>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(5);
            if (configuration["Mqtt:BridgeKey"] is { Length: >= 32 } key)
                client.DefaultRequestHeaders.Add("X-Iot-Bridge-Key", key);
        })
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false });
    }
}
