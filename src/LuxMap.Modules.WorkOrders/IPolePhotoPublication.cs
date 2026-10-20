using LuxMap.Shared.Contracts.Enums;

namespace LuxMap.Modules.WorkOrders;

public enum PolePhotoLabelSource { Ai, Engineer }

public sealed record PolePhotoPublication(string PhotoId, string WorkOrderId, string PoleId, string CommuneId,
    DataSource DataSource, string EffectiveCvState, PolePhotoLabelSource LabelSource,
    string? AiCvState, double? AiConfidence, string? AiModelVersion, string? AiArtifactVersion,
    string? ReviewedBy, DateTime? ReviewedAt, string? ReviewNote, DateTime CapturedAt, DateTime Now)
{
    public object LabelSnapshot => new
    {
        effective_cv_state = EffectiveCvState, label_source = LabelSource == PolePhotoLabelSource.Engineer ? "engineer" : "ai",
        label_changed = LabelSource == PolePhotoLabelSource.Engineer, ReviewedBy, ReviewedAt, ReviewNote,
        ai_hint = new { cv_state = AiCvState, confidence = AiConfidence, model_version = AiModelVersion, artifact_version = AiArtifactVersion },
    };
}
public sealed record PolePhotoPublicationResult(bool StatusApplied, string StatusReason);

/// <summary>Survey owns publication. Caller holds work-order → pole → photo locks and a transaction.</summary>
public interface IPolePhotoPublication
{
    Task<PolePhotoPublicationResult> PublishAsync(PolePhotoPublication input, CancellationToken ct);
}
