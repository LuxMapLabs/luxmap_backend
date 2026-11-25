using System.Net;
using LuxMap.Modules.Assets.Entities;
using LuxMap.Modules.Survey.Entities;
using LuxMap.Modules.WorkOrders;
using LuxMap.Modules.WorkOrders.Entities;
using LuxMap.Persistence;
using LuxMap.Persistence.Conventions;
using LuxMap.Shared.Contracts.Enums;
using LuxMap.Shared.Contracts.Errors;
using LuxMap.Shared.Contracts.Paging;
using LuxMap.Shared.Http;
using LuxMap.Shared.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using LuxMap.Modules.Survey.Review;

namespace LuxMap.Modules.Survey.LuxReadings;

/// <summary>Contract section 2.9 — lux readings, the ground truth CV-12 scores against.</summary>
public sealed class LuxReadingService(LuxMapDbContext dbContext, ILogger<LuxReadingService> logger, IOptions<LuxProtocolOptions> options)
{
    /// <summary>Above this a reading is logged as suspicious. It is still stored — see <see cref="LuxReading.LuxValue"/>.</summary>
    public const double ImplausibleLuxThreshold = 200;

    /// <summary>
    /// Refuses a body that sets something the server owns.
    /// </summary>
    /// <remarks>
    /// Here rather than in the controller so the offline queue (BE-43 <c>POST /sync/push</c>) refuses the
    /// same bodies. Loudly, not silently. A client that sent <c>commune_id</c> and got 201 back would reasonably
    /// believe it chose the commune — and it did not.
    /// </remarks>
    public static void RejectServerOwnedFields(CreateLuxReadingRequest request)
    {
        var offending = new List<string>();

        if (!string.IsNullOrWhiteSpace(request.LuxId))
        {
            offending.Add("lux_id");
        }

        if (!string.IsNullOrWhiteSpace(request.CommuneId))
        {
            offending.Add("commune_id");
        }

        if (offending.Count == 0)
        {
            return;
        }

        throw new LuxMapException(
            ErrorCodes.ServerOwnedField,
            HttpStatusCode.BadRequest,
            "These fields are set by the server and must not be sent: "
            + string.Join(", ", offending)
            + ". The id comes from the database and the commune is taken from the pole.",
            new Dictionary<string, object?> { ["fields"] = offending.ToArray() });
    }

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
            return (false, await ResponseAsync(existing, cancellationToken));
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

        // LUX-BASELINE D-5: a protocol reading off the setup is refused with its reason, here in the service so /sync/push
        // refuses it the same way. Pure checks, before any lock.
        double? window = request.Protocol is null ? null : CheckProtocol(request.LuxValue!.Value, request.Protocol);

        var measuredAt = DateTime.SpecifyKind(request.MeasuredAt!.Value.ToUniversalTime(), DateTimeKind.Utc);

        var reading = new LuxReading
        {
            ClientOpId = request.ClientOpId!,
            PoleId = pole.PoleId,
            PhotoId = request.PhotoId,

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
            SensorHeightM = request.Protocol?.SensorHeightM,
            ModuleId = request.Protocol?.ModuleId?.Trim(),
            ModuleFirmwareVersionId = request.Protocol?.ModuleFirmwareVersionId,
            OpticalFilterTransmittance = request.Protocol?.OpticalFilterTransmittance,
            WindowSpread = window,
            Samples = request.Protocol?.Samples?.Select((lux, seq) => new LuxReadingSample { LuxId = "", Seq = seq, Lux = lux }).ToList() ?? [],
        };

        // D-10: linking to a photo takes the photo's locks in the order the decision takes them, so a reading and a Manager's
        // decision on the same photo cannot both pass their check. Own transaction unless a caller already holds one.
        await using var transaction = request.PhotoId is not null && dbContext.Database.CurrentTransaction is null
            ? await dbContext.Database.BeginTransactionAsync(cancellationToken)
            : null;

