using System.Text.Json;
using LuxMap.Modules.Survey.Entities;
using LuxMap.Shared.Contracts.Enums;

namespace LuxMap.Modules.Survey.Review;

/// <summary>Provisional pilot values; BE-33 will own audited settings management.</summary>
public sealed class SurveyReviewOptions
{
    public int BaselineMinimumMembers { get; set; } = 3;
    public Severity LampOutSeverity { get; set; } = Severity.Medium;
    public Severity LampDimSeverity { get; set; } = Severity.Low;
    public bool IsValid() => BaselineMinimumMembers > 0
        && LampOutSeverity is Severity.Low or Severity.Medium or Severity.High
        && LampDimSeverity is Severity.Low or Severity.Medium or Severity.High;
}

public sealed record PublicationChoice(PoleObservation Observation, FixtureStatus Status, double? Confidence,
    double? Ratio, bool DimEligible, string[] Reasons);

public static class SurveyPublicationRules
{
    public static string[] Flags(string json) => JsonSerializer.Deserialize<string[]>(json) ?? [];

    // Ranking deliberately never reads lux magnitude. Stable ties use capture time and observation ID.
    public static PoleObservation Best(IEnumerable<PoleObservation> observations) => observations
        .OrderByDescending(x => x.ClassifiedAs != FixtureStatus.Unknown)
        .ThenByDescending(x => x.CvConfidence ?? 0).ThenByDescending(x => x.AssociationConfidence)
        .ThenBy(x => Flags(x.QualityFlags).Length).ThenBy(x => x.ObservedAt).ThenBy(x => x.ObservationId).First();

    public static PublicationChoice Choose(IEnumerable<PoleObservation> observations)
    {
        var rows = observations.ToArray();
        var best = Best(rows);
        if (rows.Any(x => x.CvState == "on") && rows.Any(x => x.CvState == "off"))
            return new(best, FixtureStatus.Unknown, null, null, false, ["on_off_conflict"]);
        return new(best, best.ClassifiedAs, best.ClassifiedAs == FixtureStatus.Unknown ? null : best.CvConfidence,
            best.BaselineRatio, best.DimEvaluationEligible, Flags(best.ReasonCodes));
    }

    public static bool IsNewer(DateTime evaluatedAt, DateTime? previous) => previous is null || evaluatedAt > previous;
    public static FaultType? FaultFor(FixtureStatus status) => status switch
    { FixtureStatus.Out => FaultType.LampOut, FixtureStatus.Dim => FaultType.LampDim, _ => null };
    public static Severity SeverityFor(FaultType type, bool sensitive, SurveyReviewOptions options)
    {
        var severity = type switch { FaultType.LampOut => options.LampOutSeverity,
            FaultType.LampDim => options.LampDimSeverity, _ => throw new ArgumentOutOfRangeException(nameof(type)) };
        return sensitive ? severity switch { Severity.Low => Severity.Medium, _ => Severity.High } : severity;
    }

    public static bool Eligible(PoleObservation o) => o.CvState == "on" && o.ClassifiedAs == FixtureStatus.Normal
        && o.PeakLux is >= 0 && double.IsFinite(o.PeakLux.Value)
        && !Flags(o.QualityFlags).Intersect(["peak_shared", "paired_poles", "lux_gap", "lux_saturated", "ambiguous_association"]).Any();

    public static IEnumerable<PoleObservation> Members(IEnumerable<PoleObservation> observations,
        string direction, DateOnly? installDate)
    {
        DateTime? installed = installDate?.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        return observations.Where(o => o.Pass.Direction == direction && Eligible(o)
                && (installed is null || o.ObservedAt >= installed))
            .GroupBy(o => o.RunId).Select(Best);
    }

    public static double? Median(IEnumerable<PoleObservation> observations, string direction, int minimum)
    {
        if (minimum <= 0) throw new ArgumentOutOfRangeException(nameof(minimum));
        var values = observations.Where(x => x.Pass.Direction == direction && Eligible(x)).Select(x => x.PeakLux!.Value).Order().ToArray();
        if (values.Length < minimum) return null;
        int mid = values.Length / 2;
        var median = values.Length % 2 == 1 ? values[mid] : values[mid - 1] / 2 + values[mid] / 2;
        return median > 0 ? median : null;
    }
}
