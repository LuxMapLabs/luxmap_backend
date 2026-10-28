namespace LuxMap.Shared.Contracts.Enums;

/// <summary>The mode last reported by the device; not a measurement of electrical power.</summary>
public enum FeederControlMode
{
    On,
    Off,
    Auto,
}

/// <summary>Electrical power observation: on | off | unknown. Computed on read, never persisted.</summary>
/// <remarks>
/// <c>Unknown</c> is declared first so it is the CLR default: a response built without setting the state (the
/// <c>FeederRow</c> projection does exactly that) says "no data", never "on". The wire form is the lower-case name.
/// </remarks>
public enum FeederPowerState
{
    Unknown,
    On,
    Off,
}

/// <summary>One classification rule shared by inventory and map until measured telemetry is available.</summary>
public static class FeederPower
{
    public static FeederPowerState State() => FeederPowerState.Unknown;
}
