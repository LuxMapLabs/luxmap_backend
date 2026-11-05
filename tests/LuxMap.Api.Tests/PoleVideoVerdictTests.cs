using LuxMap.Modules.WorkOrders;
using LuxMap.Shared.Media;

namespace LuxMap.Api.Tests;

/// <summary>POLE-VIDEO PV-c2 (D-4): the verdict of a clip from the votes of its frames, on its own.</summary>
public sealed class PoleVideoVerdictTests
{
    private static readonly PoleVideoOptions Defaults = new();

    private static FrameVote On(double confidence, double luma = .3) => new("on", confidence, null, luma);
    private static FrameVote Off(double confidence, double luma = .3) => new("off", confidence, null, luma);
    private static FrameVote None(string? reason, double luma = .3) => new("inconclusive", null, reason, luma);

    [Fact]
    public void Frames_that_agree_give_that_label_with_the_median_confidence_and_the_frame_nearest_it_as_the_photo()
    {
        var verdict = PoleVideoVerdict.Decide([On(.6), On(.9), On(.8), On(.7), On(.85)], Defaults);
        Assert.Equal("on", verdict.State);
        Assert.Equal(.8, verdict.Confidence);
        Assert.Equal(2, verdict.RepresentativeIndex);                              // .8 is nearest .8 — not the most confident frame (.9)
        Assert.Equal((5, 0, 0), (verdict.OnFrames, verdict.OffFrames, verdict.DaylightFrames));
        Assert.Equal((.6, .8, .9), (verdict.ConfidenceMin!.Value, verdict.ConfidenceMedian!.Value, verdict.ConfidenceMax!.Value));
        Assert.Null(verdict.ReasonCode);
    }

    [Fact]
    public void The_median_of_an_even_number_of_frames_is_the_mean_of_the_middle_two_and_a_tie_picks_the_earlier_frame()
    {
        var verdict = PoleVideoVerdict.Decide([Off(.5), Off(.9), Off(.7), Off(.7)], Defaults);
        Assert.Equal(.7, verdict.Confidence);
        Assert.Equal(2, verdict.RepresentativeIndex);
        Assert.Equal(.8, PoleVideoVerdict.Median([.5, .9, .7, .9]));
    }

    [Fact]
    public void A_minority_of_frames_that_disagree_is_tolerated_up_to_the_agreement_limit_and_still_counts_in_the_spread()
    {
        // 10 frames: 7 ON, 3 OFF in the middle ⇒ 70 % agree, the first and the last say ON.
        var votes = new[] { On(.9), On(.9), Off(.4), Off(.4), Off(.4), On(.9), On(.9), On(.9), On(.9), On(.9) };
        var verdict = PoleVideoVerdict.Decide(votes, Defaults);
        Assert.Equal("on", verdict.State);
        Assert.Equal(.9, verdict.Confidence);
        Assert.Equal((7, 3), (verdict.OnFrames, verdict.OffFrames));
        Assert.Equal((.4, .9, .9), (verdict.ConfidenceMin!.Value, verdict.ConfidenceMedian!.Value, verdict.ConfidenceMax!.Value));
    }

    [Fact]
    public void Below_the_agreement_limit_there_is_no_verdict_and_the_reason_is_INCONSISTENT()
    {
        // 6 ON, 4 OFF: 60 % < 70 %.
        var verdict = PoleVideoVerdict.Decide([On(.9), On(.9), On(.9), Off(.8), Off(.8), Off(.8), Off(.8), On(.9), On(.9), On(.9)], Defaults);
        Assert.Equal("inconclusive", verdict.State);
        Assert.Null(verdict.Confidence);
        Assert.Equal("INCONSISTENT", verdict.ReasonCode);
        Assert.Equal((6, 4), (verdict.OnFrames, verdict.OffFrames));
        Assert.NotNull(verdict.ConfidenceMin);                       // the spread is still recorded for the reviewer
    }

    [Fact]
    public void A_clip_whose_first_and_last_voting_frames_differ_has_no_verdict_even_with_a_clear_majority()
    {
        // A lamp that has only just come on: OFF, then ON ×9 — 90 % agree but the clip starts differently from how it ends.
        var votes = new[] { Off(.9) }.Concat(Enumerable.Repeat(On(.9), 9)).ToArray();
        Assert.Equal("INCONSISTENT", PoleVideoVerdict.Decide(votes, Defaults).ReasonCode);
        // Frames that gave no verdict at the ends do not count as the first or the last voter.
        var padded = new[] { None("no_lamp") }.Concat(Enumerable.Repeat(On(.9), 8)).Append(None("no_lamp")).ToArray();
        Assert.Equal("on", PoleVideoVerdict.Decide(padded, Defaults).State);
    }

