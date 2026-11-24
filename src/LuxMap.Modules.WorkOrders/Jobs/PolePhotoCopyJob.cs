using System.Security.Cryptography;
using System.Text;
using LuxMap.Modules.Assets.Photos;
using LuxMap.Modules.WorkOrders.Configurations;
using LuxMap.Modules.WorkOrders.Entities;
using LuxMap.Persistence;
using LuxMap.Persistence.Audit;
using LuxMap.Persistence.Jobs;
using LuxMap.Shared.Contracts.Enums;
using LuxMap.Shared.Contracts.Errors;
using LuxMap.Shared.Http;
using LuxMap.Shared.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace LuxMap.Modules.WorkOrders.Jobs;

/// <summary>
/// POLE-GPS-PHOTO PGP-3, PGP-6, PGP-7: after a <c>pole_gps_update</c> order is verified, its <c>observation</c> photos of the poles whose
/// position was reported become photos of those poles' galleries. Runs after the verify commits, so verify keeps its speed, its errors
/// and its all-or-nothing write of the GIS; the gallery has the photos within one interval.
/// </summary>
/// <remarks>
/// <para>
/// Each evidence photo is handled ONCE: the ledger <c>work_order_photo_copy</c> gets a row whatever happens to it — copied, gallery full,
/// above the pixel cap, object missing or unreadable — so a skipped photo is never met again. Only a failure of the infrastructure itself
/// (store or database down) leaves no row, and the next run tries again.
/// </para>
/// <para>
/// Per pole the newest capture goes first, so a gallery that fills up keeps the latest photos (PGP-6). The object is read, checked and
/// written with no transaction and no lock open; then, in one transaction, the pole is locked as uploads lock it, the visible photos are
/// counted again, and the photo, its audit event and the ledger row are ONE save. Two runs racing on one photo meet at the ledger's
/// primary key: the loser rolls back, leaving at most an orphaned object for the storage reconciliation.
/// </para>
/// </remarks>
public sealed class PolePhotoCopyJob(JobContextFactory contexts, IObjectStore store, IOriginalJpegReader images,
    ILogger<PolePhotoCopyJob> log) : IScheduledJob
{
    public const int Batch = 200;

    /// <summary>The UUIDv5 namespace of copied photos' <c>client_op_id</c> (PGP-7). Never change it: replays would stop matching.</summary>
    public static readonly Guid ClientOpNamespace = new("5f2b8f0e-3c1a-4d7e-9b62-0a4c7d2e9f13");

    public string Name => "pole_photo_copy";

    public TimeSpan Interval => TimeSpan.FromMinutes(1);

    public async Task RunAsync(DateTime now, string correlationId, CancellationToken ct)
    {
        var handled = new Dictionary<PhotoCopyOutcome, int>();
        while (true)
        {
            var due = await CandidatesAsync(ct);
            foreach (var commune in due.GroupBy(c => c.CommuneId))
            {
                await using var db = contexts.Open(commune.Key);
                var audit = JobContextFactory.AuditFor(db, correlationId);
                foreach (var candidate in commune)
                    if (await CopyAsync(db, audit, candidate, now, ct) is { } outcome)
                        handled[outcome] = handled.GetValueOrDefault(outcome) + 1;
            }

            // Every candidate gets its ledger row or the run fails, so a full batch always makes progress.
            if (due.Count < Batch) break;
        }

        if (handled.Count > 0)
            log.LogInformation("Pole photo copy ({Correlation}): {Outcomes}.", correlationId,
                string.Join(", ", handled.Select(x => $"{x.Key} {x.Value}")));
    }

    /// <summary>Photos still to handle, across communes: verified GPS orders, poles with a reported position, no ledger row yet.</summary>
    private async Task<List<Candidate>> CandidatesAsync(CancellationToken ct)
    {
        await using var scan = contexts.Unscoped();
        var query =
            from e in scan.Set<RepairEvidence>().IgnoreQueryFilters()
            join w in scan.Set<WorkOrder>().IgnoreQueryFilters() on e.WorkOrderId equals w.WorkOrderId
            join t in scan.Set<WorkOrderPoleTarget>().IgnoreQueryFilters()
                on new { WorkOrderId = e.WorkOrderId!, PoleId = e.PoleId! } equals new { t.WorkOrderId, t.PoleId }
            where w.TaskKind == TaskKind.PoleGpsUpdate && w.WoStatus == WorkOrderStatus.Verified
                && e.Kind == EvidenceKind.Observation && t.ReportedLat != null
                && !scan.Set<WorkOrderPhotoCopy>().IgnoreQueryFilters().Any(c => c.EvidenceId == e.EvidenceId)
            orderby e.PoleId, e.CapturedAt descending, e.UploadedAt descending, e.EvidenceId.Length descending, e.EvidenceId descending
            select new Candidate(e.EvidenceId, e.WorkOrderId!, e.PoleId!, e.CommuneId, e.ObjectKey, e.UploadedBy);
        return await query.AsNoTracking().Take(Batch).ToListAsync(ct);
    }

    private async Task<PhotoCopyOutcome?> CopyAsync(LuxMapDbContext db, IAuditTrail audit, Candidate c, DateTime now, CancellationToken ct)
    {
        try
        {
            if (await PoleAssetPhotoCopy.VisibleAsync(db, c.PoleId, ct) >= AssetPhotoService.MaxPhotos)
                return await RecordAsync(db, c, PhotoCopyOutcome.SkippedLimit, null, now, null, ct);

            ValidatedJpeg image;
            try
            {
                await using var original = await store.OpenAsync(StorageBucket.Evidence, c.ObjectKey, ct);
                image = await images.ReadAsync(original, AssetPhotoService.MaxUploadBytes, AssetPhotoService.MaxPixels, ct);
            }
            catch (LuxMapException error) when (Unusable(error) is { } outcome)
            {
                log.LogWarning("Evidence {EvidenceId} of {WorkOrderId} not copied to {PoleId}: {Code}.",
                    c.EvidenceId, c.WorkOrderId, c.PoleId, error.Code);
                return await RecordAsync(db, c, outcome, null, now, null, ct);
            }

            var (photoId, stored) = await PoleAssetPhotoCopy.StoreAsync(db, store, image, ct);
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var row = await PoleAssetPhotoCopy.StageAsync(db, audit, c.PoleId, photoId, stored, image, c.UploadedBy,
                ClientOpId(c.EvidenceId), now,
                new Dictionary<string, object?> { ["evidence_id"] = c.EvidenceId, ["work_order_id"] = c.WorkOrderId }, ct);
            var decided = row is null ? PhotoCopyOutcome.SkippedLimit : PhotoCopyOutcome.Copied;
            return await RecordAsync(db, c, decided, row?.PhotoId, now, transaction, ct);
        }
        finally
        {
            db.ChangeTracker.Clear();
        }
    }

    /// <summary>The ledger row, saved — inside <paramref name="transaction"/> when the photo was staged in it.</summary>
    private static async Task<PhotoCopyOutcome?> RecordAsync(LuxMapDbContext db, Candidate c, PhotoCopyOutcome outcome, string? photoId,
        DateTime now, IDbContextTransaction? transaction, CancellationToken ct)
    {
        db.Add(new WorkOrderPhotoCopy
        {
            EvidenceId = c.EvidenceId, WorkOrderId = c.WorkOrderId, PoleId = c.PoleId, CommuneId = c.CommuneId,
            Outcome = outcome, PhotoId = photoId, DecidedAt = now,
        });
        try
        {
            await db.SaveChangesAsync(ct);
            if (transaction is not null) await transaction.CommitAsync(ct);
            return outcome;
        }
        catch (DbUpdateException error) when (error.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: WorkOrderPhotoCopyConfiguration.PrimaryKey })
        {
            // Another run handled this photo first; a failed statement aborts the transaction, so roll back before anything else.
            if (transaction is not null) await transaction.RollbackAsync(ct);
            return null;
        }
    }

    /// <summary>The photo itself is the problem — not the infrastructure — so it is recorded and never tried again.</summary>
    private static PhotoCopyOutcome? Unusable(LuxMapException error) => error.Code switch
    {
        "STORAGE_OBJECT_MISSING" or ErrorCodes.UnsupportedImageFormat or "UPLOAD_TOO_LARGE" => PhotoCopyOutcome.MissingObject,
        // OriginalJpegReader answers the pixel cap with VALIDATION_FAILED: the only check of that code it makes.
        "VALIDATION_FAILED" => PhotoCopyOutcome.SkippedPixels,
        _ => null,
    };

    /// <summary>RFC 4122 version 5 (SHA-1, name-based) UUID of the evidence id under <see cref="ClientOpNamespace"/>.</summary>
    public static Guid ClientOpId(string evidenceId)
    {
        var name = Encoding.UTF8.GetBytes(evidenceId);
        var input = new byte[16 + name.Length];
        ClientOpNamespace.TryWriteBytes(input, bigEndian: true, out _);
        name.CopyTo(input, 16);
        var hash = SHA1.HashData(input);
        hash[6] = (byte)((hash[6] & 0x0F) | 0x50);
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80);
        return new Guid(hash.AsSpan(0, 16), bigEndian: true);
    }

    private sealed record Candidate(string EvidenceId, string WorkOrderId, string PoleId, string CommuneId, string ObjectKey,
        string UploadedBy);
}
