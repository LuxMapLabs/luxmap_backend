using LuxMap.Modules.Survey.LuxReadings;
using LuxMap.Shared.Modularity;
using Microsoft.Extensions.Configuration;
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
        services.AddScoped<LuxReadingService>();
    }
}
