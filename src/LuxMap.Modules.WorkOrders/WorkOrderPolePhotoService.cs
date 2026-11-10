using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using LuxMap.Modules.Assets.Entities;
using LuxMap.Modules.WorkOrders.Entities;
using LuxMap.Persistence;
using LuxMap.Persistence.Audit;
using LuxMap.Shared.AI;
using LuxMap.Shared.Authorization;
using LuxMap.Shared.Contracts;
using LuxMap.Shared.Contracts.Enums;
using LuxMap.Shared.Contracts.Errors;
using LuxMap.Shared.Contracts.Paging;
using LuxMap.Shared.Http;
using LuxMap.Shared.Media;
using LuxMap.Shared.Serialization;
using LuxMap.Shared.Storage;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace LuxMap.Modules.WorkOrders;

public sealed partial class WorkOrderPolePhotoService(LuxMapDbContext db, ICurrentActorAccessor actor,
    IObjectStore store, IOriginalJpegReader images, ISingleLampPhotoHinter hinter, IAuditTrail audit,
    WorkOrderPoleService poles, IPolePhotoPublication publication, IClipFrameSampler sampler, IOptions<PoleVideoOptions> videoOptions)
{
    public const long MaxUploadBytes = 16 * 1024 * 1024;
    public const long MaxRequestBytes = 17 * 1024 * 1024;
    public const long MaxPixels = 40_000_000;
    private string ActorId => actor.UserId ?? throw Error("UNAUTHENTICATED", HttpStatusCode.Unauthorized);
    private static LuxMapException Error(string code, HttpStatusCode status) => new(code, status, code.Replace('_', ' '));
    private async Task<WorkOrder> Order(string id, bool own, CancellationToken ct)
    {
        var order = await db.Set<WorkOrder>().AsNoTracking().SingleOrDefaultAsync(w => w.WorkOrderId == id, ct)
            ?? throw Error("WORK_ORDER_NOT_FOUND", HttpStatusCode.NotFound);
        if (own && order.AssignedTo != ActorId) throw Error("WORK_ORDER_NOT_FOUND", HttpStatusCode.NotFound);
        return order;
    }

    private async Task<Pole> Target(string id, string poleId, CancellationToken ct)
    {
        var pole = await db.Set<Pole>().AsNoTracking().SingleOrDefaultAsync(p => p.PoleId == poleId, ct)
            ?? throw Error("POLE_NOT_FOUND", HttpStatusCode.NotFound);
        if (!await poles.Targets(id).AnyAsync(p => p.PoleId == poleId, ct))
            throw Error("POLE_NOT_IN_WORK_ORDER", HttpStatusCode.Conflict);
        // A confirmed "off" photo becomes a pole-bound fault, which needs the pole's position (POLE-GEOM-NULL GN-3). Photo and clip
        // uploads both pass here.
        if (pole.Geom is null) throw PoleLocationRequired.For(pole.PoleId, PoleLocationRequired.PolePhoto);
        return pole;
    }

    private static void Writable(WorkOrder order)
    {
        if (order.WoStatus != WorkOrderStatus.InProgress)
            throw Error("WORK_ORDER_NOT_IN_PROGRESS", HttpStatusCode.Conflict);
    }

    /// <summary>The photo (or clip) already stored for this user's operation: the same request again is a replay, a different one is a conflict.</summary>
    private async Task<PolePhotoItem?> ReplayAsync(string id, string actorId, Guid op, string hash, CancellationToken ct)
    {
        var row = await db.Set<WorkOrderPolePhoto>().AsNoTracking().SingleOrDefaultAsync(p => p.UploadedBy == actorId && p.ClientOpId == op, ct);
        if (row is null) return null;
        if (row.RequestHash != hash) throw Error("IDEMPOTENCY_CONFLICT", HttpStatusCode.Conflict);
        // Scope on the photo is independent of its parent on inter-commune roads.
        await Order(id, true, ct);
        return Item(row);
    }

    /// <summary>A hint the model port may return: a state, a confidence exactly when it is on or off, versions that say what produced it.</summary>
    private static void ValidateHint(SingleLampPhotoHint? hint)
    {
        if (hint is not null && (hint.CvState is not ("on" or "off" or "inconclusive")
            || (hint.CvState == "inconclusive") != (hint.Confidence is null)
            || hint.Confidence is { } c && (!double.IsFinite(c) || c is < 0 or > 1)
            || string.IsNullOrWhiteSpace(hint.ModelVersion) || string.IsNullOrWhiteSpace(hint.ArtifactVersion)))
            throw new InvalidOperationException("Invalid single-lamp hint.");
    }

    /// <summary>A full ISO 8601 timestamp that names its offset or Z, as UTC to the microsecond.</summary>
    private static DateTime ParseCapturedAt(string? value)
    {
        if (value is null || !Timestamp().IsMatch(value)
            || !DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var captured))
            throw OptionalJson.Invalid("captured_at");
        return new(captured.Ticks / 10 * 10, DateTimeKind.Utc);
    }

    private static double ParseCoordinate(string? value, string name, double limit)
        => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var n)
            && double.IsFinite(n) && Math.Abs(n) <= limit ? (n == 0 ? 0 : n) : throw OptionalJson.Invalid(name);

    public async Task<(PolePhotoItem Item, bool Created)> UploadAsync(string id, string? poleId, IFormFile? photo,
        string? capturedAt, string? lat, string? lng, string? clientOpId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(poleId)) throw OptionalJson.Invalid("pole_id");
        if (photo is null || photo.Length == 0) throw OptionalJson.Invalid("photo");
        if (photo.Length > MaxUploadBytes) throw Error("UPLOAD_TOO_LARGE", HttpStatusCode.RequestEntityTooLarge);
        var captured = ParseCapturedAt(capturedAt);
        var latitude = ParseCoordinate(lat, "lat", 90); var longitude = ParseCoordinate(lng, "lng", 180);
        if (!Guid.TryParse(clientOpId, out var op) || op == Guid.Empty) throw OptionalJson.Invalid("client_op_id");
        var actorId = ActorId;
        await Order(id, true, ct);
        // Bounded reads even on replay; no decode or inference until after the replay check.
        using var buffer = new MemoryStream();
        await using (var input = photo.OpenReadStream())
        {
            var chunk = new byte[81920];
            int read;
            while ((read = await input.ReadAsync(chunk, ct)) != 0)
            {
                if (buffer.Length + read > MaxUploadBytes) throw Error("UPLOAD_TOO_LARGE", HttpStatusCode.RequestEntityTooLarge);
                buffer.Write(chunk, 0, read);
            }
        }
        var hash = Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
        {
            id, poleId, captured, latitude, longitude, sha256 = Convert.ToHexStringLower(SHA256.HashData(buffer.ToArray())),
        }, LuxMapJsonOptions.Default)));
        Task<PolePhotoItem?> Replay() => ReplayAsync(id, actorId, op, hash, ct);
        if (await Replay() is { } replay) return (replay, false);
        Writable(await Order(id, true, ct));
        var target = await Target(id, poleId, ct);
        buffer.Position = 0;
        var image = await images.ReadAsync(buffer, MaxUploadBytes, MaxPixels, ct);
        // Trusted registry SQL only; never constructed from request input.
        var nextIdSql = $"SELECT {PrefixedIds.WorkOrderPolePhoto.DefaultValueSql} AS \"Value\"";
        var photoId = await db.Database.SqlQueryRaw<string>(nextIdSql).SingleAsync(ct);
        using var original = new MemoryStream(image.Original, false);
        var stored = await store.StoreImageAsync(StorageBucket.Evidence, photoId, original, ct);
        original.Position = 0;
        using var thumbnail = new MemoryStream(image.Thumbnail, false);
        var hint = await hinter.HintAsync(original, thumbnail, ct);
        ValidateHint(hint);

        // No locks across storage, image decode or model inference. Global order: order → pole → photo.
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlAsync($"SELECT 1 FROM work_order WHERE work_order_id = {id} FOR UPDATE", ct);
        await Order(id, true, ct);
        if (await Replay() is { } overlap) return (overlap, false);
        Writable(await Order(id, true, ct));
        await db.Database.ExecuteSqlAsync($"SELECT 1 FROM pole WHERE pole_id = {poleId} AND commune_id = {target.CommuneId} FOR UPDATE", ct);
        var fresh = await Target(id, poleId, ct);
        if (fresh.CommuneId != target.CommuneId || fresh.DataSource != target.DataSource || fresh.SegmentId != target.SegmentId)
            throw Error("POLE_PHOTO_TARGET_CHANGED", HttpStatusCode.Conflict);
        var now = UtcMicrosecondClock.UtcNow();
        var row = new WorkOrderPolePhoto
        {
            PhotoId = photoId, WorkOrderId = id, PoleId = poleId, CommuneId = fresh.CommuneId, DataSource = fresh.DataSource,
            UploadedBy = actorId, UploadedAt = now, CapturedAt = captured, Lat = latitude, Lng = longitude,
            ClientOpId = op, RequestHash = hash, ObjectKey = stored.OriginalKey, ThumbnailKey = stored.ThumbnailKey,
            ByteCount = stored.OriginalBytes, ThumbnailBytes = stored.ThumbnailBytes, Width = image.Width, Height = image.Height,
        };
        db.Add(row);
        audit.Record(new(now, AuditActorKind.User, actorId, actor.Role, row.CommuneId, AuditEntityType.WorkOrder, id,
            AuditAction.Submitted, null, new { row.PhotoId, row.PoleId, row.DataSource, row.CapturedAt, row.Lat, row.Lng, row.ClientOpId, row.RequestHash }));
        try
        {
            await db.SaveChangesAsync(ct);
            if (hint is not null)
            {
                row.CvState = hint.CvState; row.Confidence = hint.Confidence; row.ModelVersion = hint.ModelVersion;
                row.ArtifactVersion = hint.ArtifactVersion; row.AnalyzedAt = now;
                audit.Record(new(now, AuditActorKind.Cv, null, null, row.CommuneId, AuditEntityType.WorkOrder, id,
                    AuditAction.Analyzed, null, new { row.PhotoId, row.PoleId, row.CvState, row.Confidence, row.ModelVersion, row.ArtifactVersion }));
                await db.SaveChangesAsync(ct);
            }
            await tx.CommitAsync(ct);
        }
        catch (DbUpdateException e) when (e.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "ux_pole_photo_user_operation" })
        {
            await tx.RollbackAsync(ct); db.ChangeTracker.Clear();
            await Order(id, true, ct);
            if (await Replay() is { } winner) return (winner, false);
            throw Error("IDEMPOTENCY_CONFLICT", HttpStatusCode.Conflict);
        }
        return (Item(row), true);
    }

    public async Task<PagedResult<PolePhotoItem>> ListAsync(string id, string? poleId, PageRequest page, CancellationToken ct)
    {
        await Order(id, false, ct);
        var query = db.Set<WorkOrderPolePhoto>().AsNoTracking().Where(p => p.WorkOrderId == id);
        if (poleId is not null) query = query.Where(p => p.PoleId == poleId);
        var count = await query.CountAsync(ct);
        var rows = await query.OrderBy(p => p.CapturedAt).ThenBy(p => p.UploadedAt).ThenBy(p => p.PhotoId.Length)
            .ThenBy(p => p.PhotoId).Skip(page.Skip).Take(page.PageSize).ToArrayAsync(ct);
        return PagedResult<PolePhotoItem>.From(page, count, rows.Select(Item).ToArray());
    }

    private async Task<WorkOrderPolePhoto> Photo(string id, string photoId, CancellationToken ct)
    {
        await Order(id, false, ct);
        return await db.Set<WorkOrderPolePhoto>().AsNoTracking().SingleOrDefaultAsync(p => p.WorkOrderId == id && p.PhotoId == photoId, ct)
            ?? throw Error("POLE_PHOTO_NOT_FOUND", HttpStatusCode.NotFound);
    }
    public async Task<PolePhotoItem> DetailAsync(string id, string photoId, CancellationToken ct) => Item(await Photo(id, photoId, ct));
    public async Task<Stream> OpenAsync(string id, string photoId, bool thumbnail, CancellationToken ct)
    {
        var row = await Photo(id, photoId, ct);
        return await store.OpenAsync(StorageBucket.Evidence, thumbnail ? row.ThumbnailKey : row.ObjectKey, ct);
    }

    public async Task<PolePhotoItem> ReviewAsync(string id, string photoId, PolePhotoReviewRequest request, CancellationToken ct)
    {
        if (request.FinalCvState is not ("on" or "off")) throw OptionalJson.Invalid("final_cv_state");
        if (request.ReviewNote?.Length > 2000) throw OptionalJson.Invalid("review_note");
        var note = string.IsNullOrWhiteSpace(request.ReviewNote) ? null : request.ReviewNote.Trim();
        await Order(id, true, ct);
        var initial = await Photo(id, photoId, ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlAsync($"SELECT 1 FROM work_order WHERE work_order_id = {id} FOR UPDATE", ct);
        Writable(await Order(id, true, ct));
        await db.Database.ExecuteSqlAsync($"SELECT 1 FROM pole WHERE pole_id = {initial.PoleId} AND commune_id = {initial.CommuneId} FOR UPDATE", ct);
        await db.Database.ExecuteSqlAsync($"SELECT 1 FROM work_order_pole_photo WHERE photo_id = {photoId} AND commune_id = {initial.CommuneId} FOR UPDATE", ct);
        await Photo(id, photoId, ct);
        var row = await db.Set<WorkOrderPolePhoto>().SingleAsync(p => p.WorkOrderId == id && p.PhotoId == photoId, ct);
        await db.Entry(row).ReloadAsync(ct);
        if (row.Decision is not null) throw Error("POLE_PHOTO_ALREADY_DECIDED", HttpStatusCode.Conflict);
        if (request.FinalCvState != row.CvState && note is null) throw OptionalJson.Invalid("review_note");
        if (row.FinalCvState == request.FinalCvState && row.ReviewNote == note && row.ReviewedBy == ActorId)
            return Item(row);
        var before = new { row.PhotoId, row.FinalCvState, row.ReviewNote, row.ReviewedBy, row.ReviewedAt };
        row.FinalCvState = request.FinalCvState; row.ReviewNote = note; row.ReviewedBy = ActorId;
        row.ReviewedAt = UtcMicrosecondClock.UtcNow();
        audit.Record(new(row.ReviewedAt.Value, AuditActorKind.User, ActorId, actor.Role, row.CommuneId,
            AuditEntityType.WorkOrder, id, AuditAction.DetailsChanged, before,
            new { row.PhotoId, row.FinalCvState, row.ReviewNote, row.ReviewedBy, row.ReviewedAt }));
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Item(row);
    }

    public async Task<PolePhotoItem> DecideAsync(string id, string photoId, PolePhotoDecisionRequest request, CancellationToken ct)
    {
        if (request.Decision is not ("confirm" or "reject") || request.Note?.Length > 2000) throw OptionalJson.Invalid("decision");
        var initial = await Photo(id, photoId, ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlAsync($"SELECT 1 FROM work_order WHERE work_order_id = {id} FOR UPDATE", ct);
        await Photo(id, photoId, ct);
        await db.Database.ExecuteSqlAsync($"SELECT 1 FROM pole WHERE pole_id = {initial.PoleId} AND commune_id = {initial.CommuneId} FOR UPDATE", ct);
        await db.Database.ExecuteSqlAsync($"SELECT 1 FROM work_order_pole_photo WHERE photo_id = {photoId} AND commune_id = {initial.CommuneId} FOR UPDATE", ct);
        await Photo(id, photoId, ct);
        var row = await db.Set<WorkOrderPolePhoto>().SingleAsync(p => p.WorkOrderId == id && p.PhotoId == photoId, ct);
        await db.Entry(row).ReloadAsync(ct);
        if (row.Decision is not null) throw Error("POLE_PHOTO_ALREADY_DECIDED", HttpStatusCode.Conflict);
        var effective = row.FinalCvState ?? row.CvState;
        if (request.Decision == "confirm" && effective is not ("on" or "off"))
            throw Error("POLE_PHOTO_INCONCLUSIVE", HttpStatusCode.Conflict);
        // POLE-VIDEO D-13: a Manager confirms a clip only after opening it themselves — an OFF confirmation raises a lamp_out at once, an ON one can clear
        // one. Rejecting needs no viewing. The column keeps the LATEST person to open the clip, so someone who opened it before a colleague did must open it again.
        if (request.Decision == "confirm" && row.VideoObjectKey is not null && row.VideoViewedBy != ActorId)
            throw new LuxMapException("POLE_VIDEO_NOT_VIEWED", HttpStatusCode.Conflict, "Open the clip before confirming it.",
                new Dictionary<string, object?> { ["photo_id"] = photoId, ["video_url"] = $"/api/v1/work-orders/{id}/pole-photos/{photoId}/video" });
        var now = UtcMicrosecondClock.UtcNow();
        var input = new PolePhotoPublication(row.PhotoId, id, row.PoleId, row.CommuneId, row.DataSource,
            effective!, row.FinalCvState is not null && row.FinalCvState != row.CvState ? PolePhotoLabelSource.Engineer : PolePhotoLabelSource.Ai,
            row.CvState, row.Confidence, row.ModelVersion, row.ArtifactVersion, row.ReviewedBy, row.ReviewedAt, row.ReviewNote, row.CapturedAt, now,
            row.VideoObjectKey is null ? null : new(row.VideoHdr!.Value, row.VideoExtractorVersionId!.Value, row.VideoReasonCode));
        var result = request.Decision == "confirm"
            ? await publication.PublishAsync(input, ct)
            : new PolePhotoPublicationResult(false, "rejected");
        row.Decision = request.Decision; row.DecidedBy = ActorId; row.DecidedAt = now;
        row.DecisionNote = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();
        row.StatusApplied = result.StatusApplied; row.StatusReason = result.StatusReason;
        audit.Record(new(now, AuditActorKind.User, ActorId, actor.Role, row.CommuneId, AuditEntityType.WorkOrder, id,
            request.Decision == "confirm" ? AuditAction.Confirmed : AuditAction.Rejected, null,
            new { row.PhotoId, row.PoleId, row.CvState, row.Decision, row.StatusApplied, row.StatusReason, row.ArtifactVersion, provenance = input.LabelSnapshot }, row.DecisionNote));
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Item(row);
    }

    private static PolePhotoItem Item(WorkOrderPolePhoto p)
    {
        var path = $"/api/v1/work-orders/{p.WorkOrderId}/pole-photos/{p.PhotoId}";
        return new(p.PhotoId, p.WorkOrderId, p.PoleId, p.DataSource, p.CapturedAt, p.Lat, p.Lng, p.UploadedBy, p.UploadedAt,
            p.CvState is null ? "unavailable" : "available", p.CvState, p.Confidence, p.ModelVersion, p.ArtifactVersion,
            p.AnalyzedAt, p.FinalCvState, p.ReviewNote, p.ReviewedBy, p.ReviewedAt, p.Decision, p.DecidedBy, p.DecidedAt, p.DecisionNote, p.StatusApplied, p.StatusReason,
            path + "/thumbnail", path + "/original", p.VideoObjectKey is null ? null : new(p.VideoDurationMs!.Value, p.VideoBytes!.Value, p.VideoSha256!,
                p.VideoSampledFrames!.Value, p.VideoOnFrames!.Value, p.VideoOffFrames!.Value, p.VideoDaylightFrames!.Value, p.VideoConfidenceMin,
                p.VideoConfidenceMedian, p.VideoConfidenceMax, p.VideoMedianLuma!.Value, p.VideoHdr!.Value, p.VideoReasonCode,
                p.VideoViewedBy, p.VideoViewedAt, path + "/video"));
    }

    [GeneratedRegex(@"^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}(\.[0-9]{1,7})?(Z|[+-][0-9]{2}:[0-9]{2})$")]
    private static partial Regex Timestamp();
}
