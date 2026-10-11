using LuxMap.Shared.Contracts.Enums;
using LuxMap.Shared.Contracts.Warranty;

namespace LuxMap.Shared.Tests;

/// <summary>BE-31 — the one definition of <c>warranty_status</c> (drift W-2). The edges are the decision.</summary>
public sealed class WarrantyWindowTests
{
    private static readonly DateOnly Today = new(2026, 10, 12);

    [Theory]
    [InlineData(-400, WarrantyStatus.Expired)]
    [InlineData(-1, WarrantyStatus.Expired)]
    [InlineData(0, WarrantyStatus.Expiring)]
    [InlineData(90, WarrantyStatus.Expiring)]
    [InlineData(91, WarrantyStatus.Active)]
    [InlineData(4000, WarrantyStatus.Active)]
    public void A_warranty_ending_today_is_still_covered_and_the_window_includes_both_ends(int offset, WarrantyStatus expected)
    {
        var window = new WarrantyWindow(Today, 90);

        Assert.Equal(expected, window.Classify(Today.AddDays(offset)));
        Assert.Equal(offset, window.DaysRemaining(Today.AddDays(offset)));
    }

    [Fact]
    public void No_expiry_on_record_is_unknown_never_active()
    {
        var window = new WarrantyWindow(Today, 90);

        Assert.Equal(WarrantyStatus.Unknown, window.Classify(null));
        Assert.Null(window.DaysRemaining(null));
    }

    [Fact]
    public void A_zero_day_window_leaves_only_today_expiring()
    {
        var window = new WarrantyWindow(Today, 0);

        Assert.Equal(Today, window.LastExpiringDay);
        Assert.Equal(WarrantyStatus.Expiring, window.Classify(Today));
        Assert.Equal(WarrantyStatus.Active, window.Classify(Today.AddDays(1)));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(WarrantyWindow.MaxExpiringWithinDays + 1)]
    public void A_window_outside_its_range_is_refused(int days)
        => Assert.Throws<ArgumentOutOfRangeException>(() => new WarrantyWindow(Today, days));
}
