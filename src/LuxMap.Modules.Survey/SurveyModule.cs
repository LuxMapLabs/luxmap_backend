using LuxMap.Shared.Modularity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LuxMap.Modules.Survey;

/// <summary>
/// Survey module — SurveySweep, SurveyFrame, Detection, LuminanceBaseline, LuxReading (BE-15..BE-17, BE-42).
/// Empty shell as of BE-01: no entities, no endpoints yet.
/// </summary>
public sealed class SurveyModule : ILuxMapModule
{
    public string Name => "Survey";

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
    }
}
