using LuxMap.Modules.Faults;
using LuxMap.Shared.Modularity;
using LuxMap.Shared.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using LuxMap.Shared.Versions;

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
        var dispatch = configuration.GetSection(AutoDispatchOptions.SectionName).Get<AutoDispatchOptions>() ?? new AutoDispatchOptions();
        dispatch.Validate();
        services.AddSingleton(dispatch);
        services.AddScoped<AutoDispatchQueries>();
        services.AddScoped<WorkOrderService>();
        services.AddScoped<SurveyRecurringService>();
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
        services.AddSingleton<LuxMap.Persistence.Jobs.IScheduledJob, Jobs.SurveyRecurringGenerateJob>();

        // BE-35 — what these tables hold in the store, for the storage totals and the orphan check.
        services.AddSingleton(StoredObjectColumn.Original(StorageBucket.Evidence, "repair_evidence"));
        services.AddSingleton(StoredObjectColumn.Thumbnail(StorageBucket.Evidence, "repair_evidence"));
        services.AddSingleton(StoredObjectColumn.Original(StorageBucket.Evidence, "work_order_pole_photo"));
        services.AddSingleton(StoredObjectColumn.Thumbnail(StorageBucket.Evidence, "work_order_pole_photo"));
        // POLE-VIDEO D-1: the clip lives in the video bucket, named by the same row's video_* columns.
        services.AddSingleton(new StoredObjectColumn(StorageBucket.Video, "work_order_pole_photo", "video_object_key", "video_bytes"));

        // BE-34 — the AI hint on a pole photo, by its bare model version. The row's artifact_version is the same version with
        // "+{rules}" behind it: counting it too would count each photo twice.
        services.AddSingleton(new VersionedResultColumn("pole_photo", "work_order_pole_photo", "model_version", VersionReference.Text));
        // POLE-VIDEO: the extractor that cut the frames of a clip.
        services.AddSingleton(new VersionedResultColumn("pole_video_extractor", "work_order_pole_photo", "video_extractor_version_id", VersionReference.Id, "data_source"));
    }
}
