using LuxMap.Modules.WorkOrders.Entities;

namespace LuxMap.Modules.WorkOrders;

internal static class WorkOrderPolePhotoQueries
{
    internal static IQueryable<WorkOrderPolePhoto> NotWithdrawn(this IQueryable<WorkOrderPolePhoto> photos)
        => photos.Where(p => p.WithdrawnAt == null);

    internal static IOrderedQueryable<WorkOrderPolePhoto> InCaptureOrder(this IQueryable<WorkOrderPolePhoto> photos)
        => photos.OrderBy(p => p.CapturedAt).ThenBy(p => p.UploadedAt).ThenBy(p => p.PhotoId.Length).ThenBy(p => p.PhotoId);
}
