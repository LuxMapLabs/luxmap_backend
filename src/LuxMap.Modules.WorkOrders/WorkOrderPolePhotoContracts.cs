using System.ComponentModel.DataAnnotations;
using LuxMap.Shared.Contracts.Enums;

namespace LuxMap.Modules.WorkOrders;

/// <summary>Snapshot stored on upload; GET and replay never run inference.</summary>
public sealed record PolePhotoItem(string PhotoId, string WorkOrderId, string PoleId, DataSource DataSource,
    DateTime CapturedAt, double Lat, double Lng, string UploadedBy, DateTime UploadedAt,
    string AiStatus, string? CvState, double? Confidence, string? ModelVersion, string? ArtifactVersion,
    DateTime? AnalyzedAt, string? FinalCvState, string? ReviewNote, string? ReviewedBy, DateTime? ReviewedAt,
    string? Decision, string? DecidedBy, DateTime? DecidedAt, string? DecisionNote,
    bool StatusApplied, string? StatusReason, string ThumbnailUrl, string OriginalUrl);

// A class, not a positional record: validation attributes on primary-constructor parameters made MVC's first
// concurrent requests throw "No property found that maps to constructor parameter" (500), seen in the full suite.
public sealed class PolePhotoDecisionRequest
{
    [Required] public string Decision { get; init; } = null!;
    [MaxLength(2000)] public string? Note { get; init; }
}

public sealed class PolePhotoReviewRequest
{
    [Required] public string FinalCvState { get; init; } = null!;
    [MaxLength(2000)] public string? ReviewNote { get; init; }
}
