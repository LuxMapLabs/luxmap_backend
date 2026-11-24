namespace LuxMap.Modules.WorkOrders;

/// <summary>The lux reading linked to one pole photo by <c>photo_id</c> (LUX-PHOTO).</summary>
public sealed record PhotoLux(string LuxId, double LuxValue, DateTime MeasuredAt);

/// <summary>
/// Survey owns lux readings and references WorkOrders, so WorkOrders asks through this port (same shape as
/// <see cref="ISurveyFindingLookup"/>). Only readings linked by <c>photo_id</c> count; nothing is paired by time.
/// </summary>
public interface IPhotoLuxLookup
{
    /// <summary>One batched read; a photo with no linked reading is absent from the result.</summary>
    Task<IReadOnlyDictionary<string, PhotoLux>> ForPhotosAsync(IReadOnlyCollection<string> photoIds, CancellationToken ct);
}
