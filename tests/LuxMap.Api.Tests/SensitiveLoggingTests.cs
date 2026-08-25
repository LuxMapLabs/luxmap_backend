using LuxMap.Api.Observability;
using Serilog.Events;
using Serilog.Parsing;

namespace LuxMap.Api.Tests;

/// <summary>
/// Loại trừ dữ liệu nhạy cảm phải là cấu hình tường minh, không phải "nhớ đừng log".
/// </summary>
public class SensitiveLoggingTests
{
    private static LogEvent EventWith(params (string Name, string Value)[] properties)
    {
        var logEvent = new LogEvent(
            DateTimeOffset.UtcNow,
            LogEventLevel.Information,
            exception: null,
            new MessageTemplate("test", []),
            []);

        foreach (var (name, value) in properties)
        {
            logEvent.AddOrUpdateProperty(new LogEventProperty(name, new ScalarValue(value)));
        }

        return logEvent;
    }

    [Theory]
    [InlineData("Authorization")]
    [InlineData("authorization")]
    [InlineData("RequestHeaders.Authorization")]
    [InlineData("AccessToken")]
    [InlineData("refresh_token")]
    [InlineData("Password")]
    [InlineData("DbPassword")]
    [InlineData("ClientSecret")]
    [InlineData("ConnectionString")]
    [InlineData("ApiKey")]
    [InlineData("Cookie")]
    public void Sensitive_properties_are_masked(string propertyName)
    {
        var logEvent = EventWith((propertyName, "Bearer SECRET_TOKEN_12345"));

        new SensitivePropertyScrubber().Enrich(logEvent, propertyFactory: null!);

        Assert.Equal("\"***\"", logEvent.Properties[propertyName].ToString());
        Assert.DoesNotContain("SECRET_TOKEN_12345", logEvent.Properties[propertyName].ToString());
    }

    [Theory]
    [InlineData("CorrelationId")]
    [InlineData("RequestPath")]
    [InlineData("StatusCode")]
    [InlineData("PoleId")]
    public void Ordinary_properties_are_left_alone(string propertyName)
    {
        var logEvent = EventWith((propertyName, "giữ nguyên"));

        new SensitivePropertyScrubber().Enrich(logEvent, propertyFactory: null!);

        Assert.Contains("giữ nguyên", logEvent.Properties[propertyName].ToString());
    }

    [Fact]
    public void Masking_does_not_disturb_the_other_properties_on_the_same_event()
    {
        var logEvent = EventWith(
            ("Authorization", "Bearer SECRET_TOKEN_12345"),
            ("CorrelationId", "fm-android-42"),
            ("StatusCode", "404"));

        new SensitivePropertyScrubber().Enrich(logEvent, propertyFactory: null!);

        Assert.Equal("\"***\"", logEvent.Properties["Authorization"].ToString());
        Assert.Contains("fm-android-42", logEvent.Properties["CorrelationId"].ToString());
        Assert.Contains("404", logEvent.Properties["StatusCode"].ToString());
    }
}
