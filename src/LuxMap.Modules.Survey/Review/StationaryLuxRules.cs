using System.Globalization;

namespace LuxMap.Modules.Survey.Review;

/// <summary>
/// LUX-BASELINE D-5: the stationary measuring protocol. In appsettings, not <c>system_setting</c>, on purpose: changing the height
/// mid-pilot restarts every baseline (D-9), so it is a deployment decision, not a screen an administrator edits.
/// </summary>
/// <remarks>The defaults are the proposal of D-R29 O-3 until the field trial settles real numbers.</remarks>
public sealed class LuxProtocolOptions
{
    public double SensorHeightM { get; set; } = 1.0;
    public double SensorHeightToleranceM { get; set; } = 0.05;
    public int MinSamples { get; set; } = 5;

    /// <summary>Largest accepted <c>(max − min) / median</c> of one window; above it the engineer measures again on the spot.</summary>
    public double MaxWindowSpread { get; set; } = 0.05;

    /// <summary>Largest coefficient of variation of a baseline's members before dim stops being judged (D-11, NFR CV ≤ 10%).</summary>
    public double MaxBaselineCv { get; set; } = 0.10;

    /// <summary>How far <c>lux_value</c> may sit from the server's median of the samples, relative (D-4).</summary>
    public double LuxValueTolerance { get; set; } = 0.01;

    public bool IsValid()
        => SensorHeightM is > 0 and < 100 && SensorHeightToleranceM is >= 0 and < 10 && MinSamples is >= 1 and <= 10_000
           && MaxWindowSpread is >= 0 and <= 10 && MaxBaselineCv is >= 0 and <= 10 && LuxValueTolerance is >= 0 and <= 1;

    /// <summary>Folded into the classifier's version hash so a changed threshold is a changed version (BE-34).</summary>
    public string Describe() => string.Create(CultureInfo.InvariantCulture,
        $"stationary-lux;h={SensorHeightM};tol={SensorHeightToleranceM};n={MinSamples};spread={MaxWindowSpread};cv={MaxBaselineCv};lux={LuxValueTolerance}");
}

/// <summary>The one definition of each stationary rule; callers never restate them.</summary>
public static class StationaryLuxRules
{
    /// <summary>Survey nights cross midnight (Vietnam, UTC+7, no DST): a night is the local date of the moment 12 hours earlier.</summary>
    public static DateOnly Night(DateTime measuredAtUtc)
        => DateOnly.FromDateTime(DateTime.SpecifyKind(measuredAtUtc, DateTimeKind.Utc).AddHours(7 - 12));

    public static double Median(IReadOnlyCollection<double> values)
    {
        if (values.Count == 0) throw new ArgumentException("No values.", nameof(values));
        var sorted = values.Order().ToArray();
        var mid = sorted.Length / 2;
        return sorted.Length % 2 == 1 ? sorted[mid] : sorted[mid - 1] / 2 + sorted[mid] / 2;
    }

    /// <summary><c>(max − min) / median</c>; zero for a dark window whose every sample is zero.</summary>
    public static double Spread(IReadOnlyCollection<double> samples)
    {
        var median = Median(samples);
        var range = samples.Max() - samples.Min();
        return range == 0 ? 0 : median > 0 ? range / median : double.PositiveInfinity;
    }

    /// <summary>Population coefficient of variation.</summary>
    public static double Cv(IReadOnlyCollection<double> values)
    {
        var mean = values.Average();
        if (mean <= 0) return double.PositiveInfinity;
        var variance = values.Sum(v => (v - mean) * (v - mean)) / values.Count;
        return Math.Sqrt(variance) / mean;
    }

    public static string Fingerprint(string moduleId, double sensorHeightM)
        => string.Create(CultureInfo.InvariantCulture, $"{moduleId.Trim()}@{sensorHeightM:0.###}");
}
