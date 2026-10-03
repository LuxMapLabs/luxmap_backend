namespace LuxMap.Modules.Survey.Processing;

public sealed class ProcessingFailure(string code, string stage) : Exception(code)
{
    public string Code { get; } = code;
    public string Stage { get; } = stage;
}

public sealed record ClockSample(int Epoch, long ModuleMs, long PhoneNs, long Sequence, double Lux);
public sealed record ClockFit(int Epoch, long ModuleOriginMs, long PhoneOriginNs, double NsPerMs,
    double OffsetNs, double ResidualP95Ms, double InlierRatio, int SampleCount, double SpanMs, int AnchorCount)
{
    public long Map(long moduleMs) => checked(PhoneOriginNs + (long)Math.Round((moduleMs - ModuleOriginMs) * NsPerMs + OffsetNs));
}
public sealed record TrackPoint(long TimeNs, double ChainageM, double AccuracyM, double? SpeedMps, bool OutsideCorridor = false, bool RouteAmbiguous = false, bool OtherRoute = false);
public sealed record PolePosition(string PoleId, string CommuneId, double ChainageM);
public sealed record LuxPoint(long TimeNs, double Lux, int Interval);
public sealed record LuxPeak(long TimeNs, double Lux, bool Saturated);
/// <summary>Confidence is a provisional ordinal tier (.2/.4/.6/.9), not a calibrated probability (A8).</summary>
public sealed record Passage(string PoleId, string CommuneId, double ChainageM, long TimeNs,
    LuxPeak? Peak, double SpeedMps, double Confidence, string[] Flags);
public sealed record PassResult(TrackPoint[] Track, Passage[] Observations, double GpsOffsetSeconds, bool OffsetReliable, double ExcessTimeRatio);

/// <summary>Only projected metres enter here. Geographic projection belongs exclusively to PostGIS.</summary>
public static class SurveyAlgorithms
{
    public static double Quantile(IEnumerable<double> values, double q)
    {
        var sorted = values.Order().ToArray();
        return sorted.Length == 0 ? 0 : sorted[(int)Math.Round((sorted.Length - 1) * q)];
    }

    public static ClockFit[] FitClocks(IReadOnlyList<ClockSample> samples, SurveyProcessingOptions o)
    {
        if (!o.IsValid()) throw new ArgumentException("Invalid processing options.", nameof(o));
        if (samples.Count == 0) throw new ProcessingFailure("CLOCK_INSUFFICIENT", "clock");
        return samples.GroupBy(x => x.Epoch).Select(group =>
        {
            var raw = group.OrderBy(x => x.Sequence).ToArray();
            if (raw.Zip(raw.Skip(1)).Any(x => x.Second.ModuleMs <= x.First.ModuleMs || x.Second.Sequence <= x.First.Sequence))
                throw new ProcessingFailure("CLOCK_NON_MONOTONIC", "clock");
            // Receipt timestamps may repeat/reorder. A BLE batch anchors its LAST module sample;
            // fitting every sample against one receipt time would manufacture drift and jitter.
            var batches = new List<ClockSample>();
            var receipts = raw.OrderBy(x => x.PhoneNs).ToArray();
            for (int i = 0; i < receipts.Length;)
            {
                int end = i + 1;
                while (end < receipts.Length && receipts[end].PhoneNs - receipts[i].PhoneNs <= o.ClockReceiptBatchWindowMs * 1e6) end++;
                batches.Add(receipts.Skip(i).Take(end - i).MaxBy(x => x.ModuleMs)!);
                i = end;
            }
            var data = batches.OrderBy(x => x.ModuleMs).ToArray();
            long m0 = data[0].ModuleMs, p0 = data[0].PhoneNs;
            var span = (double)(data[^1].ModuleMs - m0);
            if (data.Length < o.ClockMinimumSamples || span < o.ClockMinimumSpanMs)
                throw new ProcessingFailure("CLOCK_INSUFFICIENT", "clock");
            // Bounded Theil–Sen seed, then least squares over residual inliers. Origins remain int64.
            var anchors = Enumerable.Range(0, Math.Min(64, data.Length))
                .Select(i => data[i * (data.Length - 1) / (Math.Min(64, data.Length) - 1)]).ToArray();
            var slopes = new List<double>();
            for (var i = 0; i < anchors.Length; i++)
                for (var j = i + 1; j < anchors.Length; j++)
                    slopes.Add((double)(anchors[j].PhoneNs - anchors[i].PhoneNs) / (anchors[j].ModuleMs - anchors[i].ModuleMs));
            double a = Quantile(slopes, .5);
            double b = Quantile(data.Select(x => (x.PhoneNs - p0) - a * (x.ModuleMs - m0)), .5);
            var good = data.Where(x => Math.Abs((x.PhoneNs - p0) - a * (x.ModuleMs - m0) - b) <= o.ClockResidualMs * 1e6).ToArray();
            var ratio = (double)good.Length / data.Length;
            if (good.Length < o.ClockMinimumSamples || ratio < o.ClockMinimumInlierRatio)
                throw new ProcessingFailure("CLOCK_RESIDUAL", "clock");
            double mx = good.Average(x => (double)(x.ModuleMs - m0)), my = good.Average(x => (double)(x.PhoneNs - p0));
            a = good.Sum(x => (x.ModuleMs - m0 - mx) * (x.PhoneNs - p0 - my)) / good.Sum(x => Math.Pow(x.ModuleMs - m0 - mx, 2));
            b = my - a * mx;
            var residual = Quantile(good.Select(x => Math.Abs((x.PhoneNs - p0) - a * (x.ModuleMs - m0) - b) / 1e6), .95);
            if (!double.IsFinite(a) || Math.Abs(a / 1e6 - 1) * 1e6 > o.ClockMaximumDriftPpm || residual > o.ClockResidualMs)
                throw new ProcessingFailure("CLOCK_QUALITY", "clock");
            return new ClockFit(group.Key, m0, p0, a, b, residual, ratio, raw.Length, span, data.Length);
        }).ToArray();
    }

