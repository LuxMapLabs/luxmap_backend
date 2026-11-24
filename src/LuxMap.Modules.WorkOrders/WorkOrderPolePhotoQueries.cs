using LuxMap.Modules.WorkOrders.Entities;

namespace LuxMap.Modules.WorkOrders;

public static class WorkOrderPolePhotoQueries
{
    /// <summary>The one definition of a live photo; Survey uses it too when a lux reading names a photo (LUX-PHOTO).</summary>
    public static IQueryable<WorkOrderPolePhoto> NotWithdrawn(this IQueryable<WorkOrderPolePhoto> photos)
        => photos.Where(p => p.WithdrawnAt == null);

    internal static IOrderedQueryable<WorkOrderPolePhoto> InCaptureOrder(this IQueryable<WorkOrderPolePhoto> photos)
        => photos.OrderBy(p => p.CapturedAt).ThenBy(p => p.UploadedAt).ThenBy(p => p.PhotoId.Length).ThenBy(p => p.PhotoId);
}
