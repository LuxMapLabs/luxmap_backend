using LuxMap.Persistence.Audit;
using LuxMap.Shared.Authorization;
using LuxMap.Shared.Contracts.Enums;

namespace LuxMap.Modules.WorkOrders.Entities;

/// <summary>Operational single-pole photo; never survey imagery or repair after evidence.</summary>
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
    public string? Decision { get; set; }
    public string? DecidedBy { get; set; }
    public DateTime? DecidedAt { get; set; }
    public string? DecisionNote { get; set; }
    public bool StatusApplied { get; set; }
    public string? StatusReason { get; set; }
}
