static class AutoControl
{
    public static string? Resolve(string mode, string? type, AutoSchedule? schedule)
    {
        var resolved = mode == "AUTO" ? type?.Trim().ToLowerInvariant() ?? "scheduled" : null;
        if ((mode == "MANUAL" && type is not null) ||
            (mode == "AUTO" && resolved is not ("scheduled" or "full_time")))
            throw new ArgumentException("autoType: full_time or scheduled, only for AUTO.");
        if (schedule is not null && (mode != "AUTO" || resolved != "scheduled" || !schedule.IsValid()))
            throw new ArgumentException("autoSchedule is only allowed for scheduled AUTO, with distinct valid minutes.");
        return resolved;
    }
}
