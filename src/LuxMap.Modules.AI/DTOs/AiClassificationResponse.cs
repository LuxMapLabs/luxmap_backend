using LuxMap.Modules.AI.Detection;

namespace LuxMap.Modules.AI.DTOs;

/// <summary>Day/unknown decision; confidence is max(day, night) as a percentage from 0 to 100.</summary>
public sealed record AiClassificationResponse
{
    public required DayNightClassification Classification { get; init; }
    public required double ConfidencePercent { get; init; }
    public required string DaynightModelVersion { get; init; }
}
