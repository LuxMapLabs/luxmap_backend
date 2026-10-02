using LuxMap.Persistence.Audit;
using LuxMap.Shared.Authorization;
using LuxMap.Shared.Contracts.Enums;
using NetTopologySuite.Geometries;

namespace LuxMap.Modules.Survey.Entities;

public enum SweepStatus { Uploading, Queued, Processing, AwaitingReview, Accepted, Returned, Failed }
public enum SweepProcessingStatus { NotStarted, Queued, Processing, Succeeded, Failed }
public enum SurveyRawKind { GpsTrack, LuxLog, CaptureConfig }

public sealed class SurveySweep : ICommuneScoped, IAudited
{
    public string SweepId { get; set; } = null!;
    public required string WorkOrderId { get; set; }
    public required string CommuneId { get; set; }
    public required string CapturedBy { get; set; }
    public Guid ClientOpId { get; set; }
    public Guid BootSessionId { get; set; }
    public long ElapsedAnchorNs { get; set; }
    public long StartedElapsedNs { get; set; }
    public long? EndedElapsedNs { get; set; }
    public string? SubmissionRequestHash { get; set; }
    public DateTime UtcAnchor { get; set; }
    public double UtcUncertaintyMs { get; set; }
    public required string CreateRequestHash { get; set; }
    public SweepStatus Status { get; set; }
    public SweepProcessingStatus ProcessingStatus { get; set; }
    public DataSource DataSource { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public uint Version { get; set; }
}

public sealed class SurveyVideoClip
{
    public long ClipId { get; set; }
    public required string SweepId { get; set; }
    public int ClipNo { get; set; }
    public required string ObjectKey { get; set; }
    public required string Sha256 { get; set; }
    public long ByteCount { get; set; }
    public required string ContentType { get; set; }
    public DateTime StoredAt { get; set; }
}

public sealed class SurveyRawFile
{
    public required string SweepId { get; set; }
    public SurveyRawKind Kind { get; set; }
    public required string ObjectKey { get; set; }
    public required string Sha256 { get; set; }
    public long ByteCount { get; set; }
    public int SchemaVersion { get; set; }
}

public sealed class SurveyGpsSample
{
    public required string SweepId { get; set; }
    public int SampleNo { get; set; }
    public long PhoneElapsedNs { get; set; }
    public required Point Geom { get; set; }
    public double AccuracyM { get; set; }
    public double? HeadingDeg { get; set; }
    public double? SpeedMps { get; set; }
    public required string Provider { get; set; }
}

public sealed class SurveyLuxSample
{
    public required string SweepId { get; set; }
    public int SampleNo { get; set; }
    public int ModuleEpoch { get; set; }
    public long Seq { get; set; }
    public long ModuleMs { get; set; }
    public long PhoneElapsedNs { get; set; }
    public double Lux { get; set; }
}
