using System.Globalization;
using System.Text.Json;
using LuxMap.Shared.Serialization;

namespace LuxMap.Shared.Tests;

/// <summary>
/// Contract v1.1 sections 0 and 5.2 — API timestamps are ISO 8601 UTC with a <c>Z</c> suffix,
/// and the kind must be <see cref="DateTimeKind.Utc"/> because Npgsql throws on a TIMESTAMPTZ with the wrong kind.
/// </summary>
public class UtcDateTimeConventionTests
{
    private static readonly JsonSerializerOptions Options = LuxMapJsonOptions.Default;

    private static string Wire(DateTime value) => JsonSerializer.Serialize(value, Options).Trim('"');

    [Fact]
    public void Utc_datetime_writes_iso8601_with_z_suffix()
    {
        var value = new DateTime(2026, 8, 20, 4, 0, 0, DateTimeKind.Utc);
        Assert.Equal("2026-08-20T04:00:00Z", Wire(value));
    }

    [Fact]
    public void Local_datetime_is_converted_to_utc_at_the_boundary()
    {
        var utc = new DateTime(2026, 8, 20, 4, 0, 0, DateTimeKind.Utc);
        var local = utc.ToLocalTime();

        Assert.Equal("2026-08-20T04:00:00Z", Wire(local));
    }

    [Fact]
    public void Unspecified_kind_is_treated_as_utc_not_as_local()
    {
        var unspecified = new DateTime(2026, 8, 20, 4, 0, 0, DateTimeKind.Unspecified);
        Assert.Equal("2026-08-20T04:00:00Z", Wire(unspecified));
    }

    [Fact]
    public void Sub_second_precision_is_not_lost()
    {
        var value = new DateTime(2026, 8, 20, 4, 0, 0, DateTimeKind.Utc).AddTicks(1234567);
        Assert.Equal("2026-08-20T04:00:00.1234567Z", Wire(value));
    }

    [Theory]
    [InlineData("\"2026-08-20T04:00:00Z\"")]
    [InlineData("\"2026-08-20T11:00:00+07:00\"")]
    [InlineData("\"2026-08-20T04:00:00\"")]
    public void Reading_always_yields_utc_kind(string json)
    {
        var value = JsonSerializer.Deserialize<DateTime>(json, Options);

        Assert.Equal(DateTimeKind.Utc, value.Kind);
        Assert.Equal(new DateTime(2026, 8, 20, 4, 0, 0, DateTimeKind.Utc), value);
    }

    [Fact]
    public void Nullable_datetime_is_covered_by_the_same_converter()
    {
        DateTime? value = new DateTime(2026, 8, 20, 4, 0, 0, DateTimeKind.Utc);
        Assert.Equal("\"2026-08-20T04:00:00Z\"", JsonSerializer.Serialize(value, Options));
        Assert.Equal("null", JsonSerializer.Serialize((DateTime?)null, Options));
    }

    [Fact]
    public void DateTimeOffset_is_normalised_to_utc()
    {
        var value = new DateTimeOffset(2026, 8, 20, 11, 0, 0, TimeSpan.FromHours(7));
        Assert.Equal("\"2026-08-20T04:00:00Z\"", JsonSerializer.Serialize(value, Options));
    }

    [Fact]
    public void Output_matches_the_timestamp_format_used_by_the_fo26_mocks()
    {
        // The FO-26 mock set uses whole seconds: "2026-08-20T04:00:00Z".
        var parsed = DateTime.Parse(
            "2026-08-20T04:00:00Z",
            CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);

        Assert.Equal("2026-08-20T04:00:00Z", Wire(parsed));
    }
}
