using System.Net;
using LuxMap.Modules.Assets.Entities;
using LuxMap.Modules.Assets.Import;
using LuxMap.Persistence;
using LuxMap.Shared.Authorization;
using LuxMap.Shared.Contracts.Errors;
using LuxMap.Shared.Contracts.Paging;
using LuxMap.Shared.Http;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;

namespace LuxMap.Modules.Assets.Crud;

/// <summary>
/// Asset CRUD (BE-12a) — the write half of asset management, plus a listing that returns ids only.
/// </summary>
/// <remarks>
/// ⚠️ <b>No delete.</b> Removing a pole cascades into <c>pole_current_status</c>, a table BE-12 must
/// never touch, and <c>fault</c> and <c>lux_reading</c> both point at poles with <c>Restrict</c>, so
/// any pole carrying research data could not be deleted anyway. Retiring equipment is what
/// <c>fixture.removed_date</c> is for.
/// </remarks>
public sealed class AssetCrudService(LuxMapDbContext dbContext, ICommuneScopeAccessor scopeAccessor)
{
    public Task<PagedResult<string>> ListSegmentsAsync(IReadOnlyList<string>? communes, PageRequest page, CancellationToken ct)
        => ListAsync<RoadSegment>(communes, page, segment => segment.SegmentId, ct);

    public Task<PagedResult<string>> ListFeedersAsync(IReadOnlyList<string>? communes, PageRequest page, CancellationToken ct)
        => ListAsync<Feeder>(communes, page, feeder => feeder.FeederId, ct);

    public Task<PagedResult<string>> ListPolesAsync(IReadOnlyList<string>? communes, PageRequest page, CancellationToken ct)
        => ListAsync<Pole>(communes, page, pole => pole.PoleId, ct);

    public async Task<string> CreateSegmentAsync(CreateSegmentRequest request, CancellationToken ct)
    {
        var communeId = await CheckedCommuneAsync(request.CommuneId!, ct);
        await RejectDuplicateRefAsync<RoadSegment>(communeId, request.ExternalRef, ct);

        var segment = new RoadSegment
        {
            ExternalRef = request.ExternalRef,
            SegmentName = request.SegmentName!,
            RoadClass = request.RoadClass!.Value,
            LengthM = request.LengthM!.Value,
            Geom = Read<LineString>(request.GeomWkt),
            CommuneId = communeId,
            DataSource = request.DataSource!.Value,
        };

        dbContext.Set<RoadSegment>().Add(segment);
        await dbContext.SaveChangesAsync(ct);
        return segment.SegmentId;
    }

    public async Task<string> CreateFeederAsync(CreateFeederRequest request, CancellationToken ct)
    {
        var communeId = await CheckedCommuneAsync(request.CommuneId!, ct);
        await RejectDuplicateRefAsync<Feeder>(communeId, request.ExternalRef, ct);

        var feeder = new Feeder
        {
            ExternalRef = request.ExternalRef,
            FeederName = request.FeederName!,
            CommuneId = communeId,
            Geom = request.GeomWkt is null ? null : Read<LineString>(request.GeomWkt),
        };

        dbContext.Set<Feeder>().Add(feeder);
        await dbContext.SaveChangesAsync(ct);
        return feeder.FeederId;
    }

    public async Task<string> CreatePoleAsync(CreatePoleRequest request, CancellationToken ct)
    {
        var communeId = await CheckedCommuneAsync(request.CommuneId!, ct);
        await RejectDuplicateRefAsync<Pole>(communeId, request.ExternalRef, ct);

        // Read through the query filter: a segment outside the caller's scope is simply not there,
        // which is the 404 Contract section 7 asks for rather than a 403 that would confirm it exists.
        await RequireAsync<RoadSegment>(segment => segment.SegmentId == request.SegmentId, "road segment", ct);

        if (request.FeederId is not null)
        {
            await RequireAsync<Feeder>(feeder => feeder.FeederId == request.FeederId, "feeder", ct);
        }

        var pole = new Pole
        {
            ExternalRef = request.ExternalRef,
            SegmentId = request.SegmentId!,
            FeederId = request.FeederId,
            CommuneId = communeId,
            Geom = Read<Point>(request.GeomWkt),
            NearSensitivePoi = request.NearSensitivePoi,
            DataSource = request.DataSource!.Value,
        };

        dbContext.Set<Pole>().Add(pole);
        await dbContext.SaveChangesAsync(ct);
        return pole.PoleId;
    }

