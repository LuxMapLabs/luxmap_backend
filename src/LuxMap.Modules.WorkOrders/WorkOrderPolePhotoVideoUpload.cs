using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using LuxMap.Modules.WorkOrders.Entities;
using LuxMap.Persistence.Audit;
using LuxMap.Persistence.Versions;
using LuxMap.Shared.AI;
using LuxMap.Shared.Contracts;
using LuxMap.Shared.Http;
using LuxMap.Shared.Media;
using LuxMap.Shared.Serialization;
using LuxMap.Shared.Storage;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LuxMap.Modules.WorkOrders;

// POLE-VIDEO PV-c2: a clip of ONE lamp, taken for a work order. The clip is kept, a few frames are judged by the single-lamp model, and the
// row that results is an ordinary pole photo — the representative frame — so review, decision, publication and the fault it can raise
// are the ones a photo already has. D-3: judged inside the request. D-4: the verdict rules are PoleVideoVerdict.
public sealed partial class WorkOrderPolePhotoService
{
    /// <summary>The largest clip accepted (D-6). The same number is the Caddy guard for this route (deploy/caddy/Caddyfile).</summary>
    public const long MaxClipBytes = 100L * 1024 * 1024;

    /// <summary>A clip older than this was not shot for this work order; one from further ahead than the skew comes from a wrong clock and would shadow every later observation.</summary>
    private static readonly TimeSpan MaxCaptureAge = TimeSpan.FromDays(7), MaxClockSkew = TimeSpan.FromMinutes(5);

    [GeneratedRegex("^[0-9a-f]{64}$")]
    private static partial Regex Sha256Hex();

    private sealed record FrameEval(ValidatedJpeg Jpeg, SingleLampPhotoHint? Hint);

