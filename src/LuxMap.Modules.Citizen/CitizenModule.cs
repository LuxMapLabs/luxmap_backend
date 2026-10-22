using LuxMap.Shared.Modularity;
using LuxMap.Shared.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LuxMap.Modules.Citizen;

/// <summary>CITIZEN-QR — QR codes on poles (P2a), the public report form (P2b), the Manager's review (P2c) (D-19).</summary>
public sealed class CitizenModule : ILuxMapModule
{
    public string Name => "Citizen";

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<QrCodeService>();

        // P2b — the public form.
        var options = configuration.GetSection(CitizenOptions.SectionName).Get<CitizenOptions>() ?? new CitizenOptions();
        options.Validate();
        services.AddSingleton(options);
        services.AddSingleton<CitizenScopeFactory>();
        services.AddScoped<PublicReportService>();

        // P2c — the Manager's review.
        services.AddScoped<CitizenReviewService>();
        services.AddSingleton<ICitizenPhotoHinter, YoloCitizenPhotoHinter>();
        services.AddSingleton<CitizenHintBackoff>();

        // BE-35 — what this table holds in the store, for the storage totals and the orphan check.
        services.AddSingleton(StoredObjectColumn.Original(StorageBucket.Citizen, "citizen_photo"));
        services.AddSingleton(StoredObjectColumn.Thumbnail(StorageBucket.Citizen, "citizen_photo"));
    }
}
