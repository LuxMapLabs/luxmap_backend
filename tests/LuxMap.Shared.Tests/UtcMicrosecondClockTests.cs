using LuxMap.Shared.Serialization;

namespace LuxMap.Shared.Tests;

public class UtcMicrosecondClockTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(9)]
    public void UtcNow_truncates_submicrosecond_ticks_and_preserves_utc(int extraTicks)
    {
        var expected = new DateTime(2026, 9, 28, 3, 40, 46, DateTimeKind.Utc).AddTicks(8_506_200);
        var clock = new FixedTimeProvider(expected.AddTicks(extraTicks));

        var actual = UtcMicrosecondClock.UtcNow(clock);

        Assert.Equal(expected, actual);
        Assert.Equal(DateTimeKind.Utc, actual.Kind);
        Assert.Equal(0, actual.Ticks % TimeSpan.TicksPerMicrosecond);
    }

    private sealed class FixedTimeProvider(DateTime now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(now);
    }
}
