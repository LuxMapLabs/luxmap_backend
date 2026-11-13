using System.Net;
using System.Security.Cryptography;
using System.Text;
using LuxMap.Modules.Assets.Entities;
using LuxMap.Persistence;
using LuxMap.Persistence.Audit;
using LuxMap.Shared.Authorization;
using LuxMap.Shared.Contracts;
using LuxMap.Shared.Contracts.Enums;
using LuxMap.Shared.Contracts.Errors;
using LuxMap.Shared.Contracts.Paging;
using LuxMap.Shared.Http;
using LuxMap.Shared.Serialization;
using LuxMap.Shared.Storage;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LuxMap.Modules.Assets.Photos;

/// <summary>Shared upload/retention rules for the two asset photo tables (D-01…D-06).</summary>
public sealed class AssetPhotoService(LuxMapDbContext db, ICurrentActorAccessor actor,
    IObjectStore store, IOriginalJpegReader images, IAuditTrail audit)
{
    public const long MaxUploadBytes = 16 * 1024 * 1024;
    public const long MaxRequestBytes = 17 * 1024 * 1024;
    public const long MaxPixels = 40_000_000;
    public const int MaxPhotos = 10;

    private static bool IsPole<T>() => typeof(T) == typeof(PoleAssetPhoto);
    private static string ParentField<T>() => IsPole<T>() ? "PoleId" : "CabinetId";
    private static string Collection<T>(string id) => $"/api/v1/assets/{(IsPole<T>() ? "poles" : "cabinets")}/{id}/photos";
    private static LuxMapException Error(string code, HttpStatusCode status) => new(code, status, code.Replace('_', ' '));
    private string ActorId => actor.UserId ?? throw Error("UNAUTHENTICATED", HttpStatusCode.Unauthorized);

    private void RequireWrite(bool hide)
    {
        _ = ActorId;
        var policy = hide ? LuxMapPolicies.ManageAssets : LuxMapPolicies.UploadAssetPhotos;
        if (actor.Role is not { } role || !LuxMapPolicies.Matrix[policy].Contains(role))
            throw Error(ErrorCodes.RoleForbidden, HttpStatusCode.Forbidden);
    }

    private async Task<(string CommuneId, DataSource DataSource)> Target<T>(string id, CancellationToken ct)
    {
        if (IsPole<T>())
        {
            var p = await db.Set<Pole>().AsNoTracking().SingleOrDefaultAsync(x => x.PoleId == id, ct)
                ?? throw Error(ErrorCodes.AssetNotFound, HttpStatusCode.NotFound);
            return (p.CommuneId, p.DataSource);
        }
        var c = await db.Set<ElectricalCabinet>().AsNoTracking().SingleOrDefaultAsync(x => x.CabinetId == id, ct)
            ?? throw Error(ErrorCodes.AssetNotFound, HttpStatusCode.NotFound);
        return (c.CommuneId, c.DataSource);
    }

    private Task Lock<T>(string id, string communeId, CancellationToken ct) => IsPole<T>()
        ? db.Database.ExecuteSqlAsync($"SELECT 1 FROM pole WHERE pole_id = {id} AND commune_id = {communeId} FOR UPDATE", ct)
        : db.Database.ExecuteSqlAsync($"SELECT 1 FROM electrical_cabinet WHERE cabinet_id = {id} AND commune_id = {communeId} FOR UPDATE", ct);

    private IQueryable<T> ForParent<T>(string id) where T : class, IAssetPhoto
        => db.Set<T>().Where(p => EF.Property<string>(p, ParentField<T>()) == id);

    internal static TItem Item<T, TItem>(T row, string id) where T : IAssetPhoto where TItem : AssetPhotoItem
    {
        var path = Collection<T>(id) + "/" + row.PhotoId;
        AssetPhotoItem result = IsPole<T>()
            ? new PoleAssetPhotoItem(row.PhotoId, id, row.CreatedAt, row.UploadedBy, row.ClientOpId, row.Width, row.Height,
                row.ByteCount, row.ThumbnailBytes, path + "/original", path + "/thumbnail")
            : new CabinetAssetPhotoItem(row.PhotoId, id, row.CreatedAt, row.UploadedBy, row.ClientOpId, row.Width, row.Height,
                row.ByteCount, row.ThumbnailBytes, path + "/original", path + "/thumbnail");
        return (TItem)result;
    }

    private async Task<T?> Replay<T>(string id, Guid op, string hash, CancellationToken ct) where T : class, IAssetPhoto
    {
        var userId = ActorId;
        var row = await db.Set<T>().AsNoTracking().SingleOrDefaultAsync(p => p.UploadedBy == userId && p.ClientOpId == op, ct);
        if (row is null) return null;
        if (row.RequestHash != hash) throw Error(ErrorCodes.IdempotencyConflict, HttpStatusCode.Conflict);
        if (row.DeletedAt is not null) throw Error(ErrorCodes.AssetPhotoNotFound, HttpStatusCode.NotFound);
        // Parent identity is part of the request hash. Still read the parent again on every replay.
        await Target<T>(id, ct);
        return row;
    }

    public async Task<(TItem Item, bool Created)> UploadAsync<T, TItem>(string id, IFormCollection form, CancellationToken ct)
        where T : class, IAssetPhoto, new() where TItem : AssetPhotoItem
    {
        RequireWrite(false);
        if (form.ContainsKey("commune_id") || form.ContainsKey("data_source"))
            throw Error(ErrorCodes.ServerOwnedField, HttpStatusCode.BadRequest);
        if (form.Keys.Any(k => k != "client_op_id") || form.Any(k => k.Value.Count != 1)
            || form.Files.Count != 1 || form.Files[0].Name != "photo") throw OptionalJson.Invalid("form");
        if (!Guid.TryParse(form["client_op_id"], out var op) || op == Guid.Empty) throw OptionalJson.Invalid("client_op_id");
        var photo = form.Files[0];
        if (photo.Length == 0) throw OptionalJson.Invalid("photo");
        if (photo.Length > MaxUploadBytes) throw Error("UPLOAD_TOO_LARGE", HttpStatusCode.RequestEntityTooLarge);
        var target = await Target<T>(id, ct);
        await using var input = photo.OpenReadStream();
        var image = await images.ReadAsync(input, MaxUploadBytes, MaxPixels, ct);
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(id + "\n"
            + Convert.ToHexStringLower(SHA256.HashData(image.Original)))));
        if (await Replay<T>(id, op, hash, ct) is { } replay) return (Item<T, TItem>(replay, id), false);
        var spec = IsPole<T>() ? PrefixedIds.PoleAssetPhoto : PrefixedIds.CabinetAssetPhoto;
        // Only registry-controlled SQL, never a request identifier.
        var nextIdSql = $"SELECT {spec.DefaultValueSql} AS \"Value\"";
        var photoId = await db.Database.SqlQueryRaw<string>(nextIdSql).SingleAsync(ct);
        using var original = new MemoryStream(image.Original, false);
        var stored = await store.StoreImageAsync(StorageBucket.Evidence, photoId, original, ct);

        // Storage/decode never hold the parent lock. Uploads and hides take the same parent lock.
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await Lock<T>(id, target.CommuneId, ct);
        var fresh = await Target<T>(id, ct);
        RequireWrite(false);
        if (fresh.CommuneId != target.CommuneId) throw Error(ErrorCodes.AssetNotFound, HttpStatusCode.NotFound);
        if (await Replay<T>(id, op, hash, ct) is { } overlap) return (Item<T, TItem>(overlap, id), false);
        if (await ForParent<T>(id).CountAsync(p => p.DeletedAt == null, ct) >= MaxPhotos)
            throw Error(ErrorCodes.AssetPhotoLimitReached, HttpStatusCode.Conflict);
        var row = new T
        {
            PhotoId = photoId, CommuneId = fresh.CommuneId, DataSource = fresh.DataSource,
            CreatedAt = UtcMicrosecondClock.UtcNow(), UploadedBy = ActorId, ClientOpId = op, RequestHash = hash,
            ObjectKey = stored.OriginalKey, ThumbnailKey = stored.ThumbnailKey, ByteCount = stored.OriginalBytes,
            ThumbnailBytes = stored.ThumbnailBytes, Width = image.Width, Height = image.Height,
        };
        db.Entry(row).Property(ParentField<T>()).CurrentValue = id;
        db.Add(row);
        Record(row, id, AuditAction.Created, null);
        try
        {
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (DbUpdateException e) when (e.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "ux_pole_asset_photo_user_operation" or "ux_cabinet_asset_photo_user_operation" })
        {
            await tx.RollbackAsync(ct);
            db.ChangeTracker.Clear();
            if (await Replay<T>(id, op, hash, ct) is { } winner) return (Item<T, TItem>(winner, id), false);
            // The operation key is global per user/type; its old row may now be outside the caller's scope.
            throw Error(ErrorCodes.IdempotencyConflict, HttpStatusCode.Conflict);
        }
        return (Item<T, TItem>(row, id), true);
    }

    private static IReadOnlyDictionary<string, object?> Snapshot<T>(T row, string id) where T : IAssetPhoto
        => new Dictionary<string, object?>
        {
            ["photo_id"] = row.PhotoId,
            [IsPole<T>() ? "pole_id" : "cabinet_id"] = id,
            ["commune_id"] = row.CommuneId,
            ["data_source"] = row.DataSource,
            ["created_at"] = row.CreatedAt,
            ["uploaded_by"] = row.UploadedBy,
            ["client_op_id"] = row.ClientOpId,
            ["width"] = row.Width,
            ["height"] = row.Height,
            ["byte_count"] = row.ByteCount,
            ["thumbnail_bytes"] = row.ThumbnailBytes,
            ["deleted_at"] = row.DeletedAt,
            ["deleted_by"] = row.DeletedBy,
        };

    private void Record<T>(T row, string id, AuditAction action, object? before) where T : IAssetPhoto
        => audit.Record(new(UtcMicrosecondClock.UtcNow(), AuditActorKind.User, ActorId, actor.Role, row.CommuneId,
            IsPole<T>() ? AuditEntityType.PoleAssetPhoto : AuditEntityType.CabinetAssetPhoto, row.PhotoId, action, before, Snapshot(row, id)));

    public async Task<PagedResult<TItem>> ListAsync<T, TItem>(string id, PageRequest page, CancellationToken ct)
        where T : class, IAssetPhoto where TItem : AssetPhotoItem
    {
        await Target<T>(id, ct);
        var rows = ForParent<T>(id).AsNoTracking().Where(p => p.DeletedAt == null);
        var total = await rows.CountAsync(ct);
        var items = await AssetPhotoReader.Ordered(rows).Skip(page.Skip).Take(page.PageSize).ToListAsync(ct);
        return new(page.Page, page.PageSize, total, items.Select(p => Item<T, TItem>(p, id)).ToArray());
    }

    public async Task<Stream> OpenAsync<T>(string id, string photoId, bool thumbnail, CancellationToken ct) where T : class, IAssetPhoto
    {
        await Target<T>(id, ct);
        var row = await ForParent<T>(id).AsNoTracking().SingleOrDefaultAsync(p => p.PhotoId == photoId && p.DeletedAt == null, ct)
            ?? throw Error(ErrorCodes.AssetPhotoNotFound, HttpStatusCode.NotFound);
        return await store.OpenAsync(StorageBucket.Evidence, thumbnail ? row.ThumbnailKey : row.ObjectKey, ct);
    }

    public async Task HideAsync<T>(string id, string photoId, CancellationToken ct) where T : class, IAssetPhoto
    {
        RequireWrite(true);
        var target = await Target<T>(id, ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await Lock<T>(id, target.CommuneId, ct);
        await Target<T>(id, ct);
        RequireWrite(true);
        var row = await ForParent<T>(id).SingleOrDefaultAsync(p => p.PhotoId == photoId, ct)
            ?? throw Error(ErrorCodes.AssetPhotoNotFound, HttpStatusCode.NotFound);
        if (row.DeletedAt is not null) return;
        var before = Snapshot(row, id);
        row.DeletedAt = UtcMicrosecondClock.UtcNow(); row.DeletedBy = ActorId;
        Record(row, id, AuditAction.DetailsChanged, before);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }
}