    public static LuxPoint[][] AlignLux(IReadOnlyList<ClockSample> raw, ClockFit[] fits, SurveyProcessingOptions o)
    {
        var output = new List<LuxPoint[]>();
        foreach (var epoch in raw.GroupBy(x => x.Epoch))
        {
            var fit = fits.Single(x => x.Epoch == epoch.Key);
            var buffer = new List<LuxPoint>();
            foreach (var sample in epoch.OrderBy(x => x.Sequence))
            {
                var time = fit.Map(sample.ModuleMs);
                if (buffer.Count > 0 && time - buffer[^1].TimeNs > o.LuxGapSeconds * 1e9)
                {
                    output.Add(buffer.ToArray()); buffer.Clear();
                }
                buffer.Add(new(time, sample.Lux, output.Count));
            }
            if (buffer.Count > 0) output.Add(buffer.ToArray());
        }
        return output.OrderBy(x => x[0].TimeNs).ToArray();
    }

    public static LuxPeak[] FindPeaks(IReadOnlyList<LuxPoint> data, SurveyProcessingOptions o)
    {
        var peaks = new List<LuxPeak>();
        for (var i = 1; i < data.Count - 1; i++)
        {
            if (data[i].Lux <= data[i - 1].Lux) continue;
            int end = i;
            while (end + 1 < data.Count && data[end + 1].Lux == data[i].Lux) end++;
            if (end == data.Count - 1 || data[end + 1].Lux >= data[i].Lux) continue;
            var left = i - 1;
            while (left > 0 && data[i].TimeNs - data[left - 1].TimeNs <= o.PeakWindowSeconds * 1e9) left--;
            var right = end + 1;
            while (right < data.Count - 1 && data[right + 1].TimeNs - data[end].TimeNs <= o.PeakWindowSeconds * 1e9) right++;
            var floor = Math.Max(data.Skip(left).Take(i - left).Min(x => x.Lux), data.Skip(end + 1).Take(right - end).Min(x => x.Lux));
            var prominence = data[i].Lux - floor;
            // MAD of adjacent local differences estimates baseline noise without treating a broad
            // lamp peak itself as noise. Floor remains provisional until the pilot drive.
            var differences = data.Skip(left).Take(right - left + 1).Zip(data.Skip(left + 1).Take(right - left))
                .Select(x => x.Second.Lux - x.First.Lux).ToArray();
            double centre = Quantile(differences, .5);
            double noise = Quantile(differences.Select(x => Math.Abs(x - centre)), .5);
            if (prominence < Math.Max(o.PeakMinimumProminenceLux, o.PeakNoiseMultiplier * noise)) continue;
            var half = floor + prominence / 2;
            int l = i, r = end;
            while (l > left && data[l - 1].Lux >= half) l--;
            while (r < right && data[r + 1].Lux >= half) r++;
            double width = (data[r].TimeNs - data[l].TimeNs) / 1e9;
            if (width >= o.PeakMinimumWidthSeconds && width <= o.PeakMaximumWidthSeconds)
                peaks.Add(new(data[i].TimeNs + (data[end].TimeNs - data[i].TimeNs) / 2, data[i].Lux, data[i].Lux >= o.SaturationLux));
            i = end;
        }
        return peaks.ToArray();
    }

