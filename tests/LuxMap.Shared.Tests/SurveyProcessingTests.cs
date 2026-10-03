using LuxMap.Modules.Survey.Processing;
using Xunit.Abstractions;
using System.Globalization;

namespace LuxMap.Shared.Tests;

public sealed class SurveyProcessingTests(ITestOutputHelper output)
{
    private static readonly SurveyProcessingOptions Options = new();

    [Fact]
    public void Clock_fit_handles_drift_outliers_large_origins_and_epoch_reset()
    {
        var raw = Enumerable.Range(0, 2).SelectMany(e => Enumerable.Range(0, 100).Select(i =>
            new ClockSample(e, 8_000_000_000L + i * 125, 8_000_000_000_000_000L + e * 20_000_000_000L + i * 125_010_000L + (i == 40 ? 100_000_000 : 0), i, 1))).ToArray();
        var fits = SurveyAlgorithms.FitClocks(raw, Options);
        Assert.Equal(2, fits.Length);
        foreach (var fit in fits)
        {
            Assert.InRange(fit.NsPerMs, 1_000_079, 1_000_081);
            Assert.InRange(fit.ResidualP95Ms, 0, .001);
            Assert.Equal(.99, fit.InlierRatio, 3);
        }
        Assert.True(fits[1].Map(raw[0].ModuleMs) > fits[0].Map(raw[0].ModuleMs));
    }

    [Fact]
    public void Receipt_batches_and_reordered_receipts_are_noise_not_invalid_module_time()
    {
        var raw = Enumerable.Range(0, 160).Select(i => new ClockSample(0, i * 125,
            (i / 4 * 4 + 3) * 125_000_000L, i, 1)).ToArray();
        raw[80] = raw[80] with { PhoneNs = raw[76].PhoneNs + 1_000_000 };
        var fit = Assert.Single(SurveyAlgorithms.FitClocks(raw, Options));
        Assert.InRange(fit.Map(10_000), 9_990_000_000, 10_010_000_000);
        Assert.Equal(160, fit.SampleCount);
        Assert.InRange(fit.AnchorCount, 39, 40);
        raw[10] = raw[10] with { ModuleMs = raw[9].ModuleMs };
        Assert.Equal("CLOCK_NON_MONOTONIC", Assert.Throws<ProcessingFailure>(() => SurveyAlgorithms.FitClocks(raw, Options)).Code);
    }

    [Fact]
    public void Insufficient_or_bad_clock_fails_without_association()
    {
        Assert.Equal("CLOCK_INSUFFICIENT", Assert.Throws<ProcessingFailure>(() => SurveyAlgorithms.FitClocks([], Options)).Code);
        var raw = Enumerable.Range(0, 30).Select(i => new ClockSample(0, i * 125, i * 130_000_000L, i, 1)).ToArray();
        Assert.Equal("CLOCK_QUALITY", Assert.Throws<ProcessingFailure>(() => SurveyAlgorithms.FitClocks(raw, Options)).Code);
    }

    [Fact]
    public void One_dropped_packet_keeps_interval_but_time_gap_and_epoch_reset_split_lux_only()
    {
        var raw = Enumerable.Range(0, 80).Where(i => i != 10 && i is not (>= 20 and <= 27))
            .Select(i => new ClockSample(i / 40, i % 40 * 125, i * 125_000_000L, i % 40, 1)).ToArray();
        var intervals = SurveyAlgorithms.AlignLux(raw, SurveyAlgorithms.FitClocks(raw, Options), Options);
        Assert.Equal(3, intervals.Length);
        var track = Enumerable.Range(0, 10).Select(i => new TrackPoint(i * 1_000_000_000L, i * 10, 2, 10)).ToArray();
        var pass = Assert.Single(SurveyAlgorithms.ProcessRoute(track, [new("P", "A", 30)], intervals, Options));
        Assert.Contains("lux_gap", Assert.Single(pass.Observations).Flags);
    }

    [Fact]
    public void Bad_gps_is_excluded_from_geometry_but_flags_crossings_without_splitting_the_pass()
    {
        var track = Enumerable.Range(0, 20).Select(i => new TrackPoint(i * 1_000_000_000L,
            i is >= 6 and <= 12 ? 1000 : i * 5, i is >= 6 and <= 12 ? 25 : 2, 5)).ToArray();
        var pass = Assert.Single(SurveyAlgorithms.SplitPasses(track, Options));
        var result = SurveyAlgorithms.Associate(pass, [new("P", "A", 45)], [], Options);
        var observation = Assert.Single(result.Observations);
        Assert.Equal(9_000_000_000, observation.TimeNs);
        Assert.Contains("gps_degraded", observation.Flags);
    }

