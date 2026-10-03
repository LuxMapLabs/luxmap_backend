using LuxMap.Shared.Contracts.Enums;

namespace LuxMap.Modules.Survey.Processing.Frames;

public sealed record Classification(FixtureStatus Status, double? BaselineRatio, bool DimEvaluationEligible, string[] Reasons);
public interface ISurveyBaselineLookup
{
    Task<double?> FindAsync(string poleId, CancellationToken ct);
}

// P2c owns baseline creation and compatibility rules; no fabricated baseline in P2b.
public sealed class EmptySurveyBaselineLookup : ISurveyBaselineLookup
{
    public Task<double?> FindAsync(string poleId, CancellationToken ct) => Task.FromResult<double?>(null);
}

public static class FrameClassification
{
    public static Classification Classify(Association association, double? peakLux, double? baseline,
        IReadOnlyCollection<string> flags, double dimThresholdRatio)
    {
        if (!double.IsFinite(dimThresholdRatio) || dimThresholdRatio <= 0 || dimThresholdRatio > 1)
            throw new ArgumentOutOfRangeException(nameof(dimThresholdRatio));
        if (peakLux is { } lux && (!double.IsFinite(lux) || lux < 0)) throw new ArgumentOutOfRangeException(nameof(peakLux));
        if (baseline is { } value && (!double.IsFinite(value) || value <= 0)) throw new ArgumentOutOfRangeException(nameof(baseline));
        if (flags.Contains("video_gap")) return new(FixtureStatus.Unknown, null, false, ["video_gap"]);
        if (association.State is null) return new(FixtureStatus.Unknown, null, false, [association.Reason]);
        if (association.State == "off") return new(FixtureStatus.Out, null, false, ["cv_off"]);
        var reasons = new List<string>();
        if (baseline is null) reasons.Add("baseline_missing");
        if (peakLux is null) reasons.Add("peak_missing");
        foreach (var flag in new[] { "peak_shared", "paired_poles", "lux_gap", "lux_saturated", "ambiguous_association" })
            if (flags.Contains(flag)) reasons.Add(flag);
        if (reasons.Count > 0) return new(FixtureStatus.Normal, null, false, reasons.ToArray());
        var ratio = peakLux!.Value / baseline!.Value;
        if (!double.IsFinite(ratio)) return new(FixtureStatus.Normal, null, false, ["ratio_not_finite"]);
        return new(ratio < dimThresholdRatio ? FixtureStatus.Dim : FixtureStatus.Normal, ratio, true, ["brightness_evaluated"]);
    }
}
