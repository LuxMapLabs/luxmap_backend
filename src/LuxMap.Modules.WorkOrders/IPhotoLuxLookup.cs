namespace LuxMap.Modules.WorkOrders;

/// <summary>
/// The lux reading linked to one pole photo by <c>photo_id</c> (LUX-PHOTO), with its verdict once the photo is decided
/// (LUX-BASELINE D-13): all three verdict fields null before that and on a reference reading.
/// </summary>
public sealed record PhotoLux(string LuxId, double LuxValue, DateTime MeasuredAt,
    string? ClassifiedAs = null, double? BaselineRatio = null, IReadOnlyList<string>? ReasonCodes = null);

/// <summary>
/// Survey owns lux readings and references WorkOrders, so WorkOrders asks through this port (same shape as
/// <see cref="ISurveyFindingLookup"/>). Only readings linked by <c>photo_id</c> count; nothing is paired by time.
/// </summary>
public interface IPhotoLuxLookup
{
    /// <summary>One batched read; a photo with no linked reading is absent from the result.</summary>
    Task<IReadOnlyDictionary<string, PhotoLux>> ForPhotosAsync(IReadOnlyCollection<string> photoIds, CancellationToken ct);
}
