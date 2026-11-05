using LuxMap.Persistence.Audit;
using LuxMap.Shared.Authorization;
using LuxMap.Shared.Contracts.Enums;

namespace LuxMap.Modules.WorkOrders.Entities;

/// <summary>
/// Operational single-pole photo; never survey imagery or repair after evidence. POLE-VIDEO D-1 (option C): a clip of one lamp is
/// the same row — the representative frame is the photo, the clip and its analysis ride in the nullable <c>Video*</c> columns,
/// all-or-none. <c>ByteCount</c> / <c>Width</c> / <c>Height</c> keep describing the FRAME; the clip's size is <c>VideoBytes</c>.
/// </summary>
public sealed class WorkOrderPolePhoto : ICommuneScoped, IAudited
{
    public string PhotoId { get; set; } = null!;
    public required string WorkOrderId { get; set; }
    public required string PoleId { get; set; }
    public required string CommuneId { get; set; }
    public DataSource DataSource { get; set; }
    public required string UploadedBy { get; set; }
    public DateTime UploadedAt { get; set; }
    public DateTime CapturedAt { get; set; }
    public double Lat { get; set; }
    public double Lng { get; set; }
    public Guid ClientOpId { get; set; }
    public required string RequestHash { get; set; }
    public required string ObjectKey { get; set; }
    public required string ThumbnailKey { get; set; }
    public long ByteCount { get; set; }
    public long ThumbnailBytes { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public string? CvState { get; set; }
    public double? Confidence { get; set; }
    public string? ModelVersion { get; set; }
    public string? ArtifactVersion { get; set; }
    public DateTime? AnalyzedAt { get; set; }
    public string? FinalCvState { get; set; }
    public string? ReviewNote { get; set; }
    public string? ReviewedBy { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public string? Decision { get; set; }
    public string? DecidedBy { get; set; }
    public DateTime? DecidedAt { get; set; }
    public string? DecisionNote { get; set; }
    public bool StatusApplied { get; set; }
    public string? StatusReason { get; set; }

    public string? VideoObjectKey { get; set; }
    public string? VideoSha256 { get; set; }
    public long? VideoBytes { get; set; }
    public int? VideoDurationMs { get; set; }
    public int? VideoSampledFrames { get; set; }
    public int? VideoOnFrames { get; set; }
    public int? VideoOffFrames { get; set; }
    /// <summary>Sampled frames the daylight gate set aside — neither ON nor OFF.</summary>
    public int? VideoDaylightFrames { get; set; }
    /// <summary>Spread of the confidences of the frames that voted; all three null when no frame did.</summary>
    public double? VideoConfidenceMin { get; set; }
    public double? VideoConfidenceMedian { get; set; }
    public double? VideoConfidenceMax { get; set; }
    public double? VideoMedianLuma { get; set; }
    /// <summary>HLG / PQ source (D-14): accepted and flagged, never rejected.</summary>
    public bool? VideoHdr { get; set; }
    public long? VideoExtractorVersionId { get; set; }
    /// <summary>Why the clip gave no verdict (for example <c>NO_LAMP</c>); a service-defined code, not an enum here.</summary>
    public string? VideoReasonCode { get; set; }
    /// <summary>D-13: the user who decides must open the clip first; this is the latest such opening.</summary>
    public string? VideoViewedBy { get; set; }
    public DateTime? VideoViewedAt { get; set; }
}
