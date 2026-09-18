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
using Npgsql;

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

        await RequireFeederInCommuneAsync(request.FeederId, communeId, ct);

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

        // At most ONE lamp in service per pole (BE-REVIEW-02, D-11). A row that arrives already
        // retired is history and may coexist with the active one. The friendly check is here; the
        // partial unique index ux_fixture_pole_id_active is the guard that cannot be raced past —
        // see the catch around SaveChanges below.
        if (request.RemovedDate is null)
        {
            await RejectActiveFixtureAsync(pole.PoleId, ct);
        }
        else
        {
            RequireRemovedAfterInstall(request.RemovedDate.Value, request.InstallDate!.Value);
        }

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

        try
        {
            await dbContext.SaveChangesAsync(ct);
        }
        catch (DbUpdateException failure) when (IsActiveFixtureCollision(failure))
        {
            // Two requests passed the check above at the same time; the index settled it.
            dbContext.ChangeTracker.Clear();
            throw ActiveFixtureConflict(pole.PoleId);
        }

        return fixture.FixtureId;
    }

    /// <summary>
    /// Deletes a pole. The foreign keys decide whether it may go.
    /// </summary>
    /// <remarks>
    /// No soft delete, deliberately. A retirement column would record something that did not happen in
    /// the case this exists to serve — a row typed in by mistake was never a pole that stood and was
    /// taken down — and it would add a filter every future query has to remember, which is the
    /// opposite of the direction this repository keeps moving in.
    /// <para>
    /// <b>The protection is RESTRICT, not a rule in code.</b> <c>fault</c> and <c>lux_reading</c>
    /// reference <c>pole</c> with it, so a pole carrying research data cannot be removed however the
    /// caller asks. Checking in code first would only be a second opinion that can drift from it.
    /// </para>
    /// <para>
    /// ⚠️ <b>It refuses one level deeper too.</b> <c>fixture</c> CASCADES from <c>pole</c>, so the
    /// delete removes the lamps — and a <c>fault</c> against one of those lamps makes that cascade hit
    /// <c>fk_fault_fixture_fixture_id</c>. PostgreSQL aborts the whole statement, so nothing is left
    /// half-deleted, but the pole itself looks unreferenced. That is why the refusal explains itself.
    /// </para>
    /// </remarks>
    public async Task DeletePoleAsync(string poleId, CancellationToken ct)
    {
        // Tracked, and through the query filter: a pole outside the caller's scope is simply not
        // there, which is the 404 Contract section 7 asks for rather than a 403 that would confirm the
        // id exists. Tracking also puts the deletion in front of CommuneWriteGuard, which checks
        // EntityState.Deleted against OriginalValues.
        var pole = await RequireAsync<Pole>(candidate => candidate.PoleId == poleId, "pole", ct);

        dbContext.Set<Pole>().Remove(pole);

        try
        {
            await dbContext.SaveChangesAsync(ct);
        }
        catch (DbUpdateException failure) when (IsForeignKeyViolation(failure))
        {
            // Caught HERE, around SaveChanges, rather than from inside an interceptor. Throwing from
            // within EF's pipeline would let EF wrap it in a DbUpdateException of its own and the
            // BE-04 middleware would answer 500 — the trap CommuneWriteGuard documents at its own
            // throw site.
            dbContext.ChangeTracker.Clear();

            throw new LuxMapException(
                ErrorCodes.AssetInUse,
                HttpStatusCode.Conflict,
                "That pole still has records pointing at it, so it cannot be deleted.",
                DescribeRefusal((PostgresException)failure.InnerException!));
        }
    }

    /// <summary>
    /// Sets or clears which feeder a pole hangs off. <c>null</c> clears it.
    /// </summary>
    /// <remarks>
    /// A narrow PUT with one field, the shape <c>PUT /fixtures/{id}/removal</c> already established:
    /// one endpoint, one intent, and no way to change anything else by accident.
    /// <para>
    /// <b><c>null</c> is a value here, not a missing one.</b> A <c>solar_all_in_one</c> pole sits on no
    /// circuit at all, so "this pole has no feeder" is a fact worth recording rather than an absence.
    /// Telling the two apart is the caller's side of the contract — see <c>SetPoleFeederRequest</c>.
    /// </para>
    /// <para>
    /// ⚠️ <b>The feeder must be in the pole's own commune, and the write guard cannot enforce that.</b>
    /// <c>CommuneWriteGuard</c> reads the <c>commune_id</c> OF THE ROW BEING WRITTEN; the pole's own
    /// commune is in scope, so the write passes however foreign the feeder is. A caller holding two
    /// communes could otherwise wire a pole in one to a circuit in the other. The check below is the
    /// only thing standing there.
    /// </para>
    /// </remarks>
    public async Task SetPoleFeederAsync(string poleId, string? feederId, CancellationToken ct)
    {
        var pole = await RequireAsync<Pole>(candidate => candidate.PoleId == poleId, "pole", ct);

        await RequireFeederInCommuneAsync(feederId, pole.CommuneId, ct);

        pole.FeederId = feederId;
        pole.UpdatedAt = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(ct);
    }

    /// <summary>Retires a lamp. The row stays: the pole's equipment history is the point of the table.</summary>
    /// <remarks>
    /// Once only, and never before the lamp was installed (BE-REVIEW-02, Q-4). A second retirement
    /// would silently rewrite a date that is part of the equipment history; the database CHECK
    /// <c>ck_fixture_removed_after_install</c> stands behind the ordering rule for every other writer.
    /// </remarks>
    public async Task RetireFixtureAsync(string fixtureId, DateOnly removedDate, CancellationToken ct)
    {
        var fixture = await RequireAsync<Fixture>(candidate => candidate.FixtureId == fixtureId, "fixture", ct);

        if (fixture.RemovedDate is { } already)
        {
            throw new LuxMapException(
                ErrorCodes.ValidationFailed,
                HttpStatusCode.BadRequest,
                "That lamp is already retired; its removed_date is part of the equipment history and is not rewritten.",
                new Dictionary<string, object?> { ["removed_date"] = already.ToString("yyyy-MM-dd") });
        }

        RequireRemovedAfterInstall(removedDate, fixture.InstallDate);

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

    /// <summary>
    /// The feeder exists, the caller can see it, and it is in <paramref name="communeId"/>.
    /// </summary>
    /// <remarks>
    /// <c>null</c> passes: a <c>solar_all_in_one</c> pole is on no circuit at all, which is a fact
    /// rather than a missing value.
    /// <para>
    /// ⚠️ <b><c>CommuneWriteGuard</c> cannot do this one.</b> The guard reads the <c>commune_id</c> OF
    /// THE ROW BEING WRITTEN — the pole's own commune, which is in scope — so the write passes however
    /// foreign the feeder is. A caller holding two communes could otherwise wire a pole in one to a
    /// circuit in the other, and nothing downstream would notice.
    /// </para>
    /// <para>
    /// Shared by <see cref="CreatePoleAsync"/> and <see cref="SetPoleFeederAsync"/> deliberately. The
    /// check lived only on the PUT at first and the POST had the hole open; one function called from
    /// both is what stops the two paths from disagreeing again.
    /// </para>
    /// <para>
    /// 🔴 This is an APPLICATION check, not a constraint. A new write path that forgets to call it —
    /// a seeder, psql by hand — reopens the hole. The form that cannot be forgotten is a composite
    /// foreign key on <c>(feeder_id, commune_id)</c>, which needs a migration and is its own ticket;
    /// see <c>docs/contract-drift.md</c> item 43. <c>segment_id</c> carries the same hole, unpatched.
    /// </para>
    /// </remarks>
    private async Task RequireFeederInCommuneAsync(string? feederId, string communeId, CancellationToken ct)
    {
        if (feederId is null)
        {
            return;
        }

        var feeder = await RequireAsync<Feeder>(candidate => candidate.FeederId == feederId, "feeder", ct);

        if (!string.Equals(feeder.CommuneId, communeId, StringComparison.Ordinal))
        {
            // 409 CROSS_COMMUNE_REFERENCE, not 403 (BE-REVIEW-02, D-5): both communes may be inside
            // the caller's scope, so nothing is forbidden to them — the two rows just may not be
            // joined. That is a consistency conflict, the mirror of ASSET_IN_USE on the way out.
            throw new LuxMapException(
                ErrorCodes.CrossCommuneReference,
                HttpStatusCode.Conflict,
                "That feeder belongs to a different commune than the pole.",
                new Dictionary<string, object?>
                {
                    ["pole_commune_id"] = communeId,
                    ["feeder_commune_id"] = feeder.CommuneId,
                });
        }
    }

    private static void RequireRemovedAfterInstall(DateOnly removedDate, DateOnly installDate)
    {
        if (removedDate < installDate)
        {
            throw new LuxMapException(
                ErrorCodes.ValidationFailed,
                HttpStatusCode.BadRequest,
                "removed_date must be on or after install_date.",
                new Dictionary<string, object?>
                {
                    ["removed_date"] = removedDate.ToString("yyyy-MM-dd"),
                    ["install_date"] = installDate.ToString("yyyy-MM-dd"),
                });
        }
    }

    private async Task RejectActiveFixtureAsync(string poleId, CancellationToken ct)
    {
        var occupied = await dbContext.Set<Fixture>().AsNoTracking()
            .AnyAsync(fixture => fixture.PoleId == poleId && fixture.RemovedDate == null, ct);

        if (occupied)
        {
            throw ActiveFixtureConflict(poleId);
        }
    }

    private static LuxMapException ActiveFixtureConflict(string poleId)
        => new(
            ErrorCodes.PoleHasActiveFixture,
            HttpStatusCode.Conflict,
            "That pole already carries a lamp in service. Retire it (PUT /assets/fixtures/{id}/removal) before recording its replacement.",
            new Dictionary<string, object?> { ["pole_id"] = poleId });

    /// <summary>PostgreSQL <c>23505</c> on the one-active-lamp index.</summary>
    private static bool IsActiveFixtureCollision(DbUpdateException failure)
        => failure.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: "ux_fixture_pole_id_active",
        };

    /// <summary>What refused the delete, taken from the database's own answer.</summary>
    /// <remarks>
    /// Read off <c>PostgresException.ConstraintName</c> rather than counted with follow-up queries,
    /// and that is not only cheaper — it is the only version that cannot be wrong. A count is a second
    /// opinion formed after the fact and can disagree with the refusal it claims to explain; the
    /// constraint name IS the refusal.
    /// <para>
    /// It also keeps the module boundary intact. <c>fault</c> and <c>lux_reading</c> belong to Faults
    /// and Survey, which this module does not reference and must not start referencing to write an
    /// error message. Coupling between modules stays at the level of id strings.
    /// </para>
    /// <para>
    /// ⚠️ <c>table</c> can be <c>fixture</c> rather than <c>pole</c>, and that is the informative case:
    /// nothing referenced the pole, its lamp was cascaded into, and a fault on that lamp stopped the
    /// whole statement.
    /// </para>
    /// </remarks>
    private static Dictionary<string, object?> DescribeRefusal(PostgresException failure)
        => new()
        {
            ["constraint"] = failure.ConstraintName,
            ["table"] = failure.TableName,
        };

    /// <summary>PostgreSQL <c>23503</c> — foreign key violation.</summary>
    /// <remarks>
    /// Matched on SQLSTATE, not on the message: the message is localised and names tables that a
    /// migration can rename.
    /// </remarks>
    private static bool IsForeignKeyViolation(DbUpdateException failure)
        => failure.InnerException is PostgresException { SqlState: PostgresErrorCodes.ForeignKeyViolation };

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
