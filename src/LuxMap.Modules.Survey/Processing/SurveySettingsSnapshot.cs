using System.Text.Json.Nodes;

namespace LuxMap.Modules.Survey.Processing;

/// <summary>
/// Where <c>survey_processing_run.settings_snapshot</c> keeps the dim threshold the run classified with (BE-33). Runs before BE-33 wrote
/// it at the same path from appsettings, so one reader covers both.
/// </summary>
public static class SurveySettingsSnapshot
{
    public const string DimThresholdRatio = "dim_threshold_ratio";

    /// <summary>Null when the snapshot holds none — never a guess at what the threshold was.</summary>
    public static double? DimThresholdRatioOf(string snapshot)
        => JsonNode.Parse(snapshot)?["frames"]?[DimThresholdRatio] is JsonValue value && value.TryGetValue<double>(out var ratio)
            ? ratio : null;
}
