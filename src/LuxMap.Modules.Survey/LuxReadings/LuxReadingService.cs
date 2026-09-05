using System.Net;
using LuxMap.Modules.Assets.Entities;
using LuxMap.Modules.Survey.Entities;
using LuxMap.Persistence;
using LuxMap.Persistence.Conventions;
using LuxMap.Shared.Contracts.Enums;
using LuxMap.Shared.Contracts.Errors;
using LuxMap.Shared.Contracts.Paging;
using LuxMap.Shared.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LuxMap.Modules.Survey.LuxReadings;

/// <summary>Contract section 2.9 — lux readings, the ground truth CV-12 scores against.</summary>
public sealed class LuxReadingService(LuxMapDbContext dbContext, ILogger<LuxReadingService> logger)
{
    /// <summary>Above this a reading is logged as suspicious. It is still stored — see <see cref="LuxReading.LuxValue"/>.</summary>
    public const double ImplausibleLuxThreshold = 200;

    /// <summary>
    /// Creates a reading, or returns the existing one when <c>client_op_id</c> repeats.
    /// </summary>
    /// <returns><c>Created</c> is false when this was a duplicate — the caller answers 200, not 201.</returns>
    public async Task<(bool Created, LuxReadingResponse Reading)> CreateAsync(
        CreateLuxReadingRequest request, string measuredBy, CancellationToken cancellationToken)
    {
        var existing = await dbContext.Set<LuxReading>()
            .SingleOrDefaultAsync(reading => reading.ClientOpId == request.ClientOpId!, cancellationToken);

        if (existing is not null)
        {
            return (false, ToResponse(existing));
        }

        // Read the pole FIRST. The BE-08 query filter is in the WHERE clause, so a pole outside the
        // caller's scope is simply not found — which is why an out-of-scope pole yields 404 and not
        // 403, exactly as Contract section 7 requires for direct resource access.
        var pole = await dbContext.Set<Pole>()
            .SingleOrDefaultAsync(candidate => candidate.PoleId == request.PoleId!, cancellationToken)
            ?? throw new LuxMapException(
                ErrorCodes.PoleNotFound,
                HttpStatusCode.NotFound,
                "That pole does not exist, or it is outside your permitted commune scope.");

        var measuredAt = DateTime.SpecifyKind(request.MeasuredAt!.Value.ToUniversalTime(), DateTimeKind.Utc);

        var reading = new LuxReading
        {
            ClientOpId = request.ClientOpId!,
            PoleId = pole.PoleId,

            // Taken from the pole, never from the body. The write guard checks that a commune is
            // inside the caller's scope; it does NOT check that it matches the pole's commune, so a
            // client-supplied value could pass both checks and still file the reading under the
            // wrong commune.
            CommuneId = pole.CommuneId,

            MeasuredAt = measuredAt,
            LuxValue = request.LuxValue!.Value,
            MeterModel = request.MeterModel,
            DataSource = request.DataSource!.Value,
            Note = request.Note,
            MeasuredBy = measuredBy,
        };

        dbContext.Set<LuxReading>().Add(reading);

        if (reading.LuxValue > ImplausibleLuxThreshold)
        {
            // Logged, never refused. This is ground truth: FO-14 measures once in the field, so a
            // rejected reading is gone for good, while an implausible one stays visible to analysis.
            logger.LogWarning(
                "Lux reading for {PoleId} is {LuxValue} lux, above the {Threshold} lux typical for "
                + "road lighting. Stored unchanged; check the meter range if this looks wrong.",
                reading.PoleId, reading.LuxValue, ImplausibleLuxThreshold);
        }

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsDuplicateClientOp(exception))
        {
            // Two retries can pass the lookup above at the same time; the unique index is the real
            // guard. Losing that race is still a duplicate, and a duplicate is a 200.
            dbContext.Entry(reading).State = EntityState.Detached;

            var winner = await dbContext.Set<LuxReading>()
                .SingleAsync(other => other.ClientOpId == request.ClientOpId!, cancellationToken);

            return (false, ToResponse(winner));
        }