    [Fact]
    public void Peaks_reject_a_narrow_spike_and_flag_saturated_plateau()
    {
        var lux = Enumerable.Range(0, 81).Select(i => new LuxPoint(i * 125_000_000L,
            i is >= 20 and <= 24 ? 65535 : i == 50 ? 80 : 2, 0)).ToArray();
        var peak = Assert.Single(SurveyAlgorithms.FindPeaks(lux, Options));
        Assert.True(peak.Saturated);
        Assert.Equal(2_750_000_000L, peak.TimeNs);
    }

    [Fact]
    public void Low_prominence_lamp_survives_quiet_background_but_not_high_noise()
    {
        var quiet = Enumerable.Range(0, 80).Select(i => new LuxPoint(i * 125_000_000L,
            1.5 + 3.5 * Math.Exp(-Math.Pow((i / 8d - 5) / .3, 2) / 2), 0)).ToArray();
        Assert.Single(SurveyAlgorithms.FindPeaks(quiet, Options));
        var noisy = quiet.Select((x,i) => x with { Lux = x.Lux + (i % 2 == 0 ? 3 : 0) }).ToArray();
        Assert.Empty(SurveyAlgorithms.FindPeaks(noisy, Options));
    }

    [Fact]
    public void Turnaround_splits_but_jitter_does_not_and_missing_gps_is_never_bridged()
    {
        double[] positions = [0, 5, 10, 9, 15, 20, 19, 25, 20, 15, 10, 5, 0];
        var track = positions.Select((p, i) => new TrackPoint(i * 1_000_000_000L, p, 2, null)).ToArray();
        Assert.Equal(2, SurveyAlgorithms.SplitPasses(track, Options).Length);
        var gap = track.Take(6).Concat(track.Take(6).Select(x => x with { TimeNs = x.TimeNs + 20_000_000_000L, ChainageM = x.ChainageM + 100 })).ToArray();
        var passes = SurveyAlgorithms.SplitPasses(gap, Options);
        Assert.Equal(2, passes.Length);
        Assert.DoesNotContain(passes.SelectMany(p => SurveyAlgorithms.Associate(p, [new("missing", "A", 60)], [], Options).Observations), p => p.PoleId == "missing");
    }

    [Fact]
    public void Shared_peak_times_both_poles_without_attributing_lux_and_extra_peak_is_ambiguous()
    {
        var track = Enumerable.Range(0, 31).Select(i => new TrackPoint(i * 1_000_000_000L, i * 5, 2, 5)).ToArray();
        PolePosition[] poles = [new("A", "X",25),new("B","X",50),new("B2","X",50.5),new("C","X",75),new("D","X",100),new("E","X",125)];
        LuxPeak[] peaks = [new(5_000_000_000,5,false),new(10_000_000_000,5,false),new(15_000_000_000,5,false),new(20_000_000_000,5,false),new(25_000_000_000,5,false),new(25_400_000_000,5,false)];
        var pass = SurveyAlgorithms.Associate(track,poles,peaks,Options);
        Assert.True(pass.OffsetReliable);
        var pair = pass.Observations.Where(x => x.PoleId is "B" or "B2").ToArray();
        Assert.Equal(2,pair.Length);
        Assert.All(pair, p => { Assert.Equal(10_000_000_000,p.TimeNs); Assert.Null(p.Peak); Assert.Contains("paired_poles",p.Flags); Assert.Contains("peak_shared",p.Flags); });
        var ambiguous = Assert.Single(pass.Observations, p => p.PoleId == "E");
        Assert.Null(ambiguous.Peak);
        Assert.Contains("ambiguous_association",ambiguous.Flags);
    }

    [Fact]
    public void Stationary_interval_uses_departure_for_the_next_crossing_in_both_directions()
    {
        foreach (int direction in new[] { 1, -1 })
        {
            var track = Enumerable.Range(0, 12).Select(t => new TrackPoint(t * 1_000_000_000L,
                direction * (t == 0 ? 0 : t == 11 ? 20 : 10), 2, null)).ToArray();
            var pass = SurveyAlgorithms.Associate(track, [new("P", "C", direction * 15)], [], new());
            Assert.Equal(10_500_000_000L, Assert.Single(pass.Observations).TimeNs);
        }
    }

    [Fact]
    public void Corridor_drift_keeps_time_but_long_exit_or_another_route_splits_passes()
    {
        var o = new SurveyProcessingOptions();
        TrackPoint[] Track(int end, bool other = false) => Enumerable.Range(0, 21)
            .Select(t => new TrackPoint(t * 1_000_000_000L, t * 5, 2, 5,
                OutsideCorridor: t >= 5 && t <= end, OtherRoute: other && t == 10)).ToArray();
        var shortDrift = Assert.Single(SurveyAlgorithms.SplitPasses(Track(8), o));
        var observation = Assert.Single(SurveyAlgorithms.Associate(shortDrift, [new("P", "C", 35)], [], o).Observations);
        Assert.Contains("gps_degraded", observation.Flags);
        Assert.Equal(7_000_000_000L, observation.TimeNs);
        Assert.Equal(2, SurveyAlgorithms.SplitPasses(Track(12), o).Length);
        Assert.Equal(2, SurveyAlgorithms.SplitPasses(Track(8, true), o).Length);
        var ambiguous = Track(8).Select(p => p with { OutsideCorridor = false, RouteAmbiguous = p.OutsideCorridor }).ToArray();
        Assert.Contains("route_ambiguous", Assert.Single(SurveyAlgorithms.Associate(ambiguous, [new("P", "C", 35)], [], o).Observations).Flags);
    }

