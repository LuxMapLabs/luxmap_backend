using LuxMap.Modules.Assets;
using Microsoft.Extensions.Options;

namespace LuxMap.Modules.Survey.Processing;

/// <summary>Answers <see cref="ISurveyCorridor"/> with the corridor <see cref="SurveyChainageQuery.Poles"/> filters on.</summary>
public sealed class SurveyCorridor(IOptions<SurveyProcessingOptions> options) : ISurveyCorridor
{
    public double RouteCorridorM => options.Value.RouteCorridorM;
}
