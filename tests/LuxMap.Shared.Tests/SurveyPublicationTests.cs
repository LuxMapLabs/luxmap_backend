using LuxMap.Modules.Survey.Entities;
using LuxMap.Modules.Survey.Processing;
using LuxMap.Modules.Survey.Review;
using LuxMap.Shared.Contracts.Enums;
using LuxMap.Shared.Serialization;

namespace LuxMap.Shared.Tests;

public sealed class SurveyPublicationTests
{
    [Fact]
    public void Expected_poles_read_the_snapshot_exactly_as_the_worker_writes_it()
    {
        // SurveyProcessor serializes PolePosition with LuxMapJsonOptions; the review reads it back by name.
        var snapshot = System.Text.Json.JsonSerializer.Serialize(new { segments = new[] { "SEG-001" }, communes = new[] { "COM-001", "COM-002" },
            poles = new[] { new PolePosition("POLE-0001", "COM-001", 12.5), new PolePosition("POLE-10000", "COM-002", 40) } },
            LuxMapJsonOptions.Default);
        Assert.Equal([new ExpectedSurveyPole("POLE-0001", "COM-001"), new ExpectedSurveyPole("POLE-10000", "COM-002")],
            SurveyPublicationRules.ExpectedPoles(snapshot));
    }

    private static PoleObservation Observation(long id, double peak, double quality = .9, string direction = "forward") => new()
    {
        ObservationId = id, PoleId = "POLE-0001", CommuneId = "COM-001", RunId = id,
        Pass = new() { SegmentId = "SEG-001", Direction = direction, QualityFlags = "{}" },
        CvState = "on", CvConfidence = quality, AssociationConfidence = .9,
        ClassifiedAs = FixtureStatus.Normal, PeakLux = peak, QualityFlags = "[]", ReasonCodes = "[\"baseline_missing\"]",
        ObservedAt = DateTime.UnixEpoch.AddDays(id)
    };

    [Fact]
    public void Representative_uses_quality_not_brightness_and_stable_ties()
    {
        var good = Observation(1, 20, .95); var bright = Observation(2, 900, .8);
        Assert.Same(good, SurveyPublicationRules.Choose([bright, good]).Observation);
        bright.CvConfidence = good.CvConfidence;
        Assert.Same(good, SurveyPublicationRules.Choose([bright, good]).Observation);
    }

    [Fact]
    public void Conflicting_passes_publish_unknown_without_confidence_ratio_or_fault()
    {
        var on = Observation(1, 20); on.BaselineRatio = .5; on.DimEvaluationEligible = true;
        var off = Observation(2, 1); off.CvState = "off"; off.ClassifiedAs = FixtureStatus.Out;
        var choice = SurveyPublicationRules.Choose([on, off]);
        Assert.Equal(FixtureStatus.Unknown, choice.Status); Assert.Null(choice.Confidence); Assert.Null(choice.Ratio);
        Assert.False(choice.DimEligible); Assert.Contains("on_off_conflict", choice.Reasons);
        Assert.Null(SurveyPublicationRules.FaultFor(choice.Status));
    }

    [Theory]
    [InlineData(-1, false)] [InlineData(0, false)] [InlineData(1, true)]
    public void Late_and_equal_time_never_replace_current(int offset, bool expected)
        => Assert.Equal(expected, SurveyPublicationRules.IsNewer(DateTime.UnixEpoch.AddSeconds(offset), DateTime.UnixEpoch));

    [Theory]
    [InlineData(FaultType.LampOut, false, Severity.Medium)] [InlineData(FaultType.LampOut, true, Severity.High)]
    [InlineData(FaultType.LampDim, false, Severity.Low)] [InlineData(FaultType.LampDim, true, Severity.Medium)]
    public void Provisional_severity_and_sensitive_pole_escalation(FaultType type, bool sensitive, Severity expected)
        => Assert.Equal(expected, SurveyPublicationRules.SeverityFor(type, sensitive, new()));

