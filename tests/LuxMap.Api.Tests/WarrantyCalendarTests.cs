using LuxMap.Modules.Assets.Warranty;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Time.Testing;

namespace LuxMap.Api.Tests;

/// <summary>BE-31 — "today" is the calendar date in the communes' zone, configured once (drift W-2, W-3). No host, no database.</summary>
public sealed class WarrantyCalendarTests
{
    private static IConfiguration Config(params (string Key, string Value)[] values)
        => new ConfigurationBuilder().AddInMemoryCollection(values.ToDictionary(v => v.Key, v => (string?)v.Value)).Build();

    [Theory]
    [InlineData("2026-10-11T18:00:00Z", "2026-10-12")] // 01:00 in Vietnam: already the 12th, still the 11th in UTC
    [InlineData("2026-10-12T04:00:00Z", "2026-10-12")] // 11:00 in Vietnam: before the 12:00 night boundary, still the 12th
    [InlineData("2026-10-12T16:59:59Z", "2026-10-12")]
    [InlineData("2026-10-12T17:00:00Z", "2026-10-13")]
    public void Today_is_the_vietnamese_calendar_date_not_utc_and_not_the_survey_night(string instant, string today)
    {
        var calendar = new WarrantyCalendar(WarrantyOptions.From(Config()), new FakeTimeProvider(DateTimeOffset.Parse(instant)));

        Assert.Equal(DateOnly.Parse(today), calendar.Window().Today);
    }

    [Fact]
    public void A_zone_under_the_warranty_section_is_ignored_the_agenda_key_is_the_only_one()
    {
        var alone = WarrantyOptions.From(Config(("Assets:Warranty:TimeZone", "UTC")));
        Assert.Equal("Asia/Ho_Chi_Minh", alone.TimeZone);

        var agenda = WarrantyOptions.From(Config(("Assets:Warranty:TimeZone", "UTC"), ("WorkOrders:Agenda:TimeZone", "Asia/Bangkok")));
        Assert.Equal("Asia/Bangkok", agenda.TimeZone);
    }

    [Fact]
    public void The_window_comes_from_configuration_and_a_request_may_override_it()
    {
        var options = WarrantyOptions.From(Config(("Assets:Warranty:ExpiringWithinDays", "30")));
        var calendar = new WarrantyCalendar(options, new FakeTimeProvider(DateTimeOffset.Parse("2026-10-12T05:00:00Z")));

        Assert.Equal(30, calendar.Window().ExpiringWithinDays);
        Assert.Equal(7, calendar.Window(7).ExpiringWithinDays);
        Assert.Equal(90, WarrantyOptions.From(Config()).ExpiringWithinDays);
    }

    [Theory]
    [InlineData("WorkOrders:Agenda:TimeZone", "Mars/Olympus")]
    [InlineData("Assets:Warranty:ExpiringWithinDays", "-1")]
    public void A_setting_that_would_mislabel_every_lamp_stops_startup(string key, string value)
        => Assert.ThrowsAny<Exception>(() => WarrantyOptions.From(Config((key, value))).Validate());
}
