using LuxMap.Shared.Contracts.Enums;

namespace LuxMap.Shared.Tests;

/// <summary>
/// Pins the one definition of "an open fault".
/// </summary>
/// <remarks>
/// <c>open_fault_count</c> appears in Contract section 2.1, and the same idea drives BE-28's
/// statistics and BE-40's listing — but section 1 never says which of the six statuses count. Three
/// tickets left to decide separately would each pick a plausible set and the numbers would disagree
/// with nothing to explain why. This test exists so a later change to the set is a deliberate edit
/// rather than a drift nobody notices.
/// </remarks>
public class FaultStatusSetsTests
{
    [Theory]
    [InlineData(FaultStatus.Detected, true)]
    [InlineData(FaultStatus.Confirmed, true)]
    [InlineData(FaultStatus.InProgress, true)]
    [InlineData(FaultStatus.Rejected, false)]
    [InlineData(FaultStatus.Resolved, false)]
    [InlineData(FaultStatus.Verified, false)]
    public void Each_status_is_open_or_closed_exactly_as_agreed(FaultStatus status, bool expected)
        => Assert.Equal(expected, FaultStatusSets.IsOpen(status));

    [Fact]
    public void Every_status_is_classified_so_a_new_enum_value_cannot_slip_through_unconsidered()
    {
        // If Contract section 1 ever gains a seventh status, this fails and forces a decision about
        // which side it falls on, rather than defaulting it to "closed" by omission.
        var classified = Enum.GetValues<FaultStatus>()
            .Count(status => FaultStatusSets.Open.Contains(status) || !FaultStatusSets.Open.Contains(status));

        Assert.Equal(6, Enum.GetValues<FaultStatus>().Length);
        Assert.Equal(6, classified);
        Assert.Equal(3, FaultStatusSets.Open.Count);
    }
}