    [Fact]
    public void Configurable_severity_caps_at_high()
    {
        var options = new SurveyReviewOptions { LampDimSeverity = Severity.High };
        Assert.Equal(Severity.High, SurveyPublicationRules.SeverityFor(FaultType.LampDim, true, options));
        options.LampOutSeverity = Severity.Critical;
        Assert.False(options.IsValid());
    }

    [Theory]
    [InlineData("peak_shared")] [InlineData("paired_poles")] [InlineData("lux_gap")]
    [InlineData("lux_saturated")] [InlineData("ambiguous_association")]
    public void Unreliable_peak_never_becomes_a_member(string flag)
    {
        var row = Observation(1, 100); row.QualityFlags = "[\"" + flag + "\"]";
        Assert.False(SurveyPublicationRules.Eligible(row));
    }

    [Fact]
    public void Median_requires_enough_members_in_the_same_direction_and_is_not_mean()
    {
        var rows = new[] { Observation(1, 10), Observation(2, 20), Observation(3, 900), Observation(4, 800, direction: "reverse") };
        Assert.Equal(20, SurveyPublicationRules.Median(rows, "forward", 3));
        Assert.Null(SurveyPublicationRules.Median(rows, "forward", 4));
        Assert.Null(SurveyPublicationRules.Median(rows, "reverse", 3));
        Assert.Equal(15, SurveyPublicationRules.Median(rows.Take(2), "forward", 2));
    }

    [Fact]
    public void Members_require_on_known_and_nonnegative_finite_lux_but_not_an_existing_baseline()
    {
        var row = Observation(1, 100); Assert.True(SurveyPublicationRules.Eligible(row));
        row.ClassifiedAs = FixtureStatus.Unknown; Assert.False(SurveyPublicationRules.Eligible(row));
        row.ClassifiedAs = FixtureStatus.Normal; row.CvState = "off"; Assert.False(SurveyPublicationRules.Eligible(row));
        row.CvState = "on";
        foreach (var value in new[] { -1d, double.NaN, double.PositiveInfinity })
        { row.PeakLux = value; Assert.False(SurveyPublicationRules.Eligible(row)); }
    }
    [Fact]
    public void Zero_peak_is_a_member_but_zero_median_cannot_be_a_divisor()
    {
        var zero = Observation(1, 0);
        Assert.True(SurveyPublicationRules.Eligible(zero));
        Assert.Equal(10, SurveyPublicationRules.Median([zero, Observation(2, 10), Observation(3, 20)], "forward", 3));
        Assert.Null(SurveyPublicationRules.Median([zero, Observation(2, 0), Observation(3, 20)], "forward", 3));
    }
    [Fact]
    public void Gradual_dimming_does_not_lower_its_own_baseline()
    {
        var members = new List<PoleObservation> { Observation(1, 100), Observation(2, 100), Observation(3, 100) };
        for (int day = 4; day < 14; day++)
        {
            var baseline = SurveyPublicationRules.Median(members, "forward", 3);
            Assert.Equal(100, baseline);
            var lux = day < 9 ? 60 : 70;
            var classification = LuxMap.Modules.Survey.Processing.Frames.FrameClassification.Classify(
                new("on", .95, "frame", 1, "associated"), lux, baseline, [], .8);
            Assert.Equal(FixtureStatus.Dim, classification.Status);
            var observation = Observation(day, lux); observation.ClassifiedAs = classification.Status;
            members.Add(observation);
            Assert.Equal(3, SurveyPublicationRules.Members(members, "forward", null).Count());
        }
    }

    [Fact]
    public void Replacement_requires_new_members_and_lookup_rejects_the_old_fixture()
    {
        var observations = Enumerable.Range(1, 6).Select(i => Observation(i, i < 4 ? 100 : 200)).ToArray();
        var installed = DateOnly.FromDateTime(observations[3].ObservedAt);
        Assert.Empty(SurveyPublicationRules.Members(observations.Take(3), "forward", installed));
        Assert.Null(SurveyPublicationRules.Median(SurveyPublicationRules.Members(observations.Take(5), "forward", installed), "forward", 3));
        Assert.Equal(200, SurveyPublicationRules.Median(SurveyPublicationRules.Members(observations, "forward", installed), "forward", 3));
        Assert.Equal(6, SurveyPublicationRules.Members(observations, "forward", null).Count());
    }

