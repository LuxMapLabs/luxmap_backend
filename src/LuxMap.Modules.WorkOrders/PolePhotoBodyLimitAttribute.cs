using LuxMap.Shared.Http;

namespace LuxMap.Modules.WorkOrders;

/// <summary>Retains the work-order photo body cap before form binding, including chunked requests.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class PolePhotoBodyLimitAttribute() : UploadBodyLimitAttribute(WorkOrderPolePhotoService.MaxRequestBytes);
