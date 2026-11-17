using LuxMap.Modules.Assets.Entities;
using LuxMap.Modules.Identity.Auth;
using LuxMap.Persistence;
using LuxMap.Shared.Contracts.Communes;
using Microsoft.EntityFrameworkCore;

namespace LuxMap.Modules.Assets;

/// <summary>Assets' answer to <see cref="ICommunePoleCenters"/> (MAP-CENTER).</summary>
public sealed class CommunePoleCenters(LuxMapDbContext db) : ICommunePoleCenters
{
    /// <remarks>
    /// Read with <c>IgnoreQueryFilters</c>: <c>GET /auth/me</c> answers with the communes the account holds NOW, read from its
    /// row, while the filter goes by the token's claims, which lag an assignment by up to 60 minutes. Scope still holds — the
    /// ids are that account's own, and only an average and a count leave the query, never a pole.
    /// </remarks>
    public async Task<IReadOnlyDictionary<string, PoleCenter>> CentersAsync(
        IReadOnlyCollection<string> communeIds, CancellationToken ct)
    {
        if (communeIds.Count == 0)
        {
            return new Dictionary<string, PoleCenter>();
        }

        var rows = await db.Set<Pole>().IgnoreQueryFilters().AsNoTracking()
            .Where(pole => communeIds.Contains(pole.CommuneId) && pole.Geom != null)
            .GroupBy(pole => pole.CommuneId)
            .Select(group => new
            {
                CommuneId = group.Key,
                Count = group.Count(),
                Lat = group.Average(pole => pole.Geom!.Y),
                Lng = group.Average(pole => pole.Geom!.X),
            })
            .ToListAsync(ct);
        return rows.ToDictionary(row => row.CommuneId, row => new PoleCenter(row.Lat, row.Lng, row.Count));
    }
}
