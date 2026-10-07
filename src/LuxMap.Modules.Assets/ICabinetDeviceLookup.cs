namespace LuxMap.Modules.Assets;

/// <summary>
/// What the Telemetry module knows about devices, asked from the Assets side (CABINET).
/// </summary>
/// <remarks>
/// <para>
/// A port because Assets cannot reference Telemetry — the reference already runs the other way
/// (<c>feeder_control</c> points at <c>feeder</c>). Same shape as <c>IActiveWorkOrderLookup</c> between
/// Faults and WorkOrders. Telemetry implements it.
/// </para>
/// <para>
/// Both answers go through the commune query filter. That narrows nothing that matters: a device sits in its
/// cabinet's commune and a relay row in its feeder's (composite keys), so whoever can see the cabinet or the
/// feeder sees the device too.
/// </para>
/// </remarks>
public interface ICabinetDeviceLookup
{
    /// <summary>cabinet_id → node_id for those of <paramref name="cabinetIds"/> that carry a device.</summary>
    Task<IReadOnlyDictionary<string, string>> DevicesAsync(IReadOnlyCollection<string> cabinetIds, CancellationToken ct);

    /// <summary>
    /// feeder_id → node_id for those of <paramref name="feederIds"/> a device switches. Such a feeder cannot move
    /// to another cabinet or leave its cabinet (CAB-4).
    /// </summary>
    Task<IReadOnlyDictionary<string, string>> ControllersAsync(IReadOnlyCollection<string> feederIds, CancellationToken ct);
}