    /// <summary>
    /// Takes the clip from <paramref name="body"/> (declared length <paramref name="length"/>, SHA-256 <paramref name="sha256"/>), samples and judges
    /// frames, and records one photo row with the clip's columns. <c>Created</c> is false on a replay: the same operation and the same request
    /// again returns what was stored, without reading the body, judging or writing anything.
    /// </summary>
    public async Task<(PolePhotoItem Item, bool Created)> UploadVideoAsync(string id, string? poleId, string? capturedAt, string? lat, string? lng,
        string? clientOpId, string? sha256, Stream body, long? length, CancellationToken ct)
    {
        if (!Guid.TryParse(clientOpId, out var op) || op == Guid.Empty) throw OptionalJson.Invalid("client_op_id");
        if (sha256 is null || !Sha256Hex().IsMatch(sha256)) throw OptionalJson.Invalid("X-Content-SHA256");
        if (length is null or <= 0) throw OptionalJson.Invalid("Content-Length");
        if (length > MaxClipBytes) throw Error("UPLOAD_TOO_LARGE", HttpStatusCode.RequestEntityTooLarge);
        if (string.IsNullOrWhiteSpace(poleId)) throw OptionalJson.Invalid("pole_id");
        var captured = ParseCapturedAt(capturedAt);
        var latitude = ParseCoordinate(lat, "lat", 90); var longitude = ParseCoordinate(lng, "lng", 180);
        var actorId = ActorId;
        await Order(id, true, ct);
        var hash = Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
        {
            kind = "video", id, poleId, captured, latitude, longitude, clip = sha256,
        }, LuxMapJsonOptions.Default)));
        Task<PolePhotoItem?> Replay() => ReplayAsync(id, actorId, op, hash, ct);
        if (await Replay() is { } replay) return (replay, false);
        // The capture window is for a NEW clip only: a retry of a stored one is answered however old its capture has become (PV-6).
        var now = UtcMicrosecondClock.UtcNow(clock);
        if (captured < now - MaxCaptureAge || captured > now + MaxClockSkew) throw OptionalJson.Invalid("captured_at");
        // Refuse before taking a 100 MB body from a phone on mobile data, not after.
        Writable(await Order(id, true, ct));
        var target = await Target(id, poleId, ct);

        var options = videoOptions.Value;
        // Trusted registry SQL only; never constructed from request input.
        var nextIdSql = $"SELECT {PrefixedIds.WorkOrderPolePhoto.DefaultValueSql} AS \"Value\"";
        var photoId = await db.Database.SqlQueryRaw<string>(nextIdSql).SingleAsync(ct);
        // Keyed by work order, operation and hash: a retry of the same clip writes the same object, never a new orphan.
        var key = $"pole-videos/{id}/{op:N}/{sha256}.mp4";
        await store.StoreStreamAsync(StorageBucket.Video, key, body, new(length.Value, sha256, MaxClipBytes, "video/mp4", RequireMp4: true), ct);

        // Frames are cut, checked as JPEGs and judged one at a time; once the model is unavailable it is not asked again for this clip.
        var frames = new List<FrameEval>();
        var asking = true;
        var info = await sampler.SampleAsync(new(key, length.Value, sha256), async (frame, token) =>
        {
            var jpeg = await images.ReadAsync(new MemoryStream(frame.Jpeg, false), MaxUploadBytes, MaxPixels, token);
            SingleLampPhotoHint? hint = null;
            if (asking)
            {
                using var original = new MemoryStream(jpeg.Original, false);
                using var thumbnail = new MemoryStream(jpeg.Thumbnail, false);
                hint = await hinter.HintAsync(original, thumbnail, token);
                ValidateHint(hint);
                asking = hint is not null;
            }
            frames.Add(new(jpeg, hint));
        }, ct);
        var verdict = PoleVideoVerdict.Decide([.. frames.Select(f => new FrameVote(f.Hint?.CvState, f.Hint?.Confidence, f.Hint?.Reason, f.Jpeg.MeanLuma))], options);
        var shown = frames[verdict.RepresentativeIndex];
        using var original = new MemoryStream(shown.Jpeg.Original, false);
        var stored = await store.StoreImageAsync(StorageBucket.Evidence, photoId, original, ct);
        var model = frames.Select(f => f.Hint).FirstOrDefault(h => h is not null);
        var extractor = await sampler.DescribeAsync(ct);

        // No locks across storage, ffmpeg or inference. Global order: order → pole → photo.
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlAsync($"SELECT 1 FROM work_order WHERE work_order_id = {id} FOR UPDATE", ct);
        await Order(id, true, ct);
        if (await Replay() is { } overlap) return (overlap, false);
        Writable(await Order(id, true, ct));
        await db.Database.ExecuteSqlAsync($"SELECT 1 FROM pole WHERE pole_id = {poleId} AND commune_id = {target.CommuneId} FOR UPDATE", ct);
        var fresh = await Target(id, poleId, ct);
        if (fresh.CommuneId != target.CommuneId || fresh.DataSource != target.DataSource || fresh.SegmentId != target.SegmentId)
            throw Error("POLE_PHOTO_TARGET_CHANGED", HttpStatusCode.Conflict);
        var (extractorId, extractorHash) = await ArtifactRegistry.EnsureAsync(db, ArtifactComponents.FrameExtractor, extractor.Version, extractor.Hash, extractor.Metadata, ct);
        if (extractorHash != extractor.Hash) throw new InvalidOperationException("The frame extractor version names a different artifact.");

        var saved = UtcMicrosecondClock.UtcNow();
        var row = new WorkOrderPolePhoto
        {
            PhotoId = photoId, WorkOrderId = id, PoleId = poleId, CommuneId = fresh.CommuneId, DataSource = fresh.DataSource,
            UploadedBy = actorId, UploadedAt = saved, CapturedAt = captured, Lat = latitude, Lng = longitude,
            ClientOpId = op, RequestHash = hash, ObjectKey = stored.OriginalKey, ThumbnailKey = stored.ThumbnailKey,
            ByteCount = stored.OriginalBytes, ThumbnailBytes = stored.ThumbnailBytes, Width = shown.Jpeg.Width, Height = shown.Jpeg.Height,
            VideoObjectKey = key, VideoSha256 = sha256, VideoBytes = length.Value, VideoDurationMs = info.DurationMs,
            VideoSampledFrames = info.SampledFrames, VideoOnFrames = verdict.OnFrames, VideoOffFrames = verdict.OffFrames,
            VideoDaylightFrames = verdict.DaylightFrames, VideoConfidenceMin = verdict.ConfidenceMin, VideoConfidenceMedian = verdict.ConfidenceMedian,
            VideoConfidenceMax = verdict.ConfidenceMax, VideoMedianLuma = verdict.MedianLuma, VideoHdr = info.Hdr, VideoExtractorVersionId = extractorId,
            VideoReasonCode = verdict.ReasonCode,
        };
        db.Add(row);
        audit.Record(new(saved, AuditActorKind.User, actorId, actor.Role, row.CommuneId, AuditEntityType.WorkOrder, id,
            AuditAction.Submitted, null, new { row.PhotoId, row.PoleId, row.DataSource, row.CapturedAt, row.Lat, row.Lng, row.ClientOpId, row.RequestHash,
                Media = "video", row.VideoSha256, row.VideoBytes, row.VideoDurationMs, row.VideoHdr }));
        try
        {
            await db.SaveChangesAsync(ct);
            if (verdict.State is not null && model is not null)
            {
                row.CvState = verdict.State; row.Confidence = verdict.Confidence; row.ModelVersion = model.ModelVersion;
                row.ArtifactVersion = $"{model.ArtifactVersion}+{PoleVideoVerdict.RulesVersion(options)}"; row.AnalyzedAt = saved;
                audit.Record(new(saved, AuditActorKind.Cv, null, null, row.CommuneId, AuditEntityType.WorkOrder, id,
                    AuditAction.Analyzed, null, new { row.PhotoId, row.PoleId, row.CvState, row.Confidence, row.ModelVersion, row.ArtifactVersion,
                        Media = "video", verdict.ReasonCode }));
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
}
