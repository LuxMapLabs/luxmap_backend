using LuxMap.Shared.Contracts.Enums;

namespace LuxMap.Shared.Tests;

/// <summary>ASSET-PHOTOS D-09: until telemetry measures power, the answer is "unknown" and nothing can default to "on".</summary>
public class FeederPowerTests
{
    [Fact]
    public void A_response_built_without_setting_the_state_says_unknown_never_on()
        => Assert.Equal(FeederPowerState.Unknown, default(FeederPowerState));

    [Fact]
    public void Power_is_unknown_whatever_the_device_reported()
        => Assert.Equal(FeederPowerState.Unknown, FeederPower.State());
    [Theory]
    [InlineData(FeederHealthState.Unknown, "unknown")]
    [InlineData(FeederHealthState.Normal, "normal")]
    [InlineData(FeederHealthState.Warning, "warning")]
    [InlineData(FeederHealthState.Incident, "incident")]
    [InlineData(FeederHealthState.Offline, "offline")]
    public void Health_state_serializes_as_a_separate_string_enum(FeederHealthState state, string expected)
    {
        var options = new System.Text.Json.JsonSerializerOptions();
        options.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter(System.Text.Json.JsonNamingPolicy.SnakeCaseLower, false));
        Assert.Equal("\"" + expected + "\"", System.Text.Json.JsonSerializer.Serialize(state, options));
        Assert.Equal(FeederHealthState.Unknown, default(FeederHealthState));
    }
}