    private static bool Usable(TrackPoint p, SurveyProcessingOptions o) =>
        p.AccuracyM <= o.MaximumAccuracyM && !p.OutsideCorridor && !p.RouteAmbiguous && !p.OtherRoute;

    public static TrackPoint[][] SplitPasses(IReadOnlyList<TrackPoint> track, SurveyProcessingOptions o)
    {
        var result = new List<TrackPoint[]>();
        var buffer = new List<TrackPoint>();
        int direction = 0, extreme = 0;
        long? previousTime = null, outsideSince = null;
        void Flush()
        {
            while (buffer.Count > 0 && !Usable(buffer[^1], o)) buffer.RemoveAt(buffer.Count - 1);
            if (buffer.Count >= 2 && Math.Abs(buffer[^1].ChainageM - buffer[0].ChainageM) >= o.TurnHysteresisM)
                result.Add(buffer.ToArray());
            buffer.Clear(); direction = 0; extreme = 0;
        }
        foreach (var point in track.OrderBy(x => x.TimeNs))
        {
            if (previousTime is long previous && (point.TimeNs - previous > o.GpsGapSeconds * 1e9 || point.TimeNs <= previous)) Flush();
            previousTime = point.TimeNs;
            // Only an ACCURATE fix nearer another route is a route change. A degraded fix drifting
            // toward it is noise: it falls through to the degraded branch and keeps the pass whole.
            if (point.OtherRoute && point.AccuracyM <= o.MaximumAccuracyM) { Flush(); outsideSince = null; continue; }
            if (point.OutsideCorridor)
            {
                outsideSince ??= point.TimeNs;
                if (point.TimeNs - outsideSince > o.RouteExitSeconds * 1e9) Flush();
            }
            else outsideSince = null;
            if (!Usable(point, o))
            {
                // Retain time/quality for diagnostics, never use its chainage to detect a turn.
                if (buffer.Count > 0) buffer.Add(point);
                continue;
            }
            buffer.Add(point);
            if (direction == 0)
            {
                if (Math.Abs(point.ChainageM - buffer[0].ChainageM) >= o.TurnHysteresisM)
                { direction = Math.Sign(point.ChainageM - buffer[0].ChainageM); extreme = buffer.Count - 1; }
                continue;
            }
            if (direction * (point.ChainageM - buffer[extreme].ChainageM) >= 0) extreme = buffer.Count - 1;
            else if (direction * (buffer[extreme].ChainageM - point.ChainageM) >= o.TurnHysteresisM)
            {
                result.Add(buffer.Take(extreme + 1).ToArray());
                buffer = buffer.Skip(extreme).ToList(); direction = -direction; extreme = buffer.Count - 1;
            }
        }
        Flush();
        return result.ToArray();
    }

    /// <summary>The shared worker/test path: lux intervals constrain peak detection, not GPS passes.</summary>
    public static PassResult[] ProcessRoute(TrackPoint[] track, PolePosition[] poles, LuxPoint[][] intervals, SurveyProcessingOptions o)
    {
        var peaks = intervals.SelectMany(interval => FindPeaks(interval, o)).ToArray();
        return SplitPasses(track, o).Select(pass => Associate(pass, poles,
            peaks.Where(p => p.TimeNs >= pass[0].TimeNs - o.MaximumGpsOffsetSeconds * 1e9
                && p.TimeNs <= pass[^1].TimeNs + o.MaximumGpsOffsetSeconds * 1e9).ToArray(), o, intervals)).ToArray();
    }

