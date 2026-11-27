using System.Net;
using LuxMap.Persistence;
using LuxMap.Shared.Contracts.Errors;
using LuxMap.Shared.Http;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;

namespace LuxMap.Modules.Assets;

/// <summary>
/// Reads a commune's outline and turns <see cref="CommuneBoundary.Verdict"/> into the API's refusal (COMMUNE-BOUNDARY-GUARD).
/// </summary>
/// <remarks>
/// The RULE lives in <see cref="CommuneBoundary"/>; this class only carries the query and the error shape, so CRUD, the
/// importers and pole relocation cannot drift into three slightly different messages. The outline is always read by
/// primary key — never with a spatial predicate — because that is the only lookup the ticket asks for and a GIST index
/// would not help it (D-9).
/// </remarks>
internal static class CommuneBoundaryGuard
{
    /// <summary>The commune has an outline and the position is outside it.</summary>
    public const string Outside = "OUTSIDE_COMMUNE_BOUNDARY";

    /// <summary>The commune has no outline yet, so a position cannot be accepted there (D-2).</summary>
    public const string Missing = "COMMUNE_BOUNDARY_MISSING";

    public sealed record Outline(string CommuneId, string Name, MultiPolygon? Boundary);

    /// <summary>The commune's name and outline, or <c>null</c> when the row does not exist.</summary>
    public static async Task<Outline?> ReadAsync(LuxMapDbContext db, string communeId, CancellationToken ct)
    {
        var row = await db.Set<AdministrativeUnit>().AsNoTracking()
            .Where(unit => unit.CommuneId == communeId)
            .Select(unit => new { unit.Name, unit.Boundary })
            .SingleOrDefaultAsync(ct);

        return row is null ? null : new Outline(communeId, row.Name, row.Boundary);
    }

    /// <summary>
    /// Locks the named communes <c>FOR SHARE</c> in id order, then reads their outlines in ONE query (D-5).
    /// </summary>
    /// <remarks>
    /// The caller must already be in a transaction: a lock taken outside one is released before the next statement and
    /// proves nothing. Shared, not exclusive, because several imports and relocations may validate against the same
    /// outline at once; <c>PUT /admin/communes/{id}/boundary</c> needs no lock of its own — its <c>UPDATE</c> holds the row
    /// <c>FOR NO KEY UPDATE</c>, which conflicts with this share lock.
    /// <para>
    /// Communes named in a file that do not exist lock no row and come back absent from the dictionary — the per-row
    /// <c>commune_id</c> check has already reported them.
    /// </para>
    /// </remarks>
    public static async Task<Dictionary<string, Outline>> LockAndReadAsync(
        LuxMapDbContext db, IEnumerable<string?> communeIds, CancellationToken ct)
    {
        var ids = communeIds.Where(id => id is not null).Select(id => id!).Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal).ToArray();
        if (ids.Length == 0)
        {
            return new Dictionary<string, Outline>(StringComparer.Ordinal);
        }

        await db.Database.ExecuteSqlRawAsync(
            "SELECT 1 FROM administrative_unit WHERE commune_id = ANY({0}) ORDER BY commune_id FOR SHARE", [ids], ct);

        var rows = await db.Set<AdministrativeUnit>().AsNoTracking()
            .Where(unit => ids.Contains(unit.CommuneId))
            .Select(unit => new { unit.CommuneId, unit.Name, unit.Boundary })
            .ToListAsync(ct);

        return rows.ToDictionary(
            row => row.CommuneId, row => new Outline(row.CommuneId, row.Name, row.Boundary), StringComparer.Ordinal);
    }

    /// <summary>The 400 for a point the rule refuses. Details are <c>{ field, reason, commune_id }</c> (D-2).</summary>
    public static LuxMapException Refused(Outline outline, BoundaryVerdict verdict, string field) => verdict switch
    {
        BoundaryVerdict.Outside => new LuxMapException(
            ErrorCodes.ValidationFailed, HttpStatusCode.BadRequest, Explain(outline, verdict), Details(outline, Outside, field)),
        BoundaryVerdict.Missing => new LuxMapException(
            ErrorCodes.ValidationFailed, HttpStatusCode.BadRequest, Explain(outline, verdict), Details(outline, Missing, field)),
        _ => throw new ArgumentOutOfRangeException(nameof(verdict), verdict, "Inside is not a refusal."),
    };

    /// <summary>
    /// The sentence both the API refusal and an import row carry. Names the commune, never the outline's coordinates: a
    /// caller who does not already know where the commune ends learns the rule, not the shape (D-2).
    /// </summary>
    public static string Explain(Outline outline, BoundaryVerdict verdict) => verdict switch
    {
        BoundaryVerdict.Outside => $"That position is outside the boundary of commune '{outline.Name}' ({outline.CommuneId}).",
        BoundaryVerdict.Missing => $"Commune '{outline.Name}' ({outline.CommuneId}) has no boundary yet; set one before placing assets there.",
        _ => throw new ArgumentOutOfRangeException(nameof(verdict), verdict, "Inside is not a refusal."),
    };

    private static Dictionary<string, object?> Details(Outline outline, string reason, string field) => new()
    {
        ["field"] = field,
        ["reason"] = reason,
        ["commune_id"] = outline.CommuneId,
    };
}
