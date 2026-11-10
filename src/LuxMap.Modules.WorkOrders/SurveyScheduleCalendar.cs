namespace LuxMap.Modules.WorkOrders;

/// <summary>SURVEY-SCHED-1: local calendar dates, Monday–Sunday, Thursday 17:00 cutoff.</summary>
public static class SurveyScheduleCalendar
{
    private static DateTime Cutoff(DateTime local)
        => EngineerLoadBoard.WeekStart(DateOnly.FromDateTime(local)).AddDays(3).ToDateTime(new TimeOnly(17, 0));

    public static DateOnly StartsOnWeek(DateTime local)
        => EngineerLoadBoard.WeekStart(DateOnly.FromDateTime(local)).AddDays(local < Cutoff(local) ? 7 : 14);

    public static DateOnly HorizonEnd(DateTime local)
        => EngineerLoadBoard.WeekStart(DateOnly.FromDateTime(local)).AddDays(local < Cutoff(local) ? 6 : 13);

    public static DateOnly FirstOccurrence(DateOnly startsOnWeek, int dayOfWeek)
    {
        if (dayOfWeek is < 1 or > 7) throw new ArgumentOutOfRangeException(nameof(dayOfWeek));
        return startsOnWeek.AddDays(dayOfWeek - 1);
    }

    public static IEnumerable<DateOnly> Dates(DateTime local, DateOnly startsOnWeek, int dayOfWeek)
    {
        var end = HorizonEnd(local);
        for (var date = DateOnly.FromDateTime(local); date <= end; date = date.AddDays(1))
            if (EngineerLoadBoard.WeekStart(date) >= startsOnWeek && ((int)date.DayOfWeek + 6) % 7 + 1 == dayOfWeek)
                yield return date;
    }
}