    [Fact]
    public void Replacement_lookup_uses_only_the_active_fixture()
    {
        var old = new LuminanceBaseline { PoleId = "pole", CommuneId = "commune", Direction = "forward", CreatedBy = "user", FixtureId = "old" };
        var fresh = new LuminanceBaseline { PoleId = "pole", CommuneId = "commune", Direction = "forward", CreatedBy = "user", FixtureId = "new" };
        Assert.Empty(SurveyBaselineLookup.ForFixture(new[] { old }.AsQueryable(), "new"));
        Assert.Same(fresh, Assert.Single(SurveyBaselineLookup.ForFixture(new[] { old, fresh }.AsQueryable(), "new")));
        Assert.Empty(SurveyBaselineLookup.ForFixture(new[] { old, fresh }.AsQueryable(), null));
        fresh.FixtureId = null;
        Assert.Same(fresh, Assert.Single(SurveyBaselineLookup.ForFixture(new[] { old, fresh }.AsQueryable(), null)));
    }

    [Fact]
    public void Preview_uses_all_passes_even_when_the_conflict_is_on_another_page()
    {
        var on = Observation(1, 100); var off = Observation(2, 0);
        off.CvState = "off"; off.ClassifiedAs = FixtureStatus.Out; off.CvConfidence = .95;
        var page = SurveyReviewService.Preview([on], [on, off]);
        Assert.Equal(FixtureStatus.Normal, page[0].ClassifiedAs);
        Assert.Equal(FixtureStatus.Unknown, page[0].PublishedAs); Assert.False(page[0].IsRepresentative);
        var next = Assert.Single(SurveyReviewService.Preview([off], [on, off]));
        Assert.Equal(FixtureStatus.Unknown, next.PublishedAs); Assert.True(next.IsRepresentative);
    }

    private const string PartialSnapshot = """
        {"poles":[{"pole_id":"POLE-10000","commune_id":"COM-002"},
                  {"pole_id":"POLE-0001","commune_id":"COM-001"},
                  {"pole_id":"POLE-9999","commune_id":"COM-003"},
                  {"pole_id":"POLE-9999","commune_id":"COM-003"}]}
        """;
    private static SurveySweep PartialSweep() => new() { SweepId = "SWP-001", WorkOrderId = "WO-001",
        CommuneId = "COM-001", CapturedBy = "USR-001", CreateRequestHash = "unused",
        UtcAnchor = DateTime.UnixEpoch, ElapsedAnchorNs = 1_000_000_000, EndedElapsedNs = 11_000_001_999 };

    [Fact]
    public void Publication_set_is_the_distinct_union_including_observations_outside_snapshot()
    {
        var first = Observation(1, 100); var other = Observation(2, 100); other.PoleId = "POLE-0002";
        var targets = SurveyPublicationRules.PublicationSet(PartialSnapshot, [first, first, other]);
        Assert.Equal(4, targets.Length);
        Assert.Same(first, targets.Single(t => t.PoleId == first.PoleId).Choice!.Observation);
        Assert.Same(other, targets.Single(t => t.PoleId == other.PoleId).Choice!.Observation);
        Assert.Null(targets.Single(t => t.PoleId == "POLE-9999").Choice);
        Assert.Equal(3, SurveyPublicationRules.PublicationSet(PartialSnapshot, []).Length);
        Assert.Single(SurveyPublicationRules.PublicationSet("{}", [first]));
    }

