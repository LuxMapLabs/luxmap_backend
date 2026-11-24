using LuxMap.Modules.Survey.Entities;
using LuxMap.Modules.WorkOrders;
using LuxMap.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LuxMap.Modules.Survey.LuxReadings;

/// <summary>Survey's side of <see cref="IPhotoLuxLookup"/>. At most one reading per photo (<c>ux_lux_reading_photo_id</c>).</summary>
public sealed class PhotoLuxLookup(LuxMapDbContext db) : IPhotoLuxLookup
{
    public async Task<IReadOnlyDictionary<string, PhotoLux>> ForPhotosAsync(IReadOnlyCollection<string> photoIds, CancellationToken ct)
    {
        if (photoIds.Count == 0) return new Dictionary<string, PhotoLux>();
        return await db.Set<LuxReading>().AsNoTracking()
            .Where(reading => reading.PhotoId != null && photoIds.Contains(reading.PhotoId))
            .ToDictionaryAsync(reading => reading.PhotoId!, reading => new PhotoLux(reading.LuxId, reading.LuxValue, reading.MeasuredAt), ct);
    }
}
