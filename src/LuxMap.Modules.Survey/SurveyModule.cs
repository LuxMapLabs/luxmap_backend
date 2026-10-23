using LuxMap.Modules.Survey.LuxReadings;
using LuxMap.Shared.Modularity;
using LuxMap.Shared.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using LuxMap.Modules.Survey.Processing;
using LuxMap.Modules.Survey.Processing.Frames;
using Microsoft.Extensions.DependencyInjection;
using LuxMap.Shared.Versions;

namespace LuxMap.Modules.Survey;

/// <summary>
/// Survey module — SurveySweep, SurveyFrame, Detection, LuminanceBaseline, LuxReading (BE-15..BE-17, BE-42).
/// </summary>
/// <remarks>
/// As of BE-42 it owns <c>LuxReading</c> and the Contract section 2.9 endpoints. Everything else in
/// the list arrives with BE-15..BE-17.
/// </remarks>
public sealed class SurveyModule : ILuxMapModule
{
    public string Name => "Survey";

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<Processing.SurveyProcessingOptions>()
            .Bind(configuration.GetSection("SurveyProcessing"))
            .Validate(o => o.IsValid(), "Invalid SurveyProcessing options.").ValidateOnStart();
        services.AddOptions<SurveyProcessingOptions>().Validate<IHostEnvironment>((o, environment) =>
        {
            o.Frames.ValidateEnvironment(environment.EnvironmentName);
            return true;
        }).ValidateOnStart();
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<SurveyProcessingOptions>>().Value.Frames);
        services.AddSingleton<LuxMap.Modules.Assets.ISurveyCorridor, SurveyCorridor>();
        services.AddSingleton<IFrameExtractor, FfmpegFrameExtractor>();
        services.AddScoped<Review.SurveyReviewService>();
        services.AddScoped<LuxMap.Modules.WorkOrders.IPolePhotoPublication, Review.ReviewedPolePhotoPublisher>();
        services.AddScoped<LuxMap.Modules.WorkOrders.ISurveyFindingLookup, Review.SurveyFindingLookup>();
        services.AddScoped<Review.SurveyMediaAccess>();
        services.AddOptions<Review.SurveyReviewOptions>().Bind(configuration.GetSection("SurveyReview"))
            .Validate(o => o.IsValid(), "Invalid SurveyReview options.").ValidateOnStart();
        services.AddSingleton<IOnOffDetector>(sp =>
        {
            var options = sp.GetRequiredService<SurveyFrameOptions>();
            options.ValidateEnvironment(sp.GetRequiredService<IHostEnvironment>().EnvironmentName);
            return options.Detector switch
            {
                "fake" => new ManifestOnOffDetector(File.ReadAllText(options.FakeManifestPath ?? throw new InvalidOperationException("FakeManifestPath is required."))),
                SurveyFrameOptions.YoloDetectorKey => sp.GetRequiredKeyedService<IOnOffDetector>(SurveyFrameOptions.YoloDetectorKey),
                _ => new UnconfiguredOnOffDetector(),
            };
        });
        services.AddSingleton<SurveyFramePipeline>();
        services.AddSingleton<Processing.SurveyProcessor>();
        services.AddHostedService<Processing.SurveyProcessingWorker>();
        services.AddScoped<LuxReadingService>();
        services.AddScoped<Ingest.SurveyIngestService>();

        // BE-35 — what these tables hold in the store, for the storage totals and the orphan check.
        services.AddSingleton(StoredObjectColumn.Original(StorageBucket.Survey, "survey_frame"));
        services.AddSingleton(StoredObjectColumn.Thumbnail(StorageBucket.Survey, "survey_frame"));
        services.AddSingleton(StoredObjectColumn.Original(StorageBucket.Video, "survey_video_clip"));
        services.AddSingleton(StoredObjectColumn.Original(StorageBucket.Video, "survey_raw_file"));

        // BE-34 — where these tables name the version behind a result, for GET /admin/artifact-versions/{id}/usage.
        foreach (var column in new[] { "algorithm_version_id", "clock_version_id", "model_version_id", "extractor_version_id", "classification_version_id" })
            services.AddSingleton(new VersionedResultColumn("survey_run", "survey_processing_run", column, VersionReference.Id));
        services.AddSingleton(new VersionedResultColumn("survey_frame", "survey_frame", "extractor_version_id", VersionReference.Id, "data_source"));
        services.AddSingleton(new VersionedResultColumn("detection", "detection", "model_version_id", VersionReference.Id));
        services.AddSingleton(new VersionedResultColumn("luminance_baseline", "luminance_baseline", "algorithm_version_id", VersionReference.Id, "data_source"));
        services.AddSingleton(VersionedResultColumn.Through("survey_pass", "survey_pass", "run_id", "survey_processing_run", "run_id"));
        services.AddSingleton(VersionedResultColumn.Through("pole_observation", "pole_observation", "run_id", "survey_processing_run", "run_id", "data_source"));
        services.AddSingleton(VersionedResultColumn.Through("luminance_history", "luminance_history", "run_id", "survey_processing_run", "run_id", "data_source"));
    }
}
