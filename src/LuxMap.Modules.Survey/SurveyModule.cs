using LuxMap.Modules.Survey.LuxReadings;
using LuxMap.Shared.Modularity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using LuxMap.Modules.Survey.Processing;
using LuxMap.Modules.Survey.Processing.Frames;
using Microsoft.Extensions.DependencyInjection;

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
        services.AddSingleton<IFrameExtractor, FfmpegFrameExtractor>();
        services.AddScoped<Review.SurveyReviewService>();
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
    }
}
