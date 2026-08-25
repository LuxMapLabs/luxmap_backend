using LuxMap.Shared.Modularity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LuxMap.Modules.Survey;

/// <summary>
/// Module Survey — SurveySweep, SurveyFrame, Detection, LuminanceBaseline, LuxReading (BE-15..BE-17, BE-42).
/// Khung rỗng ở BE-01: chưa có entity, chưa có endpoint.
/// </summary>
public sealed class SurveyModule : ILuxMapModule
{
    public string Name => "Survey";

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
    }
}