        return (true, ToResponse(reading));
    }

    /// <summary>One pole's series, oldest first — Contract section 2.9, for the pole detail panel.</summary>
    /// <remarks>
    /// Paged, which section 2.9 does not mention for this endpoint. Calibration measurements are
    /// taken close to daily, so a single rig pole accumulates hundreds of points; an unbounded
    /// response would grow without limit. Recorded as deliberate drift.
    /// </remarks>
    public async Task<PagedResult<LuxReadingResponse>> ForPoleAsync(
        string poleId, PageRequest page, CancellationToken cancellationToken)
    {
        var poleExists = await dbContext.Set<Pole>()
            .AnyAsync(pole => pole.PoleId == poleId, cancellationToken);

        if (!poleExists)
        {
            throw new LuxMapException(
                ErrorCodes.PoleNotFound,
                HttpStatusCode.NotFound,
                "That pole does not exist, or it is outside your permitted commune scope.");
        }

        var query = dbContext.Set<LuxReading>()
            .Where(reading => reading.PoleId == poleId)
            .OrderBy(reading => reading.MeasuredAt)
            .ThenBy(reading => reading.CreatedAt);

        var total = await query.CountAsync(cancellationToken);

        var items = await query
            .Skip(page.Skip)
            .Take(page.PageSize)
            .Select(reading => ToResponse(reading))
            .ToListAsync(cancellationToken);

        return PagedResult<LuxReadingResponse>.From(page, total, items);
    }

    /// <summary>The bulk endpoint CV-12 pulls from — Contract section 2.9.</summary>
    public async Task<PagedResult<LuxReadingWithLuminanceResponse>> SearchAsync(
        string? poleId,
        DateTime? from,
        DateTime? to,
        DataSource? dataSource,
        PageRequest page,
        CancellationToken cancellationToken)
    {
        var query = dbContext.Set<LuxReading>().AsQueryable();

        if (!string.IsNullOrWhiteSpace(poleId))
        {
            query = query.Where(reading => reading.PoleId == poleId);
        }

        if (from is not null)
        {
            var lower = DateTime.SpecifyKind(from.Value.ToUniversalTime(), DateTimeKind.Utc);
            query = query.Where(reading => reading.MeasuredAt >= lower);
        }

        if (to is not null)
        {
            var upper = DateTime.SpecifyKind(to.Value.ToUniversalTime(), DateTimeKind.Utc);
            query = query.Where(reading => reading.MeasuredAt <= upper);
        }

        if (dataSource is not null)
        {
            query = query.Where(reading => reading.DataSource == dataSource.Value);
        }

        var ordered = query
            .OrderBy(reading => reading.MeasuredAt)
            .ThenBy(reading => reading.CreatedAt);

        var total = await ordered.CountAsync(cancellationToken);

        var items = await ordered
            .Skip(page.Skip)
            .Take(page.PageSize)
            .ToListAsync(cancellationToken);

        // nearest_luminance is null for every row until BE-15/BE-17 creates luminance_history.
        // The key is still present so CV-12 binds to the final shape today.
        var withLuminance = items
            .Select(reading => new LuxReadingWithLuminanceResponse(
                reading.LuxId,
                reading.ClientOpId,
                reading.PoleId,
                reading.MeasuredAt,
                reading.LuxValue,
                reading.MeterModel,
                ContractEnum.ToDbValue(reading.DataSource),
                reading.Note,
                NearestLuminance: null))
            .ToList();

        return PagedResult<LuxReadingWithLuminanceResponse>.From(page, total, withLuminance);
    }

    private static LuxReadingResponse ToResponse(LuxReading reading)
        => new(
            reading.LuxId,
            reading.ClientOpId,
            reading.PoleId,
            reading.MeasuredAt,
            reading.LuxValue,
            reading.MeterModel,
            ContractEnum.ToDbValue(reading.DataSource),
            reading.Note);

    private static bool IsDuplicateClientOp(DbUpdateException exception)
        => exception.InnerException is Npgsql.PostgresException { SqlState: "23505" } postgres
           && postgres.ConstraintName == "ux_lux_reading_client_op_id";
}