    public static TheoryData<int, string> Scenarios
    {
        get
        {
            var data = new TheoryData<int,string>();
            foreach (int speed in new[] {15,20,25,30}) foreach (var scenario in SyntheticDrive.Cases) data.Add(speed,scenario);
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(Scenarios))]
    public void Synthetic_accuracy_by_speed_and_hard_case(int kmh, string scenario)
    {
        var scene = SyntheticDrive.Create(kmh, scenario);
        var fits = SurveyAlgorithms.FitClocks(scene.Lux, Options);
        var intervals = SurveyAlgorithms.AlignLux(scene.Lux, fits, Options);
        var passes = SurveyAlgorithms.ProcessRoute(scene.Track, scene.Poles, intervals, Options);
        var pass = Assert.Single(passes);
        var observations = pass.Observations;
        var errors = observations.Select(x => Math.Abs(x.TimeNs - scene.Truth[x.PoleId]) / 1e9).ToArray();
        // Denominator is ALL expected poles, including missing observations.
        double half = 100d * errors.Count(e => e <= .5) / scene.Poles.Length;
        double one = 100d * errors.Count(e => e <= 1) / scene.Poles.Length;
        var matched = observations.Where(x => x.Peak is not null).ToArray();
        var correct = matched.Count(x => scene.Lit.Contains(x.PoleId) && Math.Abs(x.Peak!.TimeNs - scene.Truth[x.PoleId]) < .5e9);
        double precision = matched.Length == 0 ? 0 : 100d * correct / matched.Length;
        int ambiguous = observations.Count(x => x.Flags.Contains("ambiguous_association"));
        output.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"METRIC|{scenario}|{kmh}|{observations.Length}/{scene.Poles.Length}|{half:F2}|{one:F2}|{correct}/{matched.Length}|{precision:F2}|{ambiguous}|{SurveyAlgorithms.Quantile(errors,.5):F4}|{SurveyAlgorithms.Quantile(errors,.95):F4}|{pass.GpsOffsetSeconds:F4}"));
        Assert.Equal(scene.Poles.Length, observations.Length);
        Assert.InRange(half, 70, 100);
        Assert.InRange(one, 90, 100);
        Assert.InRange(precision, 80, 100);
        Assert.True(pass.OffsetReliable);
        if (scenario is "delay" or "combined") Assert.InRange(pass.GpsOffsetSeconds, -1.05, -.5);
        if (scenario is "canopy" or "combined") Assert.Contains(observations, x => x.Flags.Contains("gps_degraded"));
        if (scenario is "off_run" or "combined")
        {
            Assert.All(observations.Where(x => x.PoleId is "P5" or "P6" or "P7"), p =>
            { Assert.Null(p.Peak); Assert.Contains("interpolated_between_anchors", p.Flags); });
        }
        if (scenario is "dim" or "combined") Assert.NotNull(Assert.Single(observations,x => x.PoleId == "P18").Peak);
        if (scenario is "paired" or "combined") Assert.All(observations.Where(x => x.PoleId is "P10" or "PAIR"), x =>
            { Assert.Null(x.Peak); Assert.Contains("paired_poles", x.Flags); Assert.Contains("peak_shared", x.Flags); });
        Assert.All(observations, x => Assert.DoesNotContain("speed_warning",x.Flags));
        if (scenario == "stop")
        {
            var stopped = Assert.Single(observations, x => x.PoleId == "P10");
            Assert.InRange(Math.Abs(stopped.TimeNs - scene.Truth["P10"]) / 1e9, 0, 1);
            Assert.Null(stopped.Peak); // Ten seconds of sustained light is not a passing peak.
            output.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"STOP|{kmh}|{Math.Abs(stopped.TimeNs - scene.Truth["P10"]) / 1e9:F4}"));
        }
        if (kmh == 30 && scenario == "stop") Assert.InRange(pass.ExcessTimeRatio, .8, .95);
        else if (kmh == 30)
        {
            Assert.All(observations, x => Assert.Contains("speed_excess", x.Flags));
            Assert.Equal(1, pass.ExcessTimeRatio);
        }
        else Assert.All(observations, x => Assert.DoesNotContain("speed_excess", x.Flags));
    }

    [Fact]
    public void Coverage_is_distinct_and_empty_denominator_is_unknown()
    {
        Assert.Null(SurveyAlgorithms.Coverage([], []));
        var track = new[] { new TrackPoint(0, 0, 1, null), new TrackPoint(10_000_000_000, 30, 1, null) };
        PolePosition[] poles = [new("P", "A", 20), new("Q", "A", 100)];
        var pass = SurveyAlgorithms.Associate(track, poles, [], Options);
        Assert.Equal(50, SurveyAlgorithms.Coverage(poles, [pass, pass]));
        Assert.Equal(3, Assert.Single(pass.Observations).SpeedMps);
    }
}

