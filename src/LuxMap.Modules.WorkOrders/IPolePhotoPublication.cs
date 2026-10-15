using LuxMap.Shared.Contracts.Enums;

namespace LuxMap.Modules.WorkOrders;

public sealed record PolePhotoPublication(string PhotoId, string WorkOrderId, string PoleId, string CommuneId,
    DataSource DataSource, string CvState, double Confidence, string ModelVersion, string ArtifactVersion,
    DateTime CapturedAt, DateTime Now);
public sealed record PolePhotoPublicationResult(bool StatusApplied, string StatusReason);

/// <summary>Survey owns publication. Caller holds work-order → pole → photo locks and a transaction.</summary>
public interface IPolePhotoPublication
{
    Task<PolePhotoPublicationResult> PublishAsync(PolePhotoPublication input, CancellationToken ct);
}