    public static PassResult Associate(TrackPoint[] track, PolePosition[] poles, LuxPeak[] peaks,
        SurveyProcessingOptions o, LuxPoint[][]? intervals = null)
    {
        var valid = track.Where(x => Usable(x, o)).ToArray();
        if (valid.Length < 2) throw new ProcessingFailure("GPS_INSUFFICIENT", "association");
        int direction = Math.Sign(valid[^1].ChainageM - valid[0].ChainageM);
        var monotone = new List<TrackPoint> { valid[0] };
        // Keep both arrival and departure of a plateau: interpolation after it starts at departure.
        foreach (var point in valid.Skip(1))
            if (direction * (point.ChainageM - monotone[^1].ChainageM) >= 0) monotone.Add(point);
        var expected = poles.OrderBy(x => direction * x.ChainageM).Where(x =>
            direction * (x.ChainageM - monotone[0].ChainageM) >= 0 && direction * (monotone[^1].ChainageM - x.ChainageM) >= 0).ToArray();
        // Group nearby poles BEFORE matching: one crossing time, no claim of per-pole photometry.
        var groups = new List<List<PolePosition>>();
        foreach (var pole in expected)
        {
            if (groups.Count == 0 || Math.Abs(pole.ChainageM - groups[^1][0].ChainageM) >= o.AmbiguousPoleDistanceM) groups.Add([]);
            groups[^1].Add(pole);
        }
        var positions = groups.Select(g => g.Average(p => p.ChainageM)).ToArray();
        var predicted = positions.Select(p => Crossing(monotone, p)).ToArray();
        var (offset, reliable) = EstimateOffset(predicted, peaks, o);
        var corrected = predicted.Select(t => t + (long)(offset * 1e9)).ToArray();
        var (matches, ambiguous) = Match(corrected, peaks, o.AssociationToleranceSeconds);
        if (!reliable && peaks.Length > 0)
        {
            ambiguous.UnionWith(matches.Keys); matches.Clear();
        }
        var observations = new List<Passage>();
        for (int i = 0; i < groups.Count; i++)
        {
            var flags = new List<string>();
            long time = corrected[i];
            matches.TryGetValue(i, out var peak);
            if (peak is not null) time = peak.TimeNs;
            else
            {
                int? before = matches.Keys.Where(k => k < i).Order().Cast<int?>().LastOrDefault();
                int? after = matches.Keys.Where(k => k > i).Order().Cast<int?>().FirstOrDefault();
                if (before is int b && after is int a)
                {
                    double f = (positions[i] - positions[b]) / (positions[a] - positions[b]);
                    time += (long)((matches[b].TimeNs - corrected[b]) * (1 - f) + (matches[a].TimeNs - corrected[a]) * f);
                    flags.Add("interpolated_between_anchors");
                }
                else flags.Add(reliable ? "gps_offset_only" : "gps_only");
                flags.Add("no_unique_lux_peak");
            }
            if (!reliable) flags.Add("gps_offset_unresolved");
            if (ambiguous.Contains(i)) flags.Add("ambiguous_association");
            if (peak?.Saturated == true) flags.Add("lux_saturated");
            // Classify degradation at the uncorrected GPS bracket, not at an unrelated nearby sample.
            int right = monotone.FindIndex(x => x.TimeNs >= predicted[i]);
            int left = Math.Max(0, right - 1);
            if (right >= 0 && track.Any(x => x.TimeNs >= monotone[left].TimeNs && x.TimeNs <= monotone[right].TimeNs
                && !Usable(x, o))) flags.Add("gps_degraded");
            if (right >= 0 && track.Any(x => x.TimeNs >= monotone[left].TimeNs && x.TimeNs <= monotone[right].TimeNs
                && x.RouteAmbiguous)) flags.Add("route_ambiguous");
            if (intervals is not null && !intervals.Any(x => time >= x[0].TimeNs && time <= x[^1].TimeNs)) flags.Add("lux_gap");
            double speed = Speed(valid, predicted[i], o);
            if (speed * 3.6 > o.MaximumKmh) flags.Add("speed_excess");
            bool paired = groups[i].Count > 1;
            if (paired) { flags.Add("paired_poles"); if (peak is not null) flags.Add("peak_shared"); }
            foreach (var pole in groups[i]) observations.Add(new(pole.PoleId, pole.CommuneId, pole.ChainageM, time,
                paired ? null : peak, speed,
                ambiguous.Contains(i) ? .2 : peak is not null && !paired ? .9 : flags.Contains("gps_only") ? .4 : .6, flags.ToArray()));
        }
        double duration = (valid[^1].TimeNs - valid[0].TimeNs) / 1e9;
        double excess = valid.Zip(valid.Skip(1)).Sum(x => Speed(valid, x.First.TimeNs, o) * 3.6 > o.MaximumKmh
            ? (x.Second.TimeNs - x.First.TimeNs) / 1e9 : 0) / duration;
        return new(track, observations.ToArray(), offset, reliable, excess);
    }

