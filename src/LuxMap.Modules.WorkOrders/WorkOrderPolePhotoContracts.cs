using System.ComponentModel.DataAnnotations;
using LuxMap.Shared.Contracts.Enums;

namespace LuxMap.Modules.WorkOrders;

/// <summary>Snapshot stored on upload; GET and replay never run inference.</summary>
public sealed record PolePhotoItem(string PhotoId, string WorkOrderId, string PoleId, DataSource DataSource,
    DateTime CapturedAt, double Lat, double Lng, string UploadedBy, DateTime UploadedAt,
    string AiStatus, string? CvState, double? Confidence, string? ModelVersion, string? ArtifactVersion,
    DateTime? AnalyzedAt, string? FinalCvState, string? ReviewNote, string? ReviewedBy, DateTime? ReviewedAt,
    string? Decision, string? DecidedBy, DateTime? DecidedAt, string? DecisionNote,
    bool StatusApplied, string? StatusReason, string ThumbnailUrl, string OriginalUrl, PolePhotoVideoItem? Video = null);

/// <summary>
/// The clip behind a photo taken from a video (POLE-VIDEO); null on a plain photo. <c>ByteCount</c> is the CLIP's size — the photo's own
/// <c>thumbnail_url</c> / <c>original_url</c> are the representative frame. <c>ReasonCode</c> says why a clip gave no verdict
/// (<c>NO_LAMP</c>, <c>MULTIPLE_LAMPS</c>, <c>TOO_BRIGHT</c>, <c>INCONSISTENT</c>). <c>Hdr</c>: an HLG / PQ source, accepted and flagged.
/// <c>ViewedBy</c> / <c>ViewedAt</c>: the latest Manager to open the clip before it was decided.
/// </summary>
public sealed record PolePhotoVideoItem(int DurationMs, long ByteCount, string Sha256, int SampledFrames, int OnFrames, int OffFrames,
    int DaylightFrames, double? ConfidenceMin, double? ConfidenceMedian, double? ConfidenceMax, double MedianLuma, bool Hdr, string? ReasonCode,
    string? ViewedBy, DateTime? ViewedAt, string VideoUrl);

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
