using System.Globalization;
using LuxMap.Modules.WorkOrders;

namespace LuxMap.Shared.Tests;

public sealed class SurveyScheduleCalendarTests
{
    private static DateTime Local(string text) => DateTime.Parse(text, CultureInfo.InvariantCulture);

    [Theory]
    [InlineData("2025-11-04T10:00:00", 3, "2025-11-10", "2025-11-12")]
    [InlineData("2025-11-07T10:00:00", 3, "2025-11-17", "2025-11-19")]
    [InlineData("2025-11-10T10:00:00", 5, "2025-11-17", "2025-11-21")]
    [InlineData("2025-11-06T16:59:59", 3, "2025-11-10", "2025-11-12")]
    [InlineData("2025-11-06T17:00:00", 3, "2025-11-17", "2025-11-19")]
    [InlineData("2025-11-09T23:59:59", 7, "2025-11-17", "2025-11-23")]
    [InlineData("2025-11-10T00:00:00", 1, "2025-11-17", "2025-11-17")]
    [InlineData("2025-12-31T10:00:00", 7, "2026-01-05", "2026-01-11")]
    [InlineData("2026-01-01T17:00:00", 1, "2026-01-12", "2026-01-12")]
    public void Setup_uses_the_local_cutoff_and_first_week(string instant, int day, string week, string first)
    {
        var start = SurveyScheduleCalendar.StartsOnWeek(Local(instant));
        Assert.Equal(DateOnly.Parse(week, CultureInfo.InvariantCulture), start);
        Assert.Equal(DateOnly.Parse(first, CultureInfo.InvariantCulture), SurveyScheduleCalendar.FirstOccurrence(start, day));
    }

    [Theory]
    [InlineData(1, "2025-11-10")]
    [InlineData(2, "2025-11-11")]
    [InlineData(3, "2025-11-12")]
    [InlineData(4, "2025-11-13")]
    [InlineData(5, "2025-11-14")]
    [InlineData(6, "2025-11-15")]
    [InlineData(7, "2025-11-16")]
    public void All_seven_days_are_included(int day, string expected)
        => Assert.Equal(DateOnly.Parse(expected, CultureInfo.InvariantCulture),
            SurveyScheduleCalendar.FirstOccurrence(new(2025, 11, 10), day));

    [Theory]
    [InlineData("2025-11-06T16:59:59", "2025-11-09")]
    [InlineData("2025-11-06T17:00:00", "2025-11-16")]
    [InlineData("2025-11-09T23:59:59", "2025-11-16")]
    [InlineData("2025-11-10T00:00:00", "2025-11-16")]
    public void Generation_horizon_uses_the_same_cutoff(string instant, string expected)
        => Assert.Equal(DateOnly.Parse(expected, CultureInfo.InvariantCulture), SurveyScheduleCalendar.HorizonEnd(Local(instant)));

    [Fact]
    public void Generation_never_backfills_past_dates_or_future_start_weeks()
    {
        Assert.Empty(SurveyScheduleCalendar.Dates(Local("2025-11-06T16:59:59"), new(2025, 10, 27), 3));
        Assert.Equal([new DateOnly(2025, 11, 12)], SurveyScheduleCalendar.Dates(Local("2025-11-06T17:00:00"), new(2025, 10, 27), 3));
        Assert.Empty(SurveyScheduleCalendar.Dates(Local("2025-11-06T17:00:00"), new(2025, 11, 17), 3));
        Assert.Equal([new DateOnly(2025, 11, 9), new DateOnly(2025, 11, 16)],
            SurveyScheduleCalendar.Dates(Local("2025-11-06T17:00:00"), new(2025, 10, 27), 7));
    }

    [Fact]
    public void Fixed_day_selection_ignores_quota_but_reserves_week_and_day_load()
    {
        var date = new DateOnly(2025, 11, 12);
        DispatchEngineer[] people = [new("USR-10000", "higher", "COM-001"), new("USR-9999", "lower", "COM-001")];
        var board = new EngineerLoadBoard([new("USR-9999", date), new("USR-10000", date)], 1);
        Assert.Equal("USR-9999", board.SelectOn(people, date)?.UserId);
        Assert.Equal("USR-10000", board.SelectOn(people, date)?.UserId);
        Assert.Null(board.SelectOn([], date));
    }
}
