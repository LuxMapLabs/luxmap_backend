using System.Globalization;
using LuxMap.Modules.WorkOrders;

namespace LuxMap.Api.Tests;

/// <summary>"Tonight" for the agenda (BE-25 D-2): a night shift crosses midnight and belongs to the evening's date.</summary>
public class WorkOrderAgendaOptionsTests
{
    [Theory]
    [InlineData("2026-10-06T11:00:00Z", "2026-10-06")] // 18:00 in Vietnam: tonight
    [InlineData("2026-10-06T17:59:00Z", "2026-10-06")] // 00:59 the next day: still last night's shift
    [InlineData("2026-10-07T04:59:59Z", "2026-10-06")] // 11:59:59: still last night
    [InlineData("2026-10-07T05:00:00Z", "2026-10-07")] // 12:00 local: the new night
    public void The_night_crosses_midnight_and_turns_over_at_noon_local_time(string instant, string night)
        => Assert.Equal(DateOnly.ParseExact(night, "yyyy-MM-dd", CultureInfo.InvariantCulture),
            new WorkOrderAgendaOptions().NightOf(DateTimeOffset.Parse(instant, CultureInfo.InvariantCulture)));

    [Fact]
    public void An_unknown_time_zone_stops_startup()
        => Assert.Throws<InvalidOperationException>(() => new WorkOrderAgendaOptions { TimeZone = "Mars/Olympus_Mons" }.Validate());
}
