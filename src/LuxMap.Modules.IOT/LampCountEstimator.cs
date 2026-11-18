using System.Text.Json.Serialization;

namespace LuxMap.Modules.IOT;

public sealed record LampCountEstimate(
    string Status, int Samples, int RequiredSamples, int RequiredVotes,
    int? EstimatedLit, int? EstimatedMissing,
    IReadOnlyDictionary<int, int> Votes, int AmbiguousSamples, int OutsideRangeSamples)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? MedianCurrentMa { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public LampCountThresholdCounts? ThresholdCounts { get; init; }
}

public sealed record LampCountThresholdCounts(
    [property: JsonPropertyName("above_65_ma")] int Above65Ma,
    [property: JsonPropertyName("at_or_below_52_5_ma")] int AtOrBelow52Point5Ma,
    [property: JsonPropertyName("at_or_above_50_ma")] int AtOrAbove50Ma,
    [property: JsonPropertyName("at_or_above_45_ma")] int AtOrAbove45Ma);

internal static class LampCountEstimator
{
    internal const int WindowSize = 25;
    internal const double VoltageLossLimit = 0.1;
    public const string FourVolt = "4v";
    public const string FiveVolt = "5v";
    public const string QuietFiveVolt = "5v-quiet-v1";

    private static readonly (int Count, double Min, double Max)[] QuietBands =
    [
        (7, 58.75, 72.25), (8, 74.75, 82.20), (9, 84.75, 90.20), (10, 92.75, 102.5),
    ];
    private const int FiveVoltRequiredVotes = 15;
    private const int MinimumThresholdSamples = 10;
    private const int InstalledLamps = 10;

    private static readonly (int Count, double Min, double Max)[] FiveVoltBands =
    [
        (10, 93.75, 106.25), (9, 85, 96.25), (8, 76.25, 85), (7, 67.5, 72.5),
        (6, 56.25, 67.5), (5, 46.875, 56.25), (4, 37.5, 45), (3, 28.125, 33.75),
        (2, 18.75, 22.5), (1, 10, 11.25),
    ];

    private static readonly (int Count, double Min, double Max)[] FourVoltBands =
    [
        (10, 56.25, 75), (9, 53.75, 65), (8, 43.75, 52.5),
        (7, 42.5, 48.75), (6, 38.75, 43.75),
    ];

    internal static bool InCalibrationRange(double voltage, string calibration) => calibration == QuietFiveVolt
        ? voltage is >= 4.80 and <= 5.30
        : calibration == FourVolt
        ? voltage is >= 4.00 and <= 4.13
        : voltage is >= 4.75 and <= 5.25;

    internal static LampCountEstimate Evaluate(IReadOnlyList<double> samples, string calibration, int installedLamps = InstalledLamps)
    {
        var fourVolt = calibration == FourVolt;
        var bands = calibration == QuietFiveVolt ? QuietBands : fourVolt ? FourVoltBands : FiveVoltBands;
        var requiredVotes = calibration == QuietFiveVolt ? 20 : fourVolt ? WindowSize : FiveVoltRequiredVotes;
        var votes = bands.ToDictionary(band => band.Count, _ => 0);
        var ambiguous = 0;
        var outside = 0;
        foreach (var current in samples)
        {
            var matches = bands.Where(band => current >= band.Min && current <= band.Max).ToArray();
            if (matches.Length == 0) outside++;
            if (matches.Length > 1) ambiguous++;
            if (fourVolt || matches.Length == 1)
                foreach (var band in matches) votes[band.Count]++;
        }

        LampCountThresholdCounts? counts = null;
        double? median = null;
        if (fourVolt)
        {
            var ordered = samples.Order().ToArray();
            if (ordered.Length > 0)
                median = (ordered[(ordered.Length - 1) / 2] + ordered[ordered.Length / 2]) / 2;
            counts = new(samples.Count(current => current > 65), samples.Count(current => current <= 52.5),
                samples.Count(current => current >= 50), samples.Count(current => current >= 45));
        }

        if (calibration == QuietFiveVolt && samples.Count > 0)
        {
            var ordered = samples.Order().ToArray();
            median = (ordered[(ordered.Length - 1) / 2] + ordered[ordered.Length / 2]) / 2;
        }
        var candidates = votes.Where(vote => vote.Value >= requiredVotes &&
            (calibration != QuietFiveVolt || bands.Any(b => b.Count == vote.Key && median >= b.Min && median <= b.Max)) &&
            (counts is null || SupportsFourVoltEstimate(vote.Key, counts))).Select(vote => vote.Key).ToArray();
        int? lit = samples.Count == WindowSize && candidates.Length == 1 ? candidates[0] : null;
        var status = samples.Count < WindowSize ? "insufficient_samples" : lit is null ? "undetermined" : "estimated";
        return new(status, samples.Count, WindowSize, requiredVotes, lit, lit is null ? null : Math.Max(0, installedLamps - lit.Value), votes, ambiguous, outside)
        {
            MedianCurrentMa = median, ThresholdCounts = counts,
        };
    }

    // Keep a rejection margin for windows whose high-current samples are sparse.
    private static bool SupportsFourVoltEstimate(int lamps, LampCountThresholdCounts counts) => lamps switch
    {
        10 => counts.Above65Ma >= MinimumThresholdSamples,
        9 => counts.Above65Ma == 0 && counts.AtOrBelow52Point5Ma == 0,
        8 => counts.AtOrBelow52Point5Ma == WindowSize && counts.AtOrAbove50Ma >= MinimumThresholdSamples,
        7 => counts.AtOrAbove50Ma == 0 && counts.AtOrAbove45Ma >= MinimumThresholdSamples,
        6 => counts.AtOrAbove45Ma == 0,
        _ => false,
    };
}