/// <summary>Simulated already-projected curved road. Arc length is the independent motion parameter;
/// production conversion from geographic coordinates is tested only in PostGIS.</summary>
internal sealed record SyntheticDrive(TrackPoint[] Track, ClockSample[] Lux, PolePosition[] Poles,
    Dictionary<string, long> Truth, HashSet<string> Lit, (double X,double Y)[] Road)
{
    public static readonly string[] Cases = ["reference", "delay", "canopy", "ble_batch", "ble_drop", "off_run", "dim", "paired", "headlight", "combined", "stop"];
    public static SyntheticDrive Create(int kmh, string scenario)
    {
        var random = new Random(15021);
        bool Has(string feature) => scenario == feature || scenario == "combined";
        double speed = kmh / 3.6, duration = 550 / speed;
        var poles = new List<PolePosition>();
        double position = 0;
        for (int i=1;i<=22;i++) { position += 20 + random.NextDouble()*5; poles.Add(new($"P{i}","COM-001",position)); }
        if (Has("paired")) poles.Add(new("PAIR","COM-001",poles[9].ChainageM+.6));
        var truth = poles.ToDictionary(x => x.PoleId, x => (long)Math.Round(x.ChainageM / speed * 1e9));
        double stopAt = poles[9].ChainageM / speed;
        if (scenario == "stop")
        {
            duration += 10;
            foreach (var p in poles.Where(p => p.ChainageM > poles[9].ChainageM)) truth[p.PoleId] += 10_000_000_000L;
        }
        double MovingTime(double t) => scenario != "stop" || t <= stopAt ? t : t <= stopAt + 10 ? stopAt : t - 10;
        double canopyTime = truth["P14"]/1e9;
        var track = Enumerable.Range(0, (int)duration + 1).Select(i =>
        {
            double noise = (random.NextDouble()-.5)*5;
            if (scenario == "stop" && i >= stopAt && i <= stopAt + 10) noise = 0;
            bool canopy = Has("canopy") && i >= canopyTime-3 && i <= canopyTime+4;
            return new TrackPoint(i*1_000_000_000L,
                scenario == "stop" && i >= stopAt && i <= stopAt + 10 ? poles[9].ChainageM :
                Math.Max(0, speed*(MovingTime(i)-(Has("delay")?.8:0)) + noise + (canopy ? 20*Math.Sin(i):0)), canopy?25:3, scenario == "stop" && i >= stopAt && i <= stopAt + 10 ? 0 : speed);
        }).ToArray();
        var lit = poles.Where(p => p.PoleId != "PAIR" && !(Has("off_run") && p.PoleId is "P5" or "P6" or "P7")).Select(p=>p.PoleId).ToHashSet();
        var samples = new List<ClockSample>();
        for (int i=0;i<(int)(duration*8);i++)
        {
            double t=i/8d, lux=1.5+(random.NextDouble()-.5)*.3;
            foreach(var p in poles.Where(p=>lit.Contains(p.PoleId)))
            {
                double amplitude = Has("dim") && p.PoleId=="P18" ? 3.5 : 60;
                double peakTime = scenario == "stop" && p.PoleId == "P10" ? MovingTime(t) : t;
                lux += amplitude*Math.Exp(-Math.Pow((peakTime-truth[p.PoleId]/1e9)/.25,2)/2);
            }
            if(Has("headlight"))
            {
                // A broad, plausible headlight peak near a real lamp, plus a narrow spike.
                lux += 100*Math.Exp(-Math.Pow((t-truth["P12"]/1e9-.55)/.15,2)/2);
                if(i==23) lux+=120;
            }
            long receipt=(Has("ble_batch") ? (i/4*4+3)*125_000_000L : i*125_000_000L);
            if(Has("ble_drop") && i%53==17) continue;
            samples.Add(new(0,i*125,receipt,i,lux));
        }
        var road = Enumerable.Range(0,56).Select(i => (2000*Math.Sin(i*10d/2000),2000*(1-Math.Cos(i*10d/2000)))).ToArray();
        return new(track,samples.ToArray(),poles.ToArray(),truth,lit,road);
    }
}
