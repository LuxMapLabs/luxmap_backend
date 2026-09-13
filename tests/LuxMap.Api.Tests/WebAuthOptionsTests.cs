namespace LuxMap.Api.Tests;

/// <summary>T-OPTIONS for the <c>WebAuth</c> lifetimes (Contract section 2.10.4).</summary>
public class WebAuthOptionsTests
{
    [Theory]
    [InlineData("WebAuth:PersistentSlidingDays", "0")]
    [InlineData("WebAuth:PersistentSlidingDays", "-14")]
    [InlineData("WebAuth:SessionHours", "0")]
    [InlineData("WebAuth:SessionHours", "-12")]
    public void Startup_stops_on_a_non_positive_web_lifetime(string key, string value)
    {
        // A valid allowlist, so the failure can only come from the lifetime under test.
        var message = CorsTests.StartupFailure(new Dictionary<string, string>
        {
            ["Cors:AllowedOrigins:0"] = TestHostSettings.WebOrigin,
            [key] = value,
        });

        Assert.Contains(key, message, StringComparison.Ordinal);
    }
}
