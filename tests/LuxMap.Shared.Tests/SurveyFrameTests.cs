using System.Security.Cryptography;
using System.Text.Json;
using LuxMap.Modules.Survey.Processing;
using LuxMap.Modules.Survey.Processing.Frames;
using LuxMap.Shared.Contracts.Enums;
using LuxMap.Modules.Survey;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace LuxMap.Shared.Tests;

public sealed class SurveyFrameTests
{
    private static readonly Association On = new("on", .95, "frame", 1, "associated");
    [Theory]
    [InlineData("off", null, null, FixtureStatus.Out, null, false)]
    [InlineData("on", 7d, 10d, FixtureStatus.Dim, .7, true)]
    [InlineData("on", 8d, 10d, FixtureStatus.Normal, .8, true)]
    [InlineData("on", 12d, 10d, FixtureStatus.Normal, 1.2, true)]
    [InlineData("on", 0d, 10d, FixtureStatus.Dim, 0d, true)]
    [InlineData("on", 10d, null, FixtureStatus.Normal, null, false)]
    [InlineData("on", null, 10d, FixtureStatus.Normal, null, false)]
    [InlineData(null, 1d, 10d, FixtureStatus.Unknown, null, false)]
    public void Classification_preserves_cv_and_brightness_semantics(string? state, double? peak, double? baseline,
        FixtureStatus expected, double? ratio, bool eligible)
    {
        var result = FrameClassification.Classify(On with { State = state }, peak, baseline, [], .8);
        Assert.Equal(expected, result.Status); Assert.Equal(ratio, result.BaselineRatio); Assert.Equal(eligible, result.DimEvaluationEligible);
    }
    [Theory]
    [InlineData("lux_gap")] [InlineData("peak_shared")] [InlineData("paired_poles")] [InlineData("lux_saturated")]
    public void Unusable_lux_is_normal_without_dim_evaluation(string reason)
    {
        var result = FrameClassification.Classify(On, 1, 10, [reason], .8);
        Assert.Equal(FixtureStatus.Normal, result.Status); Assert.Null(result.BaselineRatio);
        Assert.False(result.DimEvaluationEligible); Assert.Contains(reason, result.Reasons);
    }
    [Fact]
    public void Video_gap_is_unknown_even_with_off_and_threshold_is_configurable()
    {
        Assert.Equal(FixtureStatus.Unknown, FrameClassification.Classify(On with { State = "off" }, null, null, ["video_gap"], .8).Status);
        Assert.Equal(FixtureStatus.Normal, FrameClassification.Classify(On, 7, 10, [], .6).Status);
        Assert.Equal(FixtureStatus.Dim, FrameClassification.Classify(On, 7, 10, [], .9).Status);
    }
    [Theory]
    [InlineData(double.NaN)] [InlineData(double.PositiveInfinity)] [InlineData(0)] [InlineData(-1)]
    public void Invalid_baselines_are_rejected(double value)
        => Assert.Throws<ArgumentOutOfRangeException>(() => FrameClassification.Classify(On, 1, value, [], .8));

