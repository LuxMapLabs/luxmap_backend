using LuxMap.Shared.Modularity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LuxMap.Modules.Admin;

/// <summary>
/// Admin module — Catalogue, thresholds, nodes, model versions, dashboards (BE-28..BE-35).
/// Communes and their map frames (COMMUNE-BBOX, <c>/admin/communes</c>); system monitoring (BE-35, <c>/admin/system/status</c>).
/// </summary>
public sealed class AdminModule : ILuxMapModule
{
    public string Name => "Admin";

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<Communes.CommuneAdminService>();

        // BE-35 — the monitoring page and the two storage jobs (the job runner is LuxMap.Persistence.Jobs).
        var monitoring = configuration.GetSection(Monitoring.MonitoringOptions.SectionName).Get<Monitoring.MonitoringOptions>()
            ?? new Monitoring.MonitoringOptions();
        monitoring.Validate();
        services.AddSingleton(monitoring);
        services.AddSingleton<Monitoring.IDiskProbe, Monitoring.DriveDiskProbe>();
        services.AddSingleton<Monitoring.StorageUsage>();
        services.AddScoped<Monitoring.SystemStatusService>();
        services.AddSingleton<LuxMap.Persistence.Jobs.IScheduledJob, Monitoring.StorageCapacityJob>();
        services.AddSingleton<LuxMap.Persistence.Jobs.IScheduledJob, Monitoring.StorageReconciliationJob>();
    }
}