        if (request.PhotoId is not null)
        {
            await CheckPhotoAsync(request.PhotoId, pole, cancellationToken);
        }

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
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception, PhotoLinkIndex))
        {
            // Lost the race for this photo to another reading. No query after the failure: under /sync/push this may
            // run inside a transaction the failure has already aborted.
            Detach(reading);
            throw PhotoAlreadyLinked(request.PhotoId!);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception, ClientOpIndex))
        {
            // Two retries can pass the lookup above at the same time; the unique index is the real
            // guard. Losing that race is still a duplicate, and a duplicate is a 200. Our own aborted transaction is rolled
            // back before the read; a caller's is the caller's to end.
            Detach(reading);
            if (transaction is not null) await transaction.RollbackAsync(cancellationToken);

            var winner = await dbContext.Set<LuxReading>()
                .SingleAsync(other => other.ClientOpId == request.ClientOpId!, cancellationToken);

            return (false, await ResponseAsync(winner, cancellationToken));
        }

        return (true, await ResponseAsync(reading, cancellationToken));
    }

    private void Detach(LuxReading reading)
    {
        foreach (var sample in reading.Samples) dbContext.Entry(sample).State = EntityState.Detached;
        dbContext.Entry(reading).State = EntityState.Detached;
    }

    /// <summary>D-4 / D-5: the window's spread when the protocol holds; otherwise the 400 the engineer acts on at the pole.</summary>
    private double CheckProtocol(double luxValue, LuxProtocolRequest protocol)
    {
        var o = options.Value;
        var samples = protocol.Samples ?? [];
        if (string.IsNullOrWhiteSpace(protocol.ModuleId)) throw OptionalJson.Invalid("protocol.module_id");
        if (protocol.SensorHeightM is not { } height || !double.IsFinite(height) || height <= 0)
            throw OptionalJson.Invalid("protocol.sensor_height_m");
        if (protocol.OpticalFilterTransmittance is { } t && !(t > 0 && t <= 1))
            throw OptionalJson.Invalid("protocol.optical_filter_transmittance");
        if (samples.Any(sample => !double.IsFinite(sample) || sample < 0)) throw OptionalJson.Invalid("protocol.samples");
        if (Math.Abs(height - o.SensorHeightM) > o.SensorHeightToleranceM)
            throw new LuxMapException(ErrorCodes.LuxProtocolMismatch, HttpStatusCode.BadRequest,
                "The sensor was not at the configured height. Set the pole to the marked height and measure again.",
                new Dictionary<string, object?> { ["sensor_height_m"] = height, ["expected_sensor_height_m"] = o.SensorHeightM,
                    ["tolerance_m"] = o.SensorHeightToleranceM });
        if (samples.Length < o.MinSamples)
            throw Unstable("Too few samples in the window.", samples.Length, null);
        var spread = StationaryLuxRules.Spread(samples);
        if (spread > o.MaxWindowSpread)
            throw Unstable("The reading swung too much during the window (a passing headlight?).", samples.Length, spread);
        var median = StationaryLuxRules.Median(samples);
        if (Math.Abs(luxValue - median) > o.LuxValueTolerance * median)
            throw new LuxMapException(ErrorCodes.LuxSamplesMismatch, HttpStatusCode.BadRequest,
                "lux_value must be the median of protocol.samples.",
                new Dictionary<string, object?> { ["lux_value"] = luxValue, ["samples_median"] = median });
        return spread;

        LuxMapException Unstable(string message, int count, double? measured) => new(ErrorCodes.LuxWindowUnstable,
            HttpStatusCode.BadRequest, message + " Measure again on the spot.",
            new Dictionary<string, object?> { ["sample_count"] = count, ["min_samples"] = o.MinSamples,
                ["window_spread"] = measured is { } m && double.IsFinite(m) ? m : null, ["max_window_spread"] = o.MaxWindowSpread });
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

        var rows = await query
            .Skip(page.Skip)
            .Take(page.PageSize)
            .ToListAsync(cancellationToken);

        return PagedResult<LuxReadingResponse>.From(page, total, await ResponsesAsync(rows, cancellationToken));
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

        // Query-string dates do NOT pass through UtcDateTimeConverter — that is a JSON converter — so
        // a value without a Z suffix arrives as Kind=Unspecified. ToUniversalTime() would then read it
        // as LOCAL time and shift it by the server's zone (7 h on an Asia/Saigon host, measured in
        // BE-REVIEW-02 F-02). UtcNormalization.ToUtc treats Unspecified as UTC, which is what the
        // Contract says every timestamp on the wire is (D-12). Both bounds are inclusive.
        if (from is not null)
        {
            var lower = UtcNormalization.ToUtc(from.Value);
            query = query.Where(reading => reading.MeasuredAt >= lower);
        }

        if (to is not null)
        {
            var upper = UtcNormalization.ToUtc(to.Value);
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

        // nearest_luminance is still null for every row: pairing reference readings with a series is D-R23 step A, not
        // LUX-BASELINE. The key is still present so CV-12 binds to the final shape today.
        var withLuminance = (await ResponsesAsync(items, cancellationToken))
            .Select(reading => new LuxReadingWithLuminanceResponse(
                reading.LuxId,
                reading.ClientOpId,
                reading.PoleId,
                reading.PhotoId,
                reading.MeasuredAt,
                reading.LuxValue,
                reading.MeterModel,
                reading.DataSource,
                reading.Note,
                NearestLuminance: null,
                reading.Protocol,
                reading.Evaluation))
            .ToList();

        return PagedResult<LuxReadingWithLuminanceResponse>.From(page, total, withLuminance);
    }

    private async Task<LuxReadingResponse> ResponseAsync(LuxReading reading, CancellationToken cancellationToken)
        => (await ResponsesAsync([reading], cancellationToken))[0];

    /// <summary>Each row with its protocol and verdict, read in two batched queries (no N+1 on a page of 200).</summary>
    private async Task<List<LuxReadingResponse>> ResponsesAsync(IReadOnlyList<LuxReading> rows, CancellationToken cancellationToken)
    {
        var ids = rows.Where(reading => reading.HasProtocol).Select(reading => reading.LuxId).ToArray();
        var counts = ids.Length == 0 ? new Dictionary<string, int>() : await dbContext.Set<LuxReadingSample>().AsNoTracking()
            .Where(sample => ids.Contains(sample.LuxId)).GroupBy(sample => sample.LuxId)
            .Select(group => new { group.Key, Count = group.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, cancellationToken);
        var verdicts = await LuxEvaluationReads.ForReadingsAsync(dbContext, ids, cancellationToken);
        return rows.Select(reading => new LuxReadingResponse(
            reading.LuxId,
            reading.ClientOpId,
            reading.PoleId,
            reading.PhotoId,
            reading.MeasuredAt,
            reading.LuxValue,
            reading.MeterModel,
            ContractEnum.ToDbValue(reading.DataSource),
            reading.Note,
            reading.HasProtocol
                ? new LuxProtocolResponse(reading.SensorHeightM!.Value, reading.ModuleId!, reading.ModuleFirmwareVersionId,
                    reading.OpticalFilterTransmittance, reading.WindowSpread!.Value, counts.GetValueOrDefault(reading.LuxId))
                : null,
            verdicts.GetValueOrDefault(reading.LuxId))).ToList();
    }

    private const string ClientOpIndex = "ux_lux_reading_client_op_id";
    private const string PhotoLinkIndex = "ux_lux_reading_photo_id";

    /// <summary>
    /// LUX-PHOTO: the named photo must be live, in scope, of the same pole, and not yet carry a reading. Read through the
    /// query filter, so a photo outside the caller's communes is simply not found (404, as for the pole).
    /// LUX-BASELINE D-10: and not yet decided — under the work order → pole → photo locks the decision takes, re-read after them.
    /// </summary>
    private async Task CheckPhotoAsync(string photoId, Pole pole, CancellationToken cancellationToken)
    {
        var photo = await Photo(photoId, cancellationToken);

        if (photo.PoleId != pole.PoleId)
        {
            throw new LuxMapException(
                ErrorCodes.PhotoPoleMismatch,
                HttpStatusCode.BadRequest,
                "That photo was taken at a different pole than pole_id.",
                new Dictionary<string, object?> { ["photo_id"] = photoId, ["photo_pole_id"] = photo.PoleId });
        }

        if (dbContext.Database.CurrentTransaction is not null)
        {
            await dbContext.Database.ExecuteSqlAsync($"SELECT 1 FROM work_order WHERE work_order_id = {photo.WorkOrderId} FOR UPDATE", cancellationToken);
            await dbContext.Database.ExecuteSqlAsync($"SELECT 1 FROM pole WHERE pole_id = {pole.PoleId} AND commune_id = {pole.CommuneId} FOR UPDATE", cancellationToken);
            await dbContext.Database.ExecuteSqlAsync($"SELECT 1 FROM work_order_pole_photo WHERE photo_id = {photoId} AND commune_id = {pole.CommuneId} FOR UPDATE", cancellationToken);
            photo = await Photo(photoId, cancellationToken);
        }

        if (photo.Decision is not null)
        {
            throw new LuxMapException(
                ErrorCodes.PolePhotoAlreadyDecided,
                HttpStatusCode.Conflict,
                "That photo has already been decided by a Manager; a lux reading can only be linked before the decision.",
                new Dictionary<string, object?> { ["photo_id"] = photoId });
        }

        if (await dbContext.Set<LuxReading>().AnyAsync(reading => reading.PhotoId == photoId, cancellationToken))
        {
            throw PhotoAlreadyLinked(photoId);
        }
    }

    private async Task<PhotoTarget> Photo(string photoId, CancellationToken cancellationToken)
        => await dbContext.Set<WorkOrderPolePhoto>()
            .AsNoTracking()
            .NotWithdrawn()
            .Where(photo => photo.PhotoId == photoId)
            .Select(photo => new PhotoTarget(photo.PoleId, photo.WorkOrderId, photo.Decision))
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new LuxMapException(
                ErrorCodes.PolePhotoNotFound,
                HttpStatusCode.NotFound,
                "That photo does not exist, was withdrawn, or is outside your permitted commune scope.");

    private sealed record PhotoTarget(string PoleId, string WorkOrderId, string? Decision);

    private static LuxMapException PhotoAlreadyLinked(string photoId)
        => new(
            ErrorCodes.PhotoLuxAlreadyLinked,
            HttpStatusCode.Conflict,
            "That photo already has a lux reading. A retry of the same reading must reuse its client_op_id.",
            new Dictionary<string, object?> { ["photo_id"] = photoId });

    private static bool IsUniqueViolation(DbUpdateException exception, string constraint)
        => exception.InnerException is Npgsql.PostgresException { SqlState: "23505" } postgres
           && postgres.ConstraintName == constraint;
}
