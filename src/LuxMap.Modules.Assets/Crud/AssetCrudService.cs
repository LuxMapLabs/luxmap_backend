using System.Net;
using LuxMap.Modules.Assets.Entities;
using LuxMap.Modules.Assets.Import;
using LuxMap.Persistence;
using LuxMap.Shared.Authorization;
using LuxMap.Shared.Contracts.Enums;
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
/// <b>Poles are the one asset with a DELETE</b> (BE-12, drift 43); fixtures have none. A pole row typed
/// in by mistake was never a pole that stood and was taken down, so deleting it records nothing false,
/// and the foreign keys — <c>fault</c> and <c>lux_reading</c> hold it with <c>Restrict</c> — decide
/// whether it may go. Retiring a lamp is a real event, which is what <c>fixture.removed_date</c> is for.
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
    /// Full replacement of a road segment (BE-12). <c>commune_id</c> is not writable.
    /// </summary>
    /// <remarks>
    /// Read through the query filter, so a segment outside the caller's scope is a 404 rather than a
    /// 403 that would confirm the id exists — Contract section 7.
    /// <para>
    /// <c>length_m</c> is overwritten with what the caller sent and is never recomputed from the new
    /// geometry. It is a DECLARED value (BE-10, rule 4): deriving it with <c>ST_Length</c> would make
    /// the number the front end shows shift by about 73 ppm for reasons nobody could explain.
    /// </para>
    /// </remarks>
    public async Task UpdateSegmentAsync(string segmentId, UpdateSegmentRequest request, CancellationToken ct)
    {
        var segment = await RequireAsync<RoadSegment>(candidate => candidate.SegmentId == segmentId, "road segment", ct);

        await RejectDuplicateRefAsync<RoadSegment>(segment.CommuneId, request.ExternalRef, ct, segment.ExternalRef);

        segment.ExternalRef = request.ExternalRef;
        segment.SegmentName = request.SegmentName!;
        segment.RoadClass = request.RoadClass!.Value;
        segment.LengthM = request.LengthM!.Value;
        segment.Geom = Read<LineString>(request.GeomWkt);
        segment.DataSource = request.DataSource!.Value;
        segment.UpdatedAt = DateTime.UtcNow;

        await dbContext.SaveChangesAsync(ct);
    }

    /// <summary>Full replacement of a feeder (BE-12). <c>commune_id</c> is not writable.</summary>
    /// <remarks>
    /// ⚠️ <b>Changing a feeder's commune is not possible here, and that is what keeps the poles on it
    /// consistent.</b> <c>RequireFeederInCommuneAsync</c> checks the match when a POLE is written; it
    /// never runs when the FEEDER moves. Were the commune writable, a feeder could be walked out from
    /// under poles that are already wired to it and every one of those pairs would quietly become
    /// cross-commune, with no write left to catch it.
    /// </remarks>
    public async Task UpdateFeederAsync(string feederId, UpdateFeederRequest request, CancellationToken ct)
    {
        var feeder = await RequireAsync<Feeder>(candidate => candidate.FeederId == feederId, "feeder", ct);

        await RejectDuplicateRefAsync<Feeder>(feeder.CommuneId, request.ExternalRef, ct, feeder.ExternalRef);

        feeder.ExternalRef = request.ExternalRef;
        feeder.FeederName = request.FeederName!;
        feeder.Geom = request.GeomWkt is null ? null : Read<LineString>(request.GeomWkt);
        feeder.UpdatedAt = DateTime.UtcNow;

        await dbContext.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Full replacement of a pole (BE-12). <c>commune_id</c> is not writable.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>This can clear <c>feeder_id</c>, and silently, because that is what a full replacement
    /// means.</b> A caller that sends a partial body loses the pole's circuit. <c>PUT
    /// /assets/poles/{id}/feeder</c> is the endpoint for touching only the circuit, and it tells an
    /// absent key from an explicit <c>null</c>.
    /// <para>
    /// The feeder check is <see cref="RequireFeederInCommuneAsync"/> — the SAME call the create path
    /// and the narrow feeder endpoint make. Three write paths reaching one function is deliberate:
    /// the check lived on only one of them once before, and the other had the hole open.
    /// </para>
    /// </remarks>
    public async Task UpdatePoleAsync(string poleId, UpdatePoleRequest request, CancellationToken ct)
    {
        var pole = await RequireAsync<Pole>(candidate => candidate.PoleId == poleId, "pole", ct);

        await RejectDuplicateRefAsync<Pole>(pole.CommuneId, request.ExternalRef, ct, pole.ExternalRef);

        // Through the query filter: a segment the caller cannot see is not there, so this is a 404.
        await RequireAsync<RoadSegment>(segment => segment.SegmentId == request.SegmentId, "road segment", ct);

        await RequireFeederInCommuneAsync(request.FeederId, pole.CommuneId, ct);

        pole.ExternalRef = request.ExternalRef;
        pole.SegmentId = request.SegmentId!;
        pole.FeederId = request.FeederId;
        pole.Geom = Read<Point>(request.GeomWkt);
        pole.NearSensitivePoi = request.NearSensitivePoi;
        pole.DataSource = request.DataSource!.Value;
        pole.UpdatedAt = DateTime.UtcNow;

        await dbContext.SaveChangesAsync(ct);
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
    public Task DeletePoleAsync(string poleId, CancellationToken ct)
        => DeleteAsync<Pole>(
            candidate => candidate.PoleId == poleId,
            "pole",
            "That pole still has records pointing at it, so it cannot be deleted.",
            ct);

    /// <summary>
    /// Deletes a road segment. The foreign keys decide whether it may go (BE-12).
    /// </summary>
    /// <remarks>
    /// Three tables hold a segment with RESTRICT and they are in TWO modules: <c>pole.segment_id</c>
    /// here, <c>fault.segment_id</c> and <c>fault_cluster.segment_id</c> in Faults. So a segment that
    /// looks empty of poles can still be refused, and the constraint name in <c>details</c> is the
    /// only thing that says which one spoke.
    /// <para>
    /// No soft delete, for the reason the pole delete gives: a segment typed in by mistake was never
    /// a road that existed and was decommissioned, and a retirement flag would record something that
    /// did not happen while adding a filter every later query has to remember.
    /// </para>
    /// </remarks>
    public Task DeleteSegmentAsync(string segmentId, CancellationToken ct)
        => DeleteAsync<RoadSegment>(
            candidate => candidate.SegmentId == segmentId,
            "road segment",
            "That road segment still has poles, faults or fault clusters pointing at it, so it cannot be deleted.",
            ct);

    /// <summary>
    /// Deletes a feeder. The foreign keys decide whether it may go (BE-12).
    /// </summary>
    /// <remarks>
    /// Only <c>pole.feeder_id</c> points here, and it is RESTRICT and NULLABLE — so a feeder that
    /// still has poles is refused rather than quietly unwiring them. Clearing the circuit is a
    /// decision somebody makes per pole through <c>PUT /assets/poles/{id}/feeder</c>, not a side
    /// effect of deleting the cabinet.
    /// </remarks>
    public Task DeleteFeederAsync(string feederId, CancellationToken ct)
        => DeleteAsync<Feeder>(
            candidate => candidate.FeederId == feederId,
            "feeder",
            "That feeder still has poles wired to it, so it cannot be deleted.",
            ct);

    /// <summary>Loads the row, removes it, and turns the database's refusal into a 409.</summary>
    /// <remarks>
    /// <b>Tracked, and through the query filter.</b> A row outside the caller's scope is simply not
    /// there, which is the 404 Contract section 7 asks for rather than a 403 that would confirm the id
    /// exists. Tracking also puts the deletion in front of <c>CommuneWriteGuard</c>, which checks
    /// <c>EntityState.Deleted</c> against <c>OriginalValues</c>.
    /// <para>
    /// ⚠️ <b>This is why <c>ExecuteDelete</c> is banned.</b> It would issue the DELETE straight to SQL,
    /// past the ChangeTracker the guard walks — the same hole on the write side that the query filter
    /// left on the read side. See <c>BannedSymbols.txt</c>.
    /// </para>
    /// </remarks>
    private async Task DeleteAsync<TEntity>(
        System.Linq.Expressions.Expression<Func<TEntity, bool>> predicate,
        string what,
        string refusal,
        CancellationToken ct)
        where TEntity : class
    {
        var entity = await RequireAsync(predicate, what, ct);

        dbContext.Set<TEntity>().Remove(entity);

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
                refusal,
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

    // ── BE-13 topology ────────────────────────────────────────────────────────────────────────
    //
    // ⚠️ PROVISIONAL — the Contract specifies no topology endpoint. Proposed in
    // docs/review/BE-13-topology-shape.md, registered as drift 46, not stable until the next FW.

    /// <summary>Every pole hanging off one circuit. The query CV-15 clusters along.</summary>
    /// <remarks>
    /// The feeder is READ FIRST, and that is not a redundant round trip. It is what makes a feeder
    /// outside the caller's commune answer <b>404</b> rather than an empty page: the query filter
    /// makes the row not exist for them, and Contract section 7 wants absence, not 403. Returning an
    /// empty list instead would turn the endpoint into a probe for whether a given feeder id exists
    /// in some other commune — the same reasoning decision A settled for <c>GET /faults?pole_id=</c>.
    /// </remarks>
    public async Task<PagedResult<TopologyPole>> ListPolesOnFeederAsync(
        string feederId, PageRequest page, CancellationToken ct)
    {
        await RequireAsync<Feeder>(feeder => feeder.FeederId == feederId, "feeder", ct);

        return await TopologyPageAsync(pole => pole.FeederId == feederId, page, withPowerSource: false, ct);
    }

    /// <summary>Every pole on one road segment.</summary>
    /// <remarks>
    /// ⚠️ These poles need NOT all be in the segment's own commune. <c>road_class =
    /// inter_commune</c> means the road runs between communes, so a pole belonging to a neighbour is
    /// correct data (BE-REVIEW-02, constraint 1). The caller still only sees what their own scope
    /// permits, because the query filter applies to <c>pole</c> independently of the segment.
    /// </remarks>
    public async Task<PagedResult<TopologyPole>> ListPolesOnSegmentAsync(
        string segmentId, PageRequest page, CancellationToken ct)
    {
        await RequireAsync<RoadSegment>(segment => segment.SegmentId == segmentId, "road segment", ct);

        return await TopologyPageAsync(pole => pole.SegmentId == segmentId, page, withPowerSource: false, ct);
    }

    /// <summary>Poles on no circuit — what CV-05 still has to place, plus every solar pole.</summary>
    /// <remarks>
    /// This listing is the ONE that carries <c>power_source</c>, because it is the only one where the
    /// caller has to separate "solar, so it has no circuit" from "nobody has assigned it yet". Both
    /// are <c>feeder_id = NULL</c> and indistinguishable without it.
    /// </remarks>
    public Task<PagedResult<TopologyPole>> ListPolesWithoutFeederAsync(
        IReadOnlyList<string>? communes, PageRequest page, CancellationToken ct)
    {
        var scoped = communes is null
            ? (System.Linq.Expressions.Expression<Func<Pole, bool>>)(pole => pole.FeederId == null)
            : pole => pole.FeederId == null && communes.Contains(pole.CommuneId);

        return TopologyPageAsync(scoped, page, withPowerSource: true, ct);
    }

    /// <summary>The one query body the three topology listings share.</summary>
    /// <remarks>
    /// <para>
    /// <c>power_source</c> comes from the pole's ACTIVE lamp — the one with no <c>removed_date</c> —
    /// and BE-REVIEW-02 constraint 3 made that unique per pole (<c>ux_fixture_pole_id_active</c>), so
    /// there is exactly one to read and no aggregation rule to invent. A pole with no lamp at all
    /// reports null rather than a guess.
    /// </para>
    /// <para>
    /// Projected in ONE query rather than loading poles and then their lamps: the N+1 that
    /// BE-REVIEW-02 finding F-03 already had to remove once.
    /// </para>
    /// </remarks>
    private async Task<PagedResult<TopologyPole>> TopologyPageAsync(
        System.Linq.Expressions.Expression<Func<Pole, bool>> predicate,
        PageRequest page,
        bool withPowerSource,
        CancellationToken ct)
    {
        var query = dbContext.Set<Pole>().AsNoTracking().Where(predicate);

        var total = await query.CountAsync(ct);
        var items = await query
            // Never OrderBy the display id — the width is a MINIMUM, so POLE-10000 sorts before
            // POLE-9999 as text (Contract section 0.3). Same rule as ListAsync.
            .OrderBy(pole => pole.CreatedAt)
            .ThenBy(pole => pole.PoleId)
            .Skip(page.Skip)
            .Take(page.PageSize)
            .Select(pole => new TopologyPole
            {
                PoleId = pole.PoleId,
                SegmentId = pole.SegmentId,
                FeederId = pole.FeederId,
                PowerSource = withPowerSource
                    ? pole.Fixtures
                        .Where(lamp => lamp.RemovedDate == null)
                        .Select(lamp => (PowerSource?)lamp.PowerSource)
                        .FirstOrDefault()
                    : null,
                // EPSG:4326 straight off the column. 3405 never leaves the SQL tree (BE-10, rule 3).
                Lat = pole.Geom.Y,
                Lng = pole.Geom.X,
            })
            .ToListAsync(ct);

        return PagedResult<TopologyPole>.From(page, total, items);
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

    /// <param name="currentRef">
    /// What the row being updated already holds, so keeping the code unchanged does not collide with
    /// the row's own entry. <c>null</c> on the create path, where there is no row yet.
    /// </param>
    private async Task RejectDuplicateRefAsync<TEntity>(
        string communeId, string? externalRef, CancellationToken ct, string? currentRef = null)
        where TEntity : class, ICommuneScoped, IExternallyReferenced
    {
        if (externalRef is null || string.Equals(externalRef, currentRef, StringComparison.Ordinal))
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
    /// <b>O-7 put a composite foreign key on <c>(feeder_id, commune_id)</c> underneath this, so a
    /// write path that forgets to call it no longer reopens the hole</b> — see
    /// <c>PoleConfiguration</c>. This check is still the one that ANSWERS: it names both communes in
    /// a 409 <c>CROSS_COMMUNE_REFERENCE</c>, where the constraint alone would surface as a bare
    /// <c>DbUpdateException</c> and a 500. Same two-layer shape as <c>CommuneFilter.Narrow</c> over
    /// <c>CommuneWriteGuard</c>: the readable refusal in front, the one that cannot be forgotten
    /// behind.
    /// </para>
    /// <para>
    /// <c>segment_id</c> gets NO such key, and that is not an omission: <c>road_class =
    /// inter_commune</c> means the road runs BETWEEN communes, so a pole in a different commune from
    /// its segment's owner is correct data (BE-REVIEW-02, constraint 1).
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