    [Theory]
    [InlineData(double.NaN)] [InlineData(double.PositiveInfinity)] [InlineData(0)] [InlineData(1.1)]
    public void Invalid_threshold_cannot_enter_classification(double threshold)
        => Assert.Throws<ArgumentOutOfRangeException>(() => FrameClassification.Classify(On, 1, 10, [], threshold));
    [Theory]
    [InlineData(double.NaN)] [InlineData(double.PositiveInfinity)] [InlineData(-1)]
    public void Invalid_peak_cannot_enter_classification(double peak)
        => Assert.Throws<ArgumentOutOfRangeException>(() => FrameClassification.Classify(On, peak, 10, [], .8));
    [Fact]
    public void Ratio_overflow_is_not_emitted_or_used_for_dim_metrics()
    {
        var result = FrameClassification.Classify(On, double.MaxValue, double.Epsilon, [], .8);
        Assert.Equal(FixtureStatus.Normal, result.Status); Assert.Null(result.BaselineRatio);
        Assert.False(result.DimEvaluationEligible); Assert.Contains("ratio_not_finite", result.Reasons);
        Assert.Contains("baseline_missing", FrameClassification.Classify(On, 1, null, [], .8).Reasons);
        Assert.Contains("peak_missing", FrameClassification.Classify(On, null, 10, [], .8).Reasons);
    }
    [Theory]
    [InlineData("on", FixtureStatus.Normal)]
    [InlineData("off", FixtureStatus.Out)]
    public void Lux_ambiguity_preserves_cv_but_excludes_dim_evaluation(string state, FixtureStatus status)
    {
        var result = SurveyFramePipeline.EvaluateObservation(On with { State = state }, 1, 10, ["ambiguous_association"], .8);
        Assert.Equal(state, result.Association.State);
        Assert.Equal(status, result.Classification.Status);
        Assert.Null(result.Classification.BaselineRatio);
        Assert.False(result.Classification.DimEvaluationEligible);
        if (state == "on") Assert.Contains("ambiguous_association", result.Classification.Reasons);
    }
    [Fact]
    public void Route_ambiguity_blocks_cv_even_with_a_clear_detection()
    {
        var result = SurveyFramePipeline.EvaluateObservation(On, 1, 10, ["route_ambiguous"], .8);
        Assert.Null(result.Association.State); Assert.Equal(FixtureStatus.Unknown, result.Classification.Status);
    }
    [Fact]
    public void Default_window_covers_approach_and_gps_lag()
    {
        var options = new SurveyFrameOptions();
        Assert.Equal(3, options.BeforeSeconds); Assert.Equal(.5, options.AfterSeconds); Assert.Equal(5, options.FramesPerSecond);
    }
    [Theory]
    [InlineData("on")] [InlineData("off")]
    public void A_single_fast_moving_lamp_does_not_need_overlapping_boxes(string state)
    {
        var result = Match(new DetectedFrame("a", 0, [Lamp(state, .05, .05)]),
            new DetectedFrame("b", 200_000_000, [Lamp(state, .3, .15)]));
        Assert.Equal(state, result.State); Assert.Equal("associated", result.Reason); Assert.Equal(1, result.TrackCount);
        Assert.Equal("b", result.RepresentativeFrameId);
    }
    private static Prediction Lamp(string label = "on", double x = .1, double width = .2, double confidence = .9)
        => new(0, label, confidence, x, .1, width, .2);
    private static Association Match(params DetectedFrame[] frames)
        => DetectionAssociation.Match(frames, 0, 2_000_000_000, "front", 1, new());
    [Fact]
    public void Track_counts_one_lamp_once_and_selects_quality_not_time_or_lux()
    {
        var result = Match(new DetectedFrame("a", 0, [Lamp()]), new("b", 400_000_000, [Lamp(width: .25)]), new("c", 800_000_000, [Lamp()]));
        Assert.Equal(1, result.TrackCount); Assert.Equal("b", result.RepresentativeFrameId); Assert.Equal("on", result.State);
    }
    [Fact]
    public void Opposing_lamps_are_separated_by_side_and_reverse_direction()
    {
        var frames = new[] { new DetectedFrame("a", 0, [Lamp(), Lamp("off", .7)]) };
        Assert.Equal("on", DetectionAssociation.Match(frames, 0, 1, "front", 1, new()).State);
        Assert.Equal("off", DetectionAssociation.Match(frames, 0, 1, "front", -1, new()).State);
        Assert.Null(DetectionAssociation.Match(frames, 0, 1, "left", -1, new()).State);
    }
    [Fact]
    public void Conflict_or_two_tracks_remain_unknown_and_outside_window_is_ignored()
    {
        Assert.Equal("on_off_conflict", Match(new DetectedFrame("a", 0, [Lamp()]), new("b", 400_000_000, [Lamp("off")])).Reason);
        Assert.Equal("ambiguous_tracks", Match(new DetectedFrame("a", 0, [Lamp(width: .05), Lamp(x: .35, width: .05)])).Reason);
        Assert.Equal("no_detection", Match(new DetectedFrame("a", 3_000_000_000, [Lamp()])).Reason);
        Assert.Equal("frame_poor", Match(new DetectedFrame("a", 0, [Lamp(confidence: .2)])).Reason);
        Assert.Equal("no_detection", Match(new DetectedFrame("a", 0, [])).Reason);
    }
    [Fact]
    public void Overlapping_observations_cannot_count_the_same_lamp_twice()
    {
        var first = Match(new DetectedFrame("a", 0, [Lamp()]), new DetectedFrame("b", 400_000_000, [Lamp()]));
        var second = Match(new DetectedFrame("b", 400_000_000, [Lamp()]), new DetectedFrame("c", 800_000_000, [Lamp()]));
        Assert.All(DetectionAssociation.ResolveSharedEvidence([("pole-a", first), ("pole-b", second)]), x =>
        { Assert.Null(x.State); Assert.Equal("shared_cv_evidence", x.Reason); });
    }
    [Theory]
    [InlineData("success", 0)] [InlineData("success", 2)] [InlineData("malformed", 1)] [InlineData("error", 1)] [InlineData("timeout", 1)]
    public async Task Fake_manifest_distinguishes_empty_errors_malformed_and_timeout(string outcome, int count)
    {
        byte[] bytes = [1, 2, 3];
        var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
        var fake = new ManifestOnOffDetector(JsonSerializer.Serialize(new Dictionary<string, FakeDetectionCase>
        { [hash] = new(outcome, Enumerable.Range(0, count).Select(i => Lamp(i == 0 ? "on" : "off", i == 0 ? .1 : .7) with { ItemNo = i }).ToArray()) }));
        var input = new FrameInput("request", "frame", bytes, 100, 80, fake.Artifact.Version);
        if (outcome == "timeout")
        {
            using var stop = new CancellationTokenSource(30);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fake.DetectAsync(input, stop.Token));
        }
        else if (outcome != "success") await Assert.ThrowsAsync<ProcessingFailure>(() => fake.DetectAsync(input, default));
        else
        {
            var first = await fake.DetectAsync(input, default); var second = await fake.DetectAsync(input, default);
            Assert.Equal(count, first.Predictions.Length); Assert.Equal(first.RawOutput, second.RawOutput);
            Assert.StartsWith("fake-", first.ModelVersion);
        }
    }
    [Theory]
    [InlineData(double.NaN, .1)] [InlineData(double.PositiveInfinity, .1)] [InlineData(.8, -.1)] [InlineData(.8, .95)]
    public void Detector_rejects_nonfinite_and_outside_bbox(double confidence, double x)
    {
        var input = new FrameInput("r", "f", [], 100, 80, "v");
        Assert.Throws<ProcessingFailure>(() => DetectorValidation.Validate(input, new("r", "f", "v", 100, 80, [Lamp(confidence: confidence, x: x)], "{}")));
    }
    [Theory]
    [InlineData("Production", false)] [InlineData("Staging", false)] [InlineData("Development", true)] [InlineData("Test", true)]
    public void Fake_is_refused_outside_development_or_test(string environment, bool allowed)
    {
        var options = new SurveyFrameOptions { Detector = "fake" };
        if (allowed) options.ValidateEnvironment(environment);
        else Assert.Throws<InvalidOperationException>(() => options.ValidateEnvironment(environment));
    }
    [Fact]
    public async Task Production_startup_validation_rejects_explicit_fake_configuration()
    {
        using var host = new HostBuilder().UseEnvironment("Production").ConfigureServices((_, services) =>
        {
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
                { ["SurveyProcessing:Frames:Detector"] = "fake" }).Build();
            new SurveyModule().RegisterServices(services, configuration);
            // Isolate options startup validation, without constructing a worker or any infrastructure.
            services.RemoveAll<IHostedService>();
        }).Build();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => host.StartAsync());
        Assert.Contains("Development/Test", error.Message);
    }
    private const string Mapping = """
        {"sensor_timestamp_source":"REALTIME","mount":{"camera_side":"front"},"clips":[
        {"clip_no":0,"first_pts_ns":-100,"last_pts_ns":900,"first_sensor_timestamp_ns":10000,"last_sensor_timestamp_ns":11000,"time_base_num":1,"time_base_den":1000}]}
        """;
    [Fact]
    public void Mapping_preserves_negative_pts_and_exact_nanoseconds()
    {
        var mapping = VideoClockMapping.Parse(Mapping, [0]); var clock = Assert.Single(mapping.Clips);
        Assert.Equal(10500, clock.ToPhone(400)); Assert.Equal(400, clock.ToPts(10500));
    }
    [Fact]
    public void Mapping_accepts_decimal_strings_without_losing_large_phone_timestamps()
    {
        var json = Mapping.Replace("10000", "\"9007199254740993\"").Replace("11000", "\"9007199254741993\"");
        Assert.Equal(9007199254741493, VideoClockMapping.Parse(json, [0]).Clips[0].ToPhone(400));
    }
    [Theory]
    [InlineData("REALTIME", "UNKNOWN")] [InlineData("11000", "11001")]
    [InlineData("\"time_base_den\":1000", "\"time_base_den\":0")]
    [InlineData("\"clips\"", "\"missing\"")]
    public void Missing_or_inconsistent_mapping_fails_clearly(string from, string to)
        => Assert.Equal("CLOCK_VIDEO_MAPPING", Assert.Throws<ProcessingFailure>(() => VideoClockMapping.Parse(Mapping.Replace(from, to), [0])).Code);
    [Fact]
    public void Vfr_selection_uses_pts_and_never_another_clip_for_a_gap()
    {
        long[] pts = [0, 35_000_000, 68_333_333, 120_000_000, 190_000_000];
        var clock = new ClipClock(0, 0, pts[^1], 1000, pts[^1] + 1000, 1, 1000000000);
        Assert.Equal(new[] { 1, 3 }, FfmpegFrameExtractor.SelectFrames(pts, clock, [new(100_000_000, 30_000_000, 150_000_000)], 20));
        Assert.Empty(FfmpegFrameExtractor.SelectFrames(pts, clock, [new(300_000_000, 0, 400_000_000)], 20));
    }
    [Fact]
    public async Task Long_operation_keeps_lease_alive_and_cancels_when_fence_is_lost()
    {
        int beats = 0;
        var result = await SurveyFramePipeline.WithHeartbeat(async ct => { await Task.Delay(80, ct); return 7; },
            ct => { beats++; return Task.CompletedTask; }, .03, 2, default);
        Assert.Equal(7, result); Assert.True(beats > 0);
        bool cancelled = false;
        var error = await Assert.ThrowsAsync<ProcessingFailure>(() => SurveyFramePipeline.WithHeartbeat(async ct =>
        { try { await Task.Delay(10000, ct); return 0; } finally { cancelled = ct.IsCancellationRequested; } },
            _ => throw new ProcessingFailure("LEASE_LOST", "lease"), .03, 2, default));
        Assert.Equal("LEASE_LOST", error.Code); Assert.True(cancelled);
    }
}
