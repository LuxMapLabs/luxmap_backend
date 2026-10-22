using LuxMap.Modules.Faults;
using LuxMap.Shared.Modularity;
using LuxMap.Shared.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LuxMap.Modules.WorkOrders;

/// <summary>
/// WorkOrders module — WorkOrder, ExternalUnit, RepairEvidence (BE-21..BE-24).
/// Inspection and repair assignments (BE-23); evidence remains BE-24.
/// </summary>
public sealed class WorkOrdersModule : ILuxMapModule
{
    public string Name => "WorkOrders";

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<WorkOrderService>();
        services.AddScoped<WorkOrderPoleService>();
        var agenda = configuration.GetSection(WorkOrderAgendaOptions.SectionName).Get<WorkOrderAgendaOptions>() ?? new WorkOrderAgendaOptions();
        // An unknown time zone STOPS startup rather than putting every engineer on the wrong night.
        agenda.Validate();
        services.AddSingleton(agenda);
        services.AddScoped<WorkOrderAgendaService>();
        services.AddScoped<WorkOrderEvidenceService>();
        services.AddScoped<WorkOrderPolePhotoService>();
        services.AddScoped<IActiveWorkOrderLookup, ActiveWorkOrderLookup>();

        // BE-26 — runs under the scheduled job runner (LuxMap.Persistence.Jobs).
        services.AddSingleton<LuxMap.Persistence.Jobs.IScheduledJob, Jobs.WorkOrderOverdueJob>();

        // BE-35 — what these tables hold in the store, for the storage totals and the orphan check.
        services.AddSingleton(StoredObjectColumn.Original(StorageBucket.Evidence, "repair_evidence"));
        services.AddSingleton(StoredObjectColumn.Thumbnail(StorageBucket.Evidence, "repair_evidence"));
        services.AddSingleton(StoredObjectColumn.Original(StorageBucket.Evidence, "work_order_pole_photo"));
        services.AddSingleton(StoredObjectColumn.Thumbnail(StorageBucket.Evidence, "work_order_pole_photo"));
    }
}
