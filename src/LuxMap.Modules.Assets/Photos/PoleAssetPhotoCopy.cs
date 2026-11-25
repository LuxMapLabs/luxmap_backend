using LuxMap.Modules.Assets.Entities;
using LuxMap.Persistence;
using LuxMap.Persistence.Audit;
using LuxMap.Shared.Contracts;
using LuxMap.Shared.Contracts.Enums;
using LuxMap.Shared.Storage;
using Microsoft.EntityFrameworkCore;

namespace LuxMap.Modules.Assets.Photos;

/// <summary>
/// Adds a photo taken elsewhere — an evidence photo of a verified pole GPS update (POLE-GPS-PHOTO PGP-3, PGP-7) — to a pole's gallery,
/// under the same rules as an upload: the JPEG was read through <see cref="IOriginalJpegReader"/> with the upload's byte and pixel caps,
/// it gets its own object and keys, and the gallery's visible-photo limit holds. The caller is a job, not a request: it owns the
/// transaction and the save, so whatever it records about the copy is written in the same save as the photo and its one audit event.
/// </summary>
public static class PoleAssetPhotoCopy
{
    /// <summary>Photos the pole's gallery shows now — the limit counts only these (APH-1).</summary>
    public static Task<int> VisibleAsync(LuxMapDbContext db, string poleId, CancellationToken ct)
        => db.Set<PoleAssetPhoto>().CountAsync(p => p.PoleId == poleId && p.DeletedAt == null, ct);

    /// <summary>
    /// Writes the object under a newly drawn photo id. No transaction and no lock may be open: storage can take seconds (BE-15 P2a).
    /// A photo that is then not staged leaves an orphaned object for the storage reconciliation, never a row without its object.
    /// </summary>
    public static async Task<(string PhotoId, StoredImage Stored)> StoreAsync(LuxMapDbContext db, IObjectStore store,
        ValidatedJpeg image, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is not null)
            throw new InvalidOperationException("Store the photo before opening the transaction that stages it.");
        // Only registry-controlled SQL, never a request identifier.
        var nextIdSql = $"SELECT {PrefixedIds.PoleAssetPhoto.DefaultValueSql} AS \"Value\"";
        var photoId = await db.Database.SqlQueryRaw<string>(nextIdSql).SingleAsync(ct);
        using var original = new MemoryStream(image.Original, false);
        return (photoId, await store.StoreImageAsync(StorageBucket.Evidence, photoId, original, ct));
    }

    /// <summary>
    /// Inside the caller's transaction: locks the pole as uploads and hides do, counts the visible photos again, and stages the row with
    /// its one <c>pole_asset_photo / created</c> event (actor <c>system</c>). <c>null</c> when the gallery is full — nothing staged.
    /// </summary>
    /// <param name="source">Where the photo came from, added to the audit event's after state.</param>
    public static async Task<PoleAssetPhoto?> StageAsync(LuxMapDbContext db, IAuditTrail audit, string poleId, string photoId,
        StoredImage stored, ValidatedJpeg image, string uploadedBy, Guid clientOpId, DateTime now,
        IReadOnlyDictionary<string, object?> source, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Stage the photo inside the caller's transaction.");
        var pole = await db.Set<Pole>().AsNoTracking().SingleAsync(p => p.PoleId == poleId, ct);
        await db.Database.ExecuteSqlAsync(
            $"SELECT 1 FROM pole WHERE pole_id = {poleId} AND commune_id = {pole.CommuneId} FOR UPDATE", ct);
        if (await VisibleAsync(db, poleId, ct) >= AssetPhotoService.MaxPhotos) return null;

        var row = new PoleAssetPhoto
        {
            PhotoId = photoId, PoleId = poleId, CommuneId = pole.CommuneId, DataSource = pole.DataSource,
            CreatedAt = now, UploadedBy = uploadedBy, ClientOpId = clientOpId,
            RequestHash = AssetPhotoService.RequestHash(poleId, image.Original),
            ObjectKey = stored.OriginalKey, ThumbnailKey = stored.ThumbnailKey, ByteCount = stored.OriginalBytes,
            ThumbnailBytes = stored.ThumbnailBytes, Width = image.Width, Height = image.Height,
        };
        db.Add(row);
        var after = new Dictionary<string, object?>(AssetPhotoService.Snapshot(row, poleId));
        foreach (var (key, value) in source) after[key] = value;
        audit.Record(new(now, AuditActorKind.System, null, null, row.CommuneId, AuditEntityType.PoleAssetPhoto, row.PhotoId,
            AuditAction.Created, null, after));
        return row;
    }
}
