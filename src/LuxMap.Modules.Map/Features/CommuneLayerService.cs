using LuxMap.Persistence;
using LuxMap.Shared.Authorization;
using LuxMap.Shared.Contracts.GeoJson;
using Microsoft.EntityFrameworkCore;

namespace LuxMap.Modules.Map.Features;

/// <summary>The flat <c>properties</c> of one commune outline (COMMUNE-BOUNDARY).</summary>
public sealed record CommuneProperties
{
    public required string CommuneId { get; init; }

    public required string Name { get; init; }
}

/// <summary>
/// <c>GET /map/communes</c> (COMMUNE-BOUNDARY): the outlines of the caller's communes, so the map can draw the area each user
/// works in. Communes without an outline are left out.
/// </summary>
/// <remarks>
/// 🔴 <b>The scope is applied HERE, explicitly.</b> <see cref="AdministrativeUnit"/> is the scope anchor and carries no query
/// filter, and <see cref="CommuneFilter.Narrow"/> answers <c>null</c> for "no narrowing — leave it to the query filter". On
/// this table that would be every commune. So a caller who is not system-wide is always held to the ids in their claim, and an
/// empty claim gets nothing — never everything.
/// <para>
/// No <c>bbox</c>, unlike the other layers: a user holds a handful of communes, and the outline is what frames the map in the
/// first place.
/// </para>
/// </remarks>
public sealed class CommuneLayerService(LuxMapDbContext dbContext, ICommuneScopeAccessor scopeAccessor)
{
    public async Task<FeatureCollection<CommuneProperties>> CommunesAsync(IReadOnlyList<string>? narrowedTo, CancellationToken ct)
    {
        var scope = scopeAccessor.Scope;
        var units = dbContext.Set<AdministrativeUnit>().AsNoTracking().Where(unit => unit.Boundary != null);

        if (!scope.IsSystemWide)
        {
            var allowed = scope.CommuneIds;
            units = units.Where(unit => allowed.Contains(unit.CommuneId));
        }

        if (narrowedTo is { Count: > 0 })
        {
            units = units.Where(unit => narrowedTo.Contains(unit.CommuneId));
        }

        // Prefixed ids are at least N digits, not exactly N: order by length before text (CLAUDE.md, Quy ước API).
        var rows = await units
            .OrderBy(unit => unit.CreatedAt).ThenBy(unit => unit.CommuneId.Length).ThenBy(unit => unit.CommuneId)
            .Select(unit => new { unit.CommuneId, unit.Name, unit.Boundary })
            .ToListAsync(ct);

        return new FeatureCollection<CommuneProperties>
        {
            Features =
            [
                .. rows.Select(row => new Feature<CommuneProperties>
                {
                    Geometry = Geometry.MultiPolygon(row.Boundary!.Geometries.Cast<NetTopologySuite.Geometries.Polygon>()
                        .Select(polygon => new[] { polygon.ExteriorRing }.Concat(polygon.InteriorRings)
                            .Select(ring => ring.Coordinates.Select(c => (c.X, c.Y))))),
                    Properties = new CommuneProperties { CommuneId = row.CommuneId, Name = row.Name },
                }),
            ],
        };
    }
}
