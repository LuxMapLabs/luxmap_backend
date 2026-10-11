using LuxMap.Shared.Contracts.Warranty;

namespace LuxMap.Modules.Assets.Warranty;

/// <summary>
/// BE-31 settings. <c>Assets:Warranty:ExpiringWithinDays</c> in appsettings (default 90, drift W-3); a restart applies it.
/// </summary>
public sealed record WarrantyOptions
{
    public const string SectionName = "Assets:Warranty";

    /// <summary>
    /// Read from <c>WorkOrders:Agenda:TimeZone</c>, NOT from this section: the communes' zone is configured once, and a
    /// second key could make "today" here differ from "tonight" on the agenda.
    /// </summary>
    public const string TimeZoneKey = "WorkOrders:Agenda:TimeZone";

    public int ExpiringWithinDays { get; init; } = 90;

    /// <summary>IANA zone whose calendar date is "today". Every pilot commune is in Vietnam.</summary>
    public string TimeZone { get; init; } = "Asia/Ho_Chi_Minh";

    /// <summary>An unknown zone or an out-of-range window would mislabel every lamp, so startup STOPS.</summary>
    public void Validate()
    {
        _ = new WarrantyWindow(DateOnly.MinValue, ExpiringWithinDays);
        Zone();
    }

    public TimeZoneInfo Zone()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(TimeZone); }
        catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            throw new InvalidOperationException($"{TimeZoneKey} '{TimeZone}' is not a known time zone.", e);
        }
    }
}

/// <summary>Today's <see cref="WarrantyWindow"/>, from the configured zone and the injected clock.</summary>
public sealed class WarrantyCalendar(WarrantyOptions options, TimeProvider clock)
{
    public int DefaultExpiringWithinDays => options.ExpiringWithinDays;

    /// <param name="expiringWithinDays">Overrides the configured window for one request (<c>within_days</c>).</param>
    public WarrantyWindow Window(int? expiringWithinDays = null)
        => new(DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), options.Zone()).DateTime),
            expiringWithinDays ?? options.ExpiringWithinDays);
}
