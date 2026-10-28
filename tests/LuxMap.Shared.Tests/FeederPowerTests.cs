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
}