    [Fact]
    public void Unobserved_history_is_unknown_at_sweep_end_without_measurements_or_fault()
    {
        var sweep = PartialSweep(); var published = DateTime.UnixEpoch.AddDays(200);
        var target = SurveyPublicationRules.PublicationSet(PartialSnapshot, []).First();
        var history = SurveyPublicationRules.History(target, sweep, 5, "LOCKED-COMMUNE", published, "reviewer");
        Assert.Equal(DateTime.UnixEpoch.AddSeconds(10).AddTicks(10), history.EvaluatedAt);
        Assert.Equal(sweep.AtElapsed(sweep.EndedElapsedNs), history.EvaluatedAt);
        Assert.Equal(published, history.PublishedAt); Assert.Equal("LOCKED-COMMUNE", history.CommuneId);
        Assert.Null(history.ObservationId); Assert.Null(history.PeakLux); Assert.Null(history.CvState);
        Assert.Null(history.BaselineId); Assert.Null(history.BaselineRatio); Assert.Null(history.StatusConfidence);
        Assert.Equal(FixtureStatus.Unknown, history.ClassifiedAs); Assert.False(history.DimEvaluationEligible);
        Assert.Equal(new[] { "not_observed" }, SurveyPublicationRules.Flags(history.ReasonCodes));
        Assert.Null(SurveyPublicationRules.FaultFor(history.ClassifiedAs));
        Assert.False(SurveyPublicationRules.IsNewer(history.EvaluatedAt, history.EvaluatedAt.AddSeconds(1)));
        Assert.False(SurveyPublicationRules.IsNewer(history.EvaluatedAt, history.EvaluatedAt));
        Assert.True(SurveyPublicationRules.IsNewer(history.EvaluatedAt, history.EvaluatedAt.AddSeconds(-1)));
    }

    [Fact]
    public void Observed_history_still_uses_capture_time_and_observation_identity()
    {
        var observation = Observation(1, 100);
        var target = Assert.Single(SurveyPublicationRules.PublicationSet("{}", [observation]));
        var history = SurveyPublicationRules.History(target, PartialSweep(), 1, observation.CommuneId, DateTime.UtcNow, "reviewer");
        Assert.Equal(observation.ObservedAt, history.EvaluatedAt); Assert.Equal(observation.ObservationId, history.ObservationId);
        Assert.Equal(FixtureStatus.Normal, history.ClassifiedAs); Assert.Equal(100, history.PeakLux);
    }

    [Fact]
    public void Unobserved_preview_has_no_measurements_and_missing_poles_have_numeric_stable_order()
    {
        var ids = SurveyPublicationRules.UnobservedPoles(PartialSnapshot, ["POLE-0001", "POLE-0002"]);
        Assert.Equal(new[] { "POLE-9999", "POLE-10000" }, ids);
        var item = SurveyReviewService.UnobservedPreview(ids[0], 5, PartialSweep());
        Assert.Null(item.ObservationId); Assert.Null(item.PassId); Assert.Null(item.Direction);
        Assert.Null(item.CvState); Assert.Null(item.CvConfidence); Assert.Null(item.PeakLux);
        Assert.Null(item.BaselineId); Assert.Null(item.BaselineValue); Assert.Null(item.BaselineRatio);
        Assert.Null(item.FrameId); Assert.Null(item.AssociationConfidence);
        Assert.Equal(FixtureStatus.Unknown, item.PublishedAs); Assert.Equal(FixtureStatus.Unknown, item.ClassifiedAs);
        Assert.Equal(new[] { "not_observed" }, item.ReasonCodes); Assert.False(item.IsRepresentative);
        Assert.False(item.DimEvaluationEligible); Assert.Empty(item.QualityFlags);
        Assert.Equal(PartialSweep().AtElapsed(PartialSweep().EndedElapsedNs), item.EvaluatedAt);
    }

    [Fact]
    public void Whole_run_requires_unobserved_snapshot_and_current_communes_too()
    {
        var required = SurveyPublicationRules.RequiredCommunes(PartialSnapshot, ["COM-001", "COM-004"], ["COM-005"]);
        var scope = LuxMap.Shared.Authorization.CommuneScope.ForCommunes(["COM-001", "COM-004"]);
        Assert.Contains("COM-002", required); Assert.Contains("COM-003", required); Assert.Contains("COM-005", required);
        Assert.False(required.All(scope.Allows));
    }
}
