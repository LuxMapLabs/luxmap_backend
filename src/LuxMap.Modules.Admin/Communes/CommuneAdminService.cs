using System.Net;
using LuxMap.Persistence;
using LuxMap.Shared.Authorization;
using LuxMap.Shared.Contracts.Communes;
using LuxMap.Shared.Contracts.Errors;
using LuxMap.Shared.Contracts.Paging;
using LuxMap.Shared.Http;
using LuxMap.Shared.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace LuxMap.Modules.Admin.Communes;

/// <summary>
/// Communes for the system admin (COMMUNE-BBOX): the name and the map frame. No delete — every scoped table holds its commune
/// with <c>Restrict</c>, and a commune with nothing in it is harmless.
/// </summary>
/// <remarks>
/// <see cref="AdministrativeUnit"/> is the scope anchor and is never filtered (see its remarks), so these reads see every
/// commune; the capability is what keeps them to the system admin, whose scope is <c>*</c> anyway.
/// </remarks>
public sealed class CommuneAdminService(LuxMapDbContext db, ICurrentActorAccessor actor, ILogger<CommuneAdminService> logger)
{
    /// <summary>Long enough for "Phường Long Phước, Thành phố Bà Rịa"; short enough to be a label.</summary>
    public const int NameMaxLength = 200;

    public async Task<PagedResult<CommuneItem>> ListAsync(PageRequest page, CancellationToken ct)
    {
        var query = db.Set<AdministrativeUnit>().AsNoTracking();
        var total = await query.CountAsync(ct);
        // Prefixed ids are at least N digits, not exactly N: order by length before text (CLAUDE.md, section 0).
        var units = await query
            .OrderBy(unit => unit.CreatedAt).ThenBy(unit => unit.CommuneId.Length).ThenBy(unit => unit.CommuneId)
            .Skip(page.Skip).Take(page.PageSize)
            .ToListAsync(ct);
        return PagedResult<CommuneItem>.From(page, total, units.Select(Item).ToList());
    }

    public async Task<CommuneItem> GetAsync(string communeId, CancellationToken ct)
        => Item(await FindAsync(communeId, tracked: false, ct));

    public async Task<CommuneItem> CreateAsync(CreateCommuneRequest request, CancellationToken ct)
    {
        var name = ValidName(request.Name);
        var bbox = ValidBbox(request.Bbox);
        await RequireFreeAsync(name, exceptCommuneId: null, ct);

        var now = UtcMicrosecondClock.UtcNow();
        var unit = new AdministrativeUnit { Name = name, CreatedAt = now, UpdatedAt = now };
        unit.SetBbox(bbox);
        db.Set<AdministrativeUnit>().Add(unit);
        await SaveAsync(ct);

        logger.LogInformation("Commune {CommuneId} created by {AdminId}, bbox set: {HasBbox}.", unit.CommuneId, actor.UserId, bbox is not null);
        return Item(unit);
    }

    public async Task<CommuneItem> UpdateAsync(string communeId, UpdateCommuneRequest request, CancellationToken ct)
    {
        var name = ValidName(request.Name);
        var bbox = ValidBbox(request.Bbox);
        var unit = await FindAsync(communeId, tracked: true, ct);
        await RequireFreeAsync(name, exceptCommuneId: unit.CommuneId, ct);

        unit.Name = name;
        unit.SetBbox(bbox);
        // Only a real change moves updated_at: saving the form untouched is not an edit.
        if (db.Entry(unit).State == EntityState.Modified)
        {
            unit.UpdatedAt = UtcMicrosecondClock.UtcNow();
            await SaveAsync(ct);
            logger.LogInformation("Commune {CommuneId} updated by {AdminId}, bbox set: {HasBbox}.", unit.CommuneId, actor.UserId, bbox is not null);
        }

        return Item(unit);
    }

    private static CommuneItem Item(AdministrativeUnit unit)
    {
        var bbox = unit.GetBbox();
        return new CommuneItem(unit.CommuneId, unit.Name, bbox, bbox?.Center(), unit.CreatedAt, unit.UpdatedAt);
    }

    private static string ValidName(string? requested)
    {
        var name = requested?.Trim();
        return string.IsNullOrEmpty(name) || name.Length > NameMaxLength
            ? throw Invalid("name", $"name is required, at most {NameMaxLength} characters.")
            : name;
    }

    private static CommuneBbox? ValidBbox(CommuneBbox? bbox)
        => bbox is null || bbox.IsValid()
            ? bbox
            : throw Invalid("bbox",
                "bbox needs min_lng < max_lng within ±180 and min_lat < max_lat within ±90, all finite (EPSG:4326, longitude first).");

    private async Task RequireFreeAsync(string name, string? exceptCommuneId, CancellationToken ct)
    {
        if (await db.Set<AdministrativeUnit>().AnyAsync(unit => unit.CommuneId != exceptCommuneId && unit.Name == name, ct))
        {
            throw NameTaken();
        }
    }

    private async Task<AdministrativeUnit> FindAsync(string communeId, bool tracked, CancellationToken ct)
    {
        var query = tracked ? db.Set<AdministrativeUnit>() : db.Set<AdministrativeUnit>().AsNoTracking();
        return await query.SingleOrDefaultAsync(unit => unit.CommuneId == communeId, ct)
            ?? throw new LuxMapException(ErrorCodes.CommuneNotFound, HttpStatusCode.NotFound, "No commune has that id.");
    }

    private async Task SaveAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: "23505" })
        {
            // Lost a race against a concurrent create or rename; ix_administrative_unit_name caught it.
            throw NameTaken();
        }
    }

    private static LuxMapException NameTaken()
        => new(KnownErrors.IdentifierTaken.Code, KnownErrors.IdentifierTaken.StatusCode, "Another commune already has that name.",
            new Dictionary<string, object?> { ["name"] = new[] { "Another commune already has that name." } });

    private static LuxMapException Invalid(string field, string message)
        => new(ErrorCodes.ValidationFailed, HttpStatusCode.BadRequest, message, new Dictionary<string, object?> { ["field"] = field });
}
