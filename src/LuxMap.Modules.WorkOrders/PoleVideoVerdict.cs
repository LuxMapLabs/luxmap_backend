using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using LuxMap.Shared.Media;

namespace LuxMap.Modules.WorkOrders;

/// <summary>One sampled frame as the verdict sees it: the single-lamp model's say (null when the model was unavailable) and the frame's brightness.</summary>
public sealed record FrameVote(string? State, double? Confidence, string? Reason, double Luma);

/// <summary>
/// The clip's verdict (POLE-VIDEO D-4). <see cref="State"/> is <c>on</c> / <c>off</c> / <c>inconclusive</c>, or null when the model was unavailable;
/// <see cref="Confidence"/> is the median over the frames that agree (null unless on / off). The spread (<see cref="ConfidenceMin"/> …) is over every
/// frame that voted, agreeing or not. <see cref="RepresentativeIndex"/> is the frame that becomes the photo.
/// </summary>
public sealed record PoleVideoVerdictResult(string? State, double? Confidence, int RepresentativeIndex, int OnFrames, int OffFrames, int DaylightFrames,
    double? ConfidenceMin, double? ConfidenceMedian, double? ConfidenceMax, double MedianLuma, string? ReasonCode);

/// <summary>Turns the votes of a clip's frames into one verdict. Pure: no clock, no I/O, every number from <see cref="PoleVideoOptions"/>.</summary>
public static class PoleVideoVerdict
{
    /// <summary>Bump when the rules below change in a way the options cannot show, so a result names the rules that made it.</summary>
    public const int RulesRevision = 1;

    /// <summary>
    /// The rules, in order. (1) Fewer than <see cref="PoleVideoOptions.MinimumDecidedFraction"/> of the frames said ON or OFF: no verdict, and the reason is
    /// the commonest one the others gave. (2) The agreeing share of those is under <see cref="PoleVideoOptions.MinimumAgreement"/>, or the first and the last
    /// frame that voted disagree (a lamp that flickers, or one that had just come on): no verdict, <c>INCONSISTENT</c>. (3) Otherwise the majority label, with
    /// the median confidence of the frames that gave it; the photo is the frame whose confidence is nearest that median.
    /// </summary>
    public static PoleVideoVerdictResult Decide(IReadOnlyList<FrameVote> votes, PoleVideoOptions options)
    {
        if (votes.Count == 0) throw new ArgumentException("A clip has at least one sampled frame.", nameof(votes));
        var medianLuma = Median(votes.Select(v => v.Luma).ToArray());
        var middle = votes.Count / 2;
        if (votes.Any(v => v.State is null))
            return new(null, null, middle, 0, 0, 0, null, null, null, medianLuma, null);          // the model was unavailable

        var decided = votes.Select((v, i) => (Vote: v, Index: i)).Where(x => x.Vote.State is "on" or "off").ToArray();
        var on = decided.Count(x => x.Vote.State == "on");
        var off = decided.Length - on;
        var daylight = votes.Count(v => v.Reason == "too_bright");
        var spread = decided.Length == 0 ? null : decided.Select(x => x.Vote.Confidence!.Value).Order().ToArray();
        (double?, double?, double?) trio = spread is null ? (null, null, null) : (spread[0], Median(spread), spread[^1]);
        PoleVideoVerdictResult None(string reason) => new("inconclusive", null, middle, on, off, daylight, trio.Item1, trio.Item2, trio.Item3, medianLuma, reason);

        if (decided.Length < options.MinimumDecidedFraction * votes.Count - 1e-9) return None(UndecidedReason(votes));
        var label = on >= off ? "on" : "off";
        var agreeing = decided.Where(x => x.Vote.State == label).ToArray();
        if (agreeing.Length < options.MinimumAgreement * decided.Length - 1e-9
            || decided[0].Vote.State != decided[^1].Vote.State)
            return None("INCONSISTENT");
        var median = Median(agreeing.Select(x => x.Vote.Confidence!.Value).ToArray());
        var representative = agreeing.OrderBy(x => Math.Abs(x.Vote.Confidence!.Value - median)).ThenBy(x => x.Index).First().Index;
        return new(label, median, representative, on, off, daylight, trio.Item1, trio.Item2, trio.Item3, medianLuma, null);
    }

    /// <summary>The commonest reason among the frames that gave no verdict; a tie goes to the one that tells the engineer most (too bright, then several lamps, then none).</summary>
    private static string UndecidedReason(IReadOnlyList<FrameVote> votes)
    {
        var counts = votes.Where(v => v.State is not ("on" or "off")).GroupBy(v => v.Reason switch
            { "too_bright" => "TOO_BRIGHT", "multiple_lamps" => "MULTIPLE_LAMPS", _ => "NO_LAMP" }).ToDictionary(g => g.Key, g => g.Count());
        return new[] { "TOO_BRIGHT", "MULTIPLE_LAMPS", "NO_LAMP" }.OrderByDescending(code => counts.GetValueOrDefault(code)).First();
    }

    /// <summary>Median; the mean of the two middle values for an even count.</summary>
    public static double Median(double[] values)
    {
        var sorted = values.Order().ToArray();
        return sorted.Length % 2 == 1 ? sorted[sorted.Length / 2] : (sorted[sorted.Length / 2 - 1] + sorted[sorted.Length / 2]) / 2;
    }

    /// <summary>
    /// <c>pv-</c> and 12 hex digits of the sampling and verdict numbers plus <see cref="RulesRevision"/>: changing any of them is a different rule set, and
    /// a result says which (BE-34, "a finding that cannot be reproduced is not a result").
    /// </summary>
    public static string RulesVersion(PoleVideoOptions o)
    {
        var rules = string.Create(CultureInfo.InvariantCulture,
            $"pole-video:r{RulesRevision};fps={o.FramesPerSecond:R};max-frames={o.MaximumFrames};skip={o.SkipLeadingSeconds:R};skip-above={o.SkipLeadingOnlyAboveSeconds:R};"
            + $"min-s={o.MinimumSeconds:R};max-s={o.MaximumSeconds:R};grace={o.DurationGraceSeconds:R};decided={o.MinimumDecidedFraction:R};agree={o.MinimumAgreement:R}");
        return "pv-" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(rules)))[..12];
    }
}