    private static (Dictionary<int, LuxPeak> Matches, HashSet<int> Ambiguous) Match(long[] times, LuxPeak[] peaks, double tolerance)
    {
        var candidates = times.Select(t => peaks.Where(p => Math.Abs(p.TimeNs - t) <= tolerance * 1e9).ToArray()).ToArray();
        var matches = new Dictionary<int, LuxPeak>();
        var ambiguous = new HashSet<int>();
        for (int i = 0; i < times.Length; i++)
        {
            if (candidates[i].Length > 1) ambiguous.Add(i);
            else if (candidates[i].Length == 1)
            {
                if (candidates.Count(c => c.Contains(candidates[i][0])) == 1) matches.Add(i, candidates[i][0]);
                else ambiguous.Add(i);
            }
        }
        foreach (var pair in matches.Keys.Order().Zip(matches.Keys.Order().Skip(1)).ToArray())
            if (matches[pair.First].TimeNs >= matches[pair.Second].TimeNs) { ambiguous.Add(pair.First); ambiguous.Add(pair.Second); }
        foreach (var i in ambiguous) matches.Remove(i);
        return (matches, ambiguous);
    }

    private static (double Offset, bool Reliable) EstimateOffset(long[] predicted, LuxPeak[] peaks, SurveyProcessingOptions o)
    {
        var hypotheses = predicted.SelectMany(t => peaks.Select(p => (p.TimeNs - t) / 1e9))
            .Where(d => Math.Abs(d) <= o.MaximumGpsOffsetSeconds).Distinct().ToArray();
        var scored = hypotheses.Select(offset =>
        {
            var times = predicted.Select(t => t + (long)(offset * 1e9)).ToArray();
            var (matches, _) = Match(times, peaks, o.AssociationToleranceSeconds);
            return new { Offset = offset, Matches = matches,
                Residual = Quantile(matches.Select(m => Math.Abs(m.Value.TimeNs - times[m.Key]) / 1e9), .5) };
        }).OrderByDescending(x => x.Matches.Count).ThenBy(x => x.Residual).ThenBy(x => Math.Abs(x.Offset)).ToArray();
        if (scored.Length == 0 || scored[0].Matches.Count < o.MinimumOffsetAnchors) return (0, false);
        var best = scored[0];
        if (scored.Skip(1).Any(x => x.Matches.Count == best.Matches.Count && x.Residual <= best.Residual + o.OffsetAmbiguitySeconds
            && Math.Abs(x.Offset - best.Offset) > 2 * o.AssociationToleranceSeconds)) return (0, false);
        return (Quantile(best.Matches.Select(m => (m.Value.TimeNs - predicted[m.Key]) / 1e9), .5), true);
    }

    private static long Crossing(List<TrackPoint> track, double chainage)
    {
        for (int i = 1; i < track.Count; i++)
            if (track[i].ChainageM != track[i - 1].ChainageM && chainage >= Math.Min(track[i - 1].ChainageM, track[i].ChainageM) && chainage <= Math.Max(track[i - 1].ChainageM, track[i].ChainageM))
                return track[i - 1].TimeNs + (long)((track[i].TimeNs - track[i - 1].TimeNs) *
                    ((chainage - track[i - 1].ChainageM) / (track[i].ChainageM - track[i - 1].ChainageM)));
        throw new ProcessingFailure("GPS_CROSSING_MISSING", "association");
    }

    private static double Speed(TrackPoint[] track, long time, SurveyProcessingOptions o)
    {
        var samples = track.Zip(track.Skip(1)).Select(x => new
        {
            Time = x.First.TimeNs,
            Speed = x.First.SpeedMps ?? Math.Abs(x.Second.ChainageM - x.First.ChainageM) / ((x.Second.TimeNs - x.First.TimeNs) / 1e9)
        }).ToArray();
        var window = samples.Where(x => Math.Abs(x.Time - time) <= o.SpeedWindowSeconds * 1e9).ToArray();
        return window.Length == 0 ? samples.MinBy(x => Math.Abs(x.Time - time))!.Speed : Quantile(window.Select(x => x.Speed), .5);
    }

    public static double? Coverage(IEnumerable<PolePosition> expected, IEnumerable<PassResult> passes)
    {
        int count = expected.Select(x => x.PoleId).Distinct().Count();
        return count == 0 ? null : 100d * passes.SelectMany(x => x.Observations).Select(x => x.PoleId).Distinct().Count() / count;
    }
}
