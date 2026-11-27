namespace LuxMap.Shared.Contracts.Enums;

/// <summary>Read-only feeder health in asset inventory; separate from the observed ON/OFF lamp state.</summary>
public enum FeederHealthState
{
    Unknown,
    Normal,
    Warning,
    Incident,
    Offline,
    Repairing,
}
