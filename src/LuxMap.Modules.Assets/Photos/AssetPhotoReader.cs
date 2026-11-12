using LuxMap.Modules.Assets.Entities;
using LuxMap.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LuxMap.Modules.Assets.Photos;

public sealed class AssetPhotoReader(LuxMapDbContext db)
{
    private static string ParentField<T>() => typeof(T) == typeof(PoleAssetPhoto) ? "PoleId" : "CabinetId";
    internal static IOrderedQueryable<T> Ordered<T>(IQueryable<T> rows) where T : IAssetPhoto
        => rows.OrderBy(p => p.CreatedAt).ThenBy(p => p.PhotoId.Length).ThenBy(p => p.PhotoId);

    public Task<IReadOnlyDictionary<string, IReadOnlyList<PoleAssetPhotoItem>>> PolesAsync(IReadOnlyCollection<string> ids, CancellationToken ct)
        => GalleriesAsync<PoleAssetPhoto, PoleAssetPhotoItem>(ids, ct);

    public Task<IReadOnlyDictionary<string, IReadOnlyList<CabinetAssetPhotoItem>>> CabinetsAsync(IReadOnlyCollection<string> ids, CancellationToken ct)
        => GalleriesAsync<CabinetAssetPhoto, CabinetAssetPhotoItem>(ids, ct);

    public static AssetPhotoCover? Cover<TItem>(IReadOnlyList<TItem> items) where TItem : AssetPhotoItem
        => items.Count == 0 ? null : new(items[0].PhotoId, items[0].ThumbnailUrl);

    private async Task<IReadOnlyDictionary<string, IReadOnlyList<TItem>>> GalleriesAsync<T, TItem>(IReadOnlyCollection<string> ids, CancellationToken ct)
        where T : class, IAssetPhoto where TItem : AssetPhotoItem
    {
        if (ids.Count == 0) return new Dictionary<string, IReadOnlyList<TItem>>();
        var rows = await Ordered(db.Set<T>().AsNoTracking().Where(p => p.DeletedAt == null && ids.Contains(EF.Property<string>(p, ParentField<T>()))))
            .Select(p => new { ParentId = EF.Property<string>(p, ParentField<T>()), Photo = p }).ToListAsync(ct);
        return rows.GroupBy(r => r.ParentId).ToDictionary(g => g.Key,
            g => (IReadOnlyList<TItem>)g.Select(r => AssetPhotoService.Item<T, TItem>(r.Photo, r.ParentId)).ToArray());
    }

}
