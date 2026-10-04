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

public sealed record ExpectedSurveyPole(string PoleId, string CommuneId);
public sealed record PublicationTarget(string PoleId, PublicationChoice? Choice);

public static class SurveyPublicationRules
{
    public static ExpectedSurveyPole[] ExpectedPoles(string snapshot)
    {
        using var json = JsonDocument.Parse(snapshot);
        return json.RootElement.TryGetProperty("poles", out var poles)
            ? poles.EnumerateArray().Select(p => new ExpectedSurveyPole(
                p.GetProperty("pole_id").GetString()!, p.GetProperty("commune_id").GetString()!)).ToArray() : [];
    }

    public static string[] UnobservedPoles(string snapshot, IEnumerable<string> observedIds)
        => ExpectedPoles(snapshot).Select(p => p.PoleId).Except(observedIds)
            .OrderBy(id => id.Length).ThenBy(id => id, StringComparer.Ordinal).ToArray();

    public static IEnumerable<string> RequiredCommunes(string snapshot, IEnumerable<string> observed, IEnumerable<string> current)
        => observed.Concat(ExpectedPoles(snapshot).Select(p => p.CommuneId)).Concat(current).Distinct();

    public static PublicationTarget[] PublicationSet(string snapshot, IEnumerable<PoleObservation> observations)
    {
        var choices = observations.GroupBy(o => o.PoleId).ToDictionary(g => g.Key, Choose);
        return ExpectedPoles(snapshot).Select(p => p.PoleId).Union(choices.Keys)
            .Select(id => new PublicationTarget(id, choices.GetValueOrDefault(id))).ToArray();
    }

    public static LuminanceHistory History(PublicationTarget target, SurveySweep sweep, long runId,
        string communeId, DateTime publishedAt, string publishedBy)
    {
        var choice = target.Choice;
        var o = choice?.Observation;
        return new() { SweepId = sweep.SweepId, PoleId = target.PoleId, CommuneId = communeId, RunId = runId,
            ObservationId = o?.ObservationId, BaselineId = o?.BaselineId,
            EvaluatedAt = o?.ObservedAt ?? sweep.AtElapsed(sweep.EndedElapsedNs)
                ?? throw new InvalidOperationException("A completed sweep must have a valid end time."),
            PeakLux = o?.PeakLux, CvState = choice?.Status == FixtureStatus.Unknown ? null : o?.CvState,
            BaselineRatio = choice?.Ratio, StatusConfidence = choice?.Confidence, AssociationConfidence = o?.AssociationConfidence ?? 0,
            ClassifiedAs = choice?.Status ?? FixtureStatus.Unknown, DimEvaluationEligible = choice?.DimEligible ?? false,
            DataSource = sweep.DataSource, PublishedAt = publishedAt, PublishedBy = publishedBy,
            ReasonCodes = JsonSerializer.Serialize(choice?.Reasons ?? ["not_observed"]) };
    }

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