    /// <summary>
    /// Records a lamp installation. This is also how a lamp is REPLACED: set <c>removed_date</c> on
    /// the old row, then create a new one.
    /// </summary>
    public async Task<string> CreateFixtureAsync(CreateFixtureRequest request, CancellationToken ct)
    {
        var pole = await RequireAsync<Pole>(candidate => candidate.PoleId == request.PoleId, "pole", ct);

        var fixture = new Fixture
        {
            PoleId = pole.PoleId,

            // From the pole, never from the body — the same rule as LuxReading. A body-supplied value
            // could name a commune the caller is allowed to write while the pole sits in another, and
            // both the scope check and the write guard would wave it through.
            CommuneId = pole.CommuneId,

            FixtureType = request.FixtureType!.Value,
            PowerSource = request.PowerSource!.Value,
            LampWatt = request.LampWatt!.Value,
            InstallDate = request.InstallDate!.Value,
            RemovedDate = request.RemovedDate,
            WarrantyExpiry = request.WarrantyExpiry,
            DataSource = request.DataSource!.Value,
        };

        dbContext.Set<Fixture>().Add(fixture);
        await dbContext.SaveChangesAsync(ct);
        return fixture.FixtureId;
    }

    /// <summary>Retires a lamp. The row stays: the pole's equipment history is the point of the table.</summary>
    public async Task RetireFixtureAsync(string fixtureId, DateOnly removedDate, CancellationToken ct)
    {
        var fixture = await RequireAsync<Fixture>(candidate => candidate.FixtureId == fixtureId, "fixture", ct);

        fixture.RemovedDate = removedDate;
        fixture.UpdatedAt = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(ct);
    }

    private async Task<PagedResult<string>> ListAsync<TEntity>(
        IReadOnlyList<string>? communes,
        PageRequest page,
        System.Linq.Expressions.Expression<Func<TEntity, string>> id,
        CancellationToken ct)
        where TEntity : class, ICommuneScoped
    {
        var query = dbContext.Set<TEntity>().AsNoTracking();

        if (communes is not null)
        {
            query = query.Where(entity => communes.Contains(entity.CommuneId));
        }

        var total = await query.CountAsync(ct);
        var items = await query
            // NEVER OrderBy the display id: the width is a MINIMUM, so POLE-10000 sorts before
            // POLE-9999 as text. created_at is the stable order (Contract section 0.3). Reached by
            // name because the three asset types share the column but no common base type.
            .OrderBy(entity => EF.Property<DateTime>(entity, "CreatedAt"))
            .ThenBy(id)
            .Skip(page.Skip)
            .Take(page.PageSize)
            .Select(id)
            .ToListAsync(ct);

        return PagedResult<string>.From(page, total, items);
    }

    /// <summary>The commune must exist AND be inside the caller's scope, or this is a 403.</summary>
    private async Task<string> CheckedCommuneAsync(string communeId, CancellationToken ct)
    {
        // Narrow is the entry-point check Contract section 7 asks for: it answers 403 naming the
        // rejected commune, where the query filter would only ever produce a silent empty result.
        CommuneFilter.Narrow(scopeAccessor.Scope, [communeId]);

        var exists = await dbContext.Set<AdministrativeUnit>().AsNoTracking()
            .AnyAsync(unit => unit.CommuneId == communeId, ct);

        return exists
            ? communeId
            : throw new LuxMapException(
                ErrorCodes.ValidationFailed,
                HttpStatusCode.BadRequest,
                "That commune does not exist.",
                new Dictionary<string, object?> { ["commune_id"] = communeId });
    }

    private async Task RejectDuplicateRefAsync<TEntity>(string communeId, string? externalRef, CancellationToken ct)
        where TEntity : class, ICommuneScoped, IExternallyReferenced
    {
        if (externalRef is null)
        {
            return;
        }

        var taken = await dbContext.Set<TEntity>().AsNoTracking()
            .AnyAsync(entity => entity.CommuneId == communeId && entity.ExternalRef == externalRef, ct);

        if (taken)
        {
            throw new LuxMapException(
                ErrorCodes.ExternalRefTaken,
                HttpStatusCode.Conflict,
                "That inventory code is already used in this commune.",
                new Dictionary<string, object?> { ["external_ref"] = externalRef, ["commune_id"] = communeId });
        }
    }

    private async Task<TEntity> RequireAsync<TEntity>(
        System.Linq.Expressions.Expression<Func<TEntity, bool>> predicate, string what, CancellationToken ct)
        where TEntity : class
        => await dbContext.Set<TEntity>().FirstOrDefaultAsync(predicate, ct)
            ?? throw new LuxMapException(
                ErrorCodes.AssetNotFound,
                HttpStatusCode.NotFound,
                $"That {what} does not exist, or it is outside your permitted commune scope.");

    private static TGeometry Read<TGeometry>(string? wkt)
        where TGeometry : Geometry
    {
        if (!AssetGeometry.TryReadWkt(wkt, out var geometry, out var error))
        {
            throw new LuxMapException(
                ErrorCodes.ValidationFailed,
                HttpStatusCode.BadRequest,
                "The geometry could not be read.",
                new Dictionary<string, object?> { ["geom_wkt"] = error });
        }

        return geometry as TGeometry
            ?? throw new LuxMapException(
                ErrorCodes.ValidationFailed,
                HttpStatusCode.BadRequest,
                $"Expected a {typeof(TGeometry).Name}.",
                new Dictionary<string, object?> { ["geom_wkt"] = $"got {geometry!.GeometryType}" });
    }
}
