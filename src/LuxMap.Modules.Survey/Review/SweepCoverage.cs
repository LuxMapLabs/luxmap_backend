using LuxMap.Shared.Contracts.GeoJson;

namespace LuxMap.Modules.Survey.Review;

/// <summary><c>GET /sweeps/{id}/coverage</c> (BE-17, D-1…D-4). SELF-SIGNED, provisional until FW.</summary>
public sealed record SweepCoverageResponse(string SweepId, long RunId, double? CoveragePct, SegmentCoverage[] Segments);

/// <summary>
/// One segment of the job as the run saw it. Metres are positions ALONG the segment from its first vertex — the fraction the pass
/// covered times the segment's declared <c>length_m</c> (D-3) — never a distance between two points.
/// </summary>
public sealed record SegmentCoverage(
    string SegmentId, double? LengthM, double CoveredRatio, PassCoverage[] Passes, CoverageGap[] Gaps,
    int PolesExpected, int PolesObserved, string[] NotObservedPoleIds);

/// <summary>A pass in the order it was driven: <c>from_m &gt; to_m</c> for a <c>reverse</c> pass.</summary>
public sealed record PassCoverage(int PassNo, string Direction, double? FromM, double? ToM);

/// <summary>A stretch no pass drove (D-2: "not driven", by the GPS track matched to the segment). Geometry is EPSG:4326.</summary>
public sealed record CoverageGap(double? FromM, double? ToM, Geometry? Geometry);

/// <summary>The interval arithmetic behind the coverage, kept pure so it is tested without a database.</summary>
public static class SweepCoverage
{
    /// <summary>A gap SHORTER than this is GPS noise between two passes that met, not a stretch nobody drove; one this long is kept (SC-4).</summary>
    public const double MinimumGapM = 1.0;

    /// <summary>The union of the passes as sorted, disjoint <c>[from, to]</c> fractions — direction dropped, clamped to [0, 1].</summary>
    public static (double From, double To)[] Union(IEnumerable<(double A, double B)> passes)
    {
        var merged = new List<(double From, double To)>();
        foreach (var (from, to) in passes
                     .Select(p => (From: Math.Clamp(Math.Min(p.A, p.B), 0, 1), To: Math.Clamp(Math.Max(p.A, p.B), 0, 1)))
                     .OrderBy(p => p.From))
        {
            if (merged.Count > 0 && from <= merged[^1].To) merged[^1] = (merged[^1].From, Math.Max(merged[^1].To, to));
            else merged.Add((from, to));
        }

        return [.. merged];
    }

    /// <summary>
    /// The complement of <paramref name="covered"/> in [0, 1], without gaps shorter than <see cref="MinimumGapM"/> on a segment of
    /// <paramref name="lengthM"/>. Unknown length: every gap is kept.
    /// </summary>
    public static (double From, double To)[] Gaps(IReadOnlyList<(double From, double To)> covered, double? lengthM)
    {
        var minimum = lengthM is > 0 ? MinimumGapM / lengthM.Value : 0;
        var gaps = new List<(double From, double To)>();
        var cursor = 0d;
        foreach (var (from, to) in covered)
        {
            if (Keeps(from - cursor, minimum)) gaps.Add((cursor, from));
            cursor = Math.Max(cursor, to);
        }

        if (Keeps(1 - cursor, minimum)) gaps.Add((cursor, 1));
        return [.. gaps];
    }

    // Positive, and at least the minimum — with a hair of tolerance, since fraction × length rarely lands exactly on 1 m.
    private static bool Keeps(double width, double minimum) => width > 0 && width >= minimum - 1e-9;

    /// <summary>Covered share of the segment: the gaps that were kept are what is missing.</summary>
    public static double CoveredRatio(IReadOnlyList<(double From, double To)> gaps)
        => Math.Clamp(1 - gaps.Sum(g => g.To - g.From), 0, 1);
}
