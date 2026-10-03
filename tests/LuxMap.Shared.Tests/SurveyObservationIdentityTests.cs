using LuxMap.Modules.Survey.Entities;
using LuxMap.Modules.Survey.Processing;
using LuxMap.Modules.Survey.Processing.Frames;
using LuxMap.Shared.Contracts.Enums;

namespace LuxMap.Shared.Tests;

public sealed class SurveyObservationIdentityTests
{
    [Fact]
    public async Task Turnaround_preserves_each_pass_classification_in_rows_prepared_for_saving()
    {
        var track = Enumerable.Range(0, 21).Select(i => new TrackPoint(i * 1_000_000_000L,
            (i <= 10 ? i : 20 - i) * 10, 2, 10)).ToArray();
        var passes = SurveyAlgorithms.ProcessRoute(track, [new("pole", "commune", 100)], [], new());
        Assert.Equal(2, passes.Length);
        var first = Assert.Single(passes[0].Observations);
        var second = Assert.Single(passes[1].Observations);
        Assert.Equal(first.TimeNs, second.TimeNs);
        var result = new FramePipelineResult();
        var run = new SurveyProcessingRun { SweepId = "sweep", CommuneId = "commune", InputHash = new('a', 64), SettingsSnapshot = "{}", ResultState = "completed", Stage = "completed" };
        await SurveyFramePipeline.ClassifyObservationsAsync(result, run,
            passes.Select(p => ("segment", 100d, p)).ToList(), [Pole("pole")],
            new("left", [new(0, 0, 20_000_000_000, 0, 20_000_000_000, 1, 1000)]),
            new Dictionary<int, List<DetectedFrame>> { [0] = [Frame(first.TimeNs)] },
            new EmptySurveyBaselineLookup(), new(), default);
        Assert.Equal(2, result.Observations.Count);
        var sweep = new SurveySweep { WorkOrderId = "order", CommuneId = "commune", CapturedBy = "user", CreateRequestHash = new('a', 64), UtcAnchor = DateTime.UnixEpoch, DataSource = DataSource.Simulated };
        var forward = new SurveyPass { PassNo = 0, Run = run, SegmentId = "segment", Direction = "forward", QualityFlags = "{}" };
        var reverse = new SurveyPass { PassNo = 1, Run = run, SegmentId = "segment", Direction = "reverse", QualityFlags = "{}" };
        var row0 = SurveyProcessor.CreateObservation(forward, run, sweep, first, result);
        var row1 = SurveyProcessor.CreateObservation(reverse, run, sweep, second, result);
        Assert.Same(forward, row0.Pass); Assert.Same(reverse, row1.Pass);
        Assert.Equal(FixtureStatus.Normal, row0.ClassifiedAs);
        Assert.Equal("on", row0.CvState); Assert.Equal("frame", row0.RepresentativeFrameId);
        Assert.Equal(FixtureStatus.Unknown, row1.ClassifiedAs);
        Assert.Null(row1.CvState); Assert.Null(row1.RepresentativeFrameId);
        Assert.Contains("outside_camera_side", row1.ReasonCodes, StringComparison.Ordinal);
        Assert.Equal(100, run.DetectionCoveragePct);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Shared_prediction_across_passes_is_valid_only_for_the_same_pole(bool samePole)
    {
        string other = samePole ? "pole" : "other";
        var first = Passage("pole", 10_000_000_000);
        var second = Passage(other, 11_000_000_000);
        var track = new[] { new TrackPoint(0, 0, 2, 10), new TrackPoint(20_000_000_000, 100, 2, 10) };
        var result = new FramePipelineResult();
        var run = new SurveyProcessingRun { SweepId = "sweep", CommuneId = "commune", InputHash = new('a', 64), SettingsSnapshot = "{}", ResultState = "completed", Stage = "completed" };
        await SurveyFramePipeline.ClassifyObservationsAsync(result, run,
            [("segment", 100d, new(track, [first], 0, true, 0)), ("segment", 100d, new(track, [second], 0, true, 0))],
            samePole ? [Pole("pole")] : [Pole("pole"), Pole(other)],
            new("front", [new(0, 0, 20_000_000_000, 0, 20_000_000_000, 1, 1000)]),
            new Dictionary<int, List<DetectedFrame>> { [0] = [Frame(10_000_000_000)] },
            new EmptySurveyBaselineLookup(), new(), default);
        Assert.Equal(2, result.Observations.Count);
        Assert.All(result.Observations.Values, cv =>
        {
            Assert.Equal(samePole ? FixtureStatus.Normal : FixtureStatus.Unknown, cv.Classification.Status);
            Assert.Equal(samePole ? "associated" : "shared_cv_evidence", cv.Association.Reason);
            Assert.Equal(samePole ? "on" : null, cv.Association.State);
            Assert.Null(cv.Classification.BaselineRatio);
            Assert.False(cv.Classification.DimEvaluationEligible);
        });
        Assert.Equal(samePole ? 100 : 0, run.DetectionCoveragePct);
        Assert.Equal(0, run.DimCoveragePct);
    }

    private static Passage Passage(string pole, long time) => new(pole, "commune", 50, time, null, 10, .9, []);
    private static DetectedFrame Frame(long time) => new("frame", time, [new(0, "on", .95, .1, .1, .1, .1)]);
    private static ProjectedPole Pole(string id) => new()
    {
        PoleId = id, CommuneId = "commune", SegmentId = "segment", SideOfRoute = 1,
        ChainageM = 100, LengthM = 100, GeometryJson = "{}", RouteGeometryJson = "{}"
    };
}
