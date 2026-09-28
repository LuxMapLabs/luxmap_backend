namespace LuxMap.Shared.Serialization;

/// <summary>UTC timestamps at PostgreSQL TIMESTAMPTZ precision, before persistence and response serialization.</summary>
public static class UtcMicrosecondClock
{
    public static DateTime UtcNow(TimeProvider? timeProvider = null)
    {
        var now = (timeProvider ?? TimeProvider.System).GetUtcNow().UtcDateTime;
        return new DateTime(now.Ticks - now.Ticks % TimeSpan.TicksPerMicrosecond, DateTimeKind.Utc);
    }
}