    [Fact]
    public void Fewer_deciding_frames_than_the_fraction_gives_no_verdict_with_the_commonest_reason()
    {
        // 5 frames, 2 decided (40 % < 60 %); three of them too bright.
        var verdict = PoleVideoVerdict.Decide([On(.9), None("too_bright"), None("too_bright"), None("no_lamp"), Off(.9)], Defaults);
        Assert.Equal(("inconclusive", "TOO_BRIGHT"), (verdict.State, verdict.ReasonCode));
        Assert.Equal(2, verdict.DaylightFrames);
        Assert.Equal(2, verdict.RepresentativeIndex);                // the middle frame stands in for a clip with no verdict
        Assert.Equal("MULTIPLE_LAMPS", PoleVideoVerdict.Decide([None("multiple_lamps"), None("multiple_lamps"), None("no_lamp")], Defaults).ReasonCode);
        Assert.Equal("NO_LAMP", PoleVideoVerdict.Decide([None("no_lamp"), None(null), On(.9)], Defaults).ReasonCode);
    }

    [Fact]
    public void Exactly_the_fraction_is_enough_and_one_frame_fewer_is_not()
    {
        var three = new[] { On(.9), On(.9), On(.9), None("no_lamp"), None("no_lamp") };       // 3 / 5 = 60 %
        Assert.Equal("on", PoleVideoVerdict.Decide(three, Defaults).State);
        var two = new[] { On(.9), On(.9), None("no_lamp"), None("no_lamp"), None("no_lamp") };
        Assert.Equal("inconclusive", PoleVideoVerdict.Decide(two, Defaults).State);
        // 7 of 12 is 58 %: refused; 8 of 12 passes.
        Assert.Equal("inconclusive", PoleVideoVerdict.Decide(Enumerable.Repeat(On(.9), 7).Concat(Enumerable.Repeat(None("no_lamp"), 5)).ToArray(), Defaults).State);
        Assert.Equal("on", PoleVideoVerdict.Decide(Enumerable.Repeat(On(.9), 8).Concat(Enumerable.Repeat(None("no_lamp"), 4)).ToArray(), Defaults).State);
    }

    [Fact]
    public void A_clip_where_no_frame_voted_has_no_spread()
    {
        var verdict = PoleVideoVerdict.Decide([None("no_lamp"), None("no_lamp"), None("no_lamp")], Defaults);
        Assert.Equal((null, null, null), (verdict.ConfidenceMin, verdict.ConfidenceMedian, verdict.ConfidenceMax));
        Assert.Equal((0, 0), (verdict.OnFrames, verdict.OffFrames));
    }

    [Fact]
    public void When_the_model_was_unavailable_for_any_frame_there_is_no_state_at_all_but_the_brightness_is_kept()
    {
        var verdict = PoleVideoVerdict.Decide([new(null, null, null, .2), new(null, null, null, .4), new(null, null, null, .6)], Defaults);
        Assert.Null(verdict.State);
        Assert.Equal(.4, verdict.MedianLuma);
        Assert.Equal((0, 0, 0), (verdict.OnFrames, verdict.OffFrames, verdict.DaylightFrames));
        Assert.Null(verdict.ConfidenceMin);
    }

    [Fact]
    public void The_median_brightness_is_over_every_frame()
        => Assert.Equal(.3, PoleVideoVerdict.Decide([On(.9, .1), On(.9, .3), On(.9, .9)], Defaults).MedianLuma);

    [Fact]
    public void A_clip_needs_at_least_one_frame()
        => Assert.Throws<ArgumentException>(() => PoleVideoVerdict.Decide([], Defaults));

    [Fact]
    public void Every_setting_changes_the_rules_version_and_the_same_settings_give_the_same_one()
    {
        var baseline = PoleVideoVerdict.RulesVersion(Defaults);
        Assert.Equal(baseline, PoleVideoVerdict.RulesVersion(new()));
        Assert.Matches("^pv-[0-9a-f]{12}$", baseline);
        var variants = new Action<PoleVideoOptions>[]
        {
            o => o.FramesPerSecond = 2, o => o.MaximumFrames = 8, o => o.SkipLeadingSeconds = 2, o => o.SkipLeadingOnlyAboveSeconds = 8,
            o => o.MinimumSeconds = 4, o => o.MaximumSeconds = 20, o => o.DurationGraceSeconds = 1, o => o.MinimumDecidedFraction = .7, o => o.MinimumAgreement = .8,
        };
        var seen = new HashSet<string> { baseline };
        foreach (var change in variants)
        {
            var options = new PoleVideoOptions(); change(options);
            Assert.True(seen.Add(PoleVideoVerdict.RulesVersion(options)), "a setting did not change the rules version");
        }
        // Settings that do not affect the verdict (slots, timeouts, temp directory) leave it alone.
        Assert.Equal(baseline, PoleVideoVerdict.RulesVersion(new PoleVideoOptions { MaximumConcurrentClips = 3, TimeoutSeconds = 5, TempRoot = "/tmp/x" }));
    }
}
