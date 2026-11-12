namespace LuxMap.Modules.IOT;

public sealed record LampCountEstimate(
    string Status, int Samples, int RequiredSamples, int RequiredVotes,
    int? EstimatedLit, int? EstimatedMissing,
    IReadOnlyDictionary<int, int> Votes, int AmbiguousSamples, int OutsideRangeSamples);

internal static class LampCountEstimator
{
    internal const int WindowSize = 25;
    internal const double VoltageLossLimit = 0.1;
    private const int RequiredVotes = 15;
    private const int InstalledLamps = 10;

    private static readonly (int Count, double Min, double Max)[] Bands =
    [
        (10, 93.75, 106.25), (9, 85, 96.25), (8, 76.25, 85), (7, 67.5, 72.5),
        (6, 56.25, 67.5), (5, 46.875, 56.25), (4, 37.5, 45), (3, 28.125, 33.75),
        (2, 18.75, 22.5), (1, 10, 11.25),
    ];

    internal static bool InCalibrationRange(double voltage) => voltage is >= 4.75 and <= 5.25;

    internal static LampCountEstimate Evaluate(IReadOnlyList<double> samples)
    {
        var votes = Bands.ToDictionary(band => band.Count, _ => 0);
        var ambiguous = 0;
        var outside = 0;
        foreach (var current in samples)
        {
            var matches = Bands.Where(band => current >= band.Min && current <= band.Max).ToArray();
            if (matches.Length == 1) votes[matches[0].Count]++;
            else if (matches.Length == 0) outside++;
            else ambiguous++;
        }

        int? lit = samples.Count == WindowSize
            ? votes.Where(vote => vote.Value >= RequiredVotes).Select(vote => (int?)vote.Key).SingleOrDefault()
            : null;
        var status = samples.Count < WindowSize ? "insufficient_samples" : lit is null ? "undetermined" : "estimated";
        return new(status, samples.Count, WindowSize, RequiredVotes, lit, InstalledLamps - lit, votes, ambiguous, outside);
    }
}
