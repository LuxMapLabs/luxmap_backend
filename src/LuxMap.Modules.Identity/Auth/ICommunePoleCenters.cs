using LuxMap.Shared.Contracts.Communes;

namespace LuxMap.Modules.Identity.Auth;

/// <summary>
/// Where each commune's poles are, asked from Identity so <c>GET /auth/me</c> can open the map on them (MAP-CENTER).
/// </summary>
/// <remarks>
/// A port because Identity cannot reference Assets — the reference already runs the other way. Same shape as
/// <c>ICabinetDeviceLookup</c>. Assets implements it.
/// </remarks>
public interface ICommunePoleCenters
{
    /// <summary>
    /// commune_id → the mean position and count of the poles that have a position, for those of
    /// <paramref name="communeIds"/> that have any. The caller's commune ids come from the database, not from the token.
    /// </summary>
    Task<IReadOnlyDictionary<string, PoleCenter>> CentersAsync(IReadOnlyCollection<string> communeIds, CancellationToken ct);
}
