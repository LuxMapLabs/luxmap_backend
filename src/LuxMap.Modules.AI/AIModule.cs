using LuxMap.Modules.AI.Detection;
using LuxMap.Modules.Survey.Processing.Frames;
using LuxMap.Shared.Modularity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LuxMap.Modules.AI;

/// <summary>
/// AI module — the YOLO ON / OFF model (AI-1, SELF-SIGNED): the survey pipeline's detector when
/// <c>SurveyProcessing:Frames:Detector = yolo</c>, and <c>POST /ai/detect</c> for trying it by hand.
/// </summary>
/// <remarks>The model is loaded on first use, not at startup: a host that never detects never pays for it.</remarks>
public sealed class AIModule : ILuxMapModule
{
    public string Name => "AI";

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection(AiOptions.SectionName).Get<AiOptions>() ?? new AiOptions();
        options.Validate();

        services.AddSingleton(options);
        services.AddSingleton<YoloModel>();
        services.AddSingleton<YoloOnOffDetector>();
        services.AddKeyedSingleton<IOnOffDetector>(SurveyFrameOptions.YoloDetectorKey,
            (provider, _) => provider.GetRequiredService<YoloOnOffDetector>());
    }
}
