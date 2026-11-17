using LuxMap.Modules.Survey.Entities;
using LuxMap.Modules.Survey.Review;
using LuxMap.Modules.WorkOrders;
using LuxMap.Modules.WorkOrders.Entities;
using LuxMap.Shared.Contracts.Enums;

namespace LuxMap.Shared.Tests;

public sealed class SurveyFindingTests
{
    private static LuminanceHistory Sweep(FixtureStatus status, int at = 1, int published = 1, string id = "SWP-0001",
        DataSource source = DataSource.Simulated) => new()
    {
        SweepId = id, PoleId = "pole", CommuneId = "commune", PublishedBy = "manager", ReasonCodes = "[]",
        ClassifiedAs = status, EvaluatedAt = DateTime.UnixEpoch.AddDays(at),
        PublishedAt = DateTime.UnixEpoch.AddDays(published), DataSource = source
    };

    private static WorkOrderPolePhoto Photo(string state, int at = 2, int decided = 2, string id = "WPP-0001",
        DataSource source = DataSource.Field) => new()
    {
        PhotoId = id, PoleId = "pole", CommuneId = "commune", WorkOrderId = "order", UploadedBy = "engineer",
        RequestHash = "unused", ObjectKey = "unused", ThumbnailKey = "unused", CvState = state,
        Decision = "confirm", CapturedAt = DateTime.UnixEpoch.AddDays(at),
        DecidedAt = DateTime.UnixEpoch.AddDays(decided), DataSource = source
    };

    [Theory]
    [InlineData(FixtureStatus.Out, "on", null, null)]
    [InlineData(FixtureStatus.Unknown, "on", null, null)]
    [InlineData(FixtureStatus.Normal, "on", null, null)]
    // The photo changed nothing, so the source stays the sweep's: that sweep raised the open fault to carry.
    [InlineData(FixtureStatus.Dim, "on", FaultType.LampDim, DataSource.Simulated)]
    [InlineData(FixtureStatus.Out, "off", FaultType.LampOut, DataSource.Simulated)]
    [InlineData(FixtureStatus.Normal, "off", FaultType.LampOut, DataSource.Field)]
    public void Later_photo_folds_the_previous_sweep_classification(FixtureStatus status, string state, FaultType? expected,
        DataSource? source)
    {
        var findings = SurveyFindingLookup.Merge([Sweep(status)], [Photo(state)]);
        if (expected is null) Assert.Empty(findings);
        else Assert.Equal(new SurveyFinding("pole", expected.Value, source!.Value), Assert.Single(findings));
    }

    [Fact]
    public void Photo_only_and_engineer_label_are_used()
    {
        var photo = Photo("on"); photo.FinalCvState = "off"; photo.StatusApplied = false;
        Assert.Equal(new SurveyFinding("pole", FaultType.LampOut, DataSource.Field),
            Assert.Single(SurveyFindingLookup.Merge([], [photo])));
        Assert.Empty(SurveyFindingLookup.Merge([], [Photo("on")]));
    }

    [Fact]
    public void Capture_order_wins_over_input_and_publication_order_and_all_steps_are_folded()
    {
        var sweep = Sweep(FixtureStatus.Dim, published: 100);
        var off = Photo("off", at: 2, decided: 50);
        var on = Photo("on", at: 3, decided: 4);
        // Taking only the latest photo and applying it to dim would incorrectly carry lamp_dim.
        Assert.Empty(SurveyFindingLookup.Merge([sweep], [on, off]));
        Assert.Empty(SurveyFindingLookup.Merge([Sweep(FixtureStatus.Normal, at: 3)], [off]));
        Assert.Empty(SurveyFindingLookup.Merge([Sweep(FixtureStatus.Unknown, at: 3), Sweep(FixtureStatus.Out)], []));
    }

    [Fact]
    public void Equal_capture_time_sweep_wins_even_when_photo_is_decided_later()
    {
        Assert.Empty(SurveyFindingLookup.Merge([Sweep(FixtureStatus.Normal)], [Photo("off", at: 1, decided: 100)]));
        Assert.Equal(FaultType.LampDim, Assert.Single(SurveyFindingLookup.Merge(
            [Sweep(FixtureStatus.Dim)], [Photo("off", at: 1, decided: 100)])).FaultType);
        Assert.Equal(DataSource.Simulated, Assert.Single(SurveyFindingLookup.Merge(
            [Sweep(FixtureStatus.Out)], [Photo("on", at: 1, decided: 100)])).DataSource);
    }

    [Fact]
    public void Equal_photos_use_decision_time_then_id_length_then_ordinal_in_both_input_orders()
    {
        var on = Photo("on", decided: 1, id: "WPP-9999");
        var off = Photo("off", decided: 2, id: "WPP-0001");
        void Out()
        {
            Assert.Equal(FaultType.LampOut, Assert.Single(SurveyFindingLookup.Merge([], [off, on])).FaultType);
            Assert.Equal(FaultType.LampOut, Assert.Single(SurveyFindingLookup.Merge([], [on, off])).FaultType);
        }
        Out();
        off.DecidedAt = on.DecidedAt; off.PhotoId = "WPP-10000"; Out();
        on.PhotoId = "WPP-10000"; off.PhotoId = "WPP-10001"; Out();
    }

    [Fact]
    public void Equal_sweeps_keep_publication_time_then_id_length_then_ordinal_tiebreak()
    {
        var normal = Sweep(FixtureStatus.Normal, published: 1, id: "SWP-9999");
        var dim = Sweep(FixtureStatus.Dim, published: 2, id: "SWP-0001");
        void Dim()
        {
            Assert.Equal(FaultType.LampDim, Assert.Single(SurveyFindingLookup.Merge([dim, normal], [])).FaultType);
            Assert.Equal(FaultType.LampDim, Assert.Single(SurveyFindingLookup.Merge([normal, dim], [])).FaultType);
        }
        Dim();
        dim.PublishedAt = normal.PublishedAt; dim.SweepId = "SWP-10000"; Dim();
        normal.SweepId = "SWP-10000"; dim.SweepId = "SWP-10001"; Dim();
    }

    [Fact]
    public void Each_pole_has_its_own_state_and_final_observation_source()
    {
        var dim = Sweep(FixtureStatus.Dim); var off = Photo("off"); off.PoleId = "other";
        var findings = SurveyFindingLookup.Merge([dim], [off, Photo("on")]);
        Assert.Equal(2, findings.Length);
        Assert.Contains(new SurveyFinding("pole", FaultType.LampDim, DataSource.Simulated), findings);
        Assert.Contains(new SurveyFinding("other", FaultType.LampOut, DataSource.Field), findings);
    }
}
