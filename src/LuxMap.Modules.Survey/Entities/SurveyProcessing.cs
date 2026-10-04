using LuxMap.Shared.Authorization;
using LuxMap.Persistence.Conventions;
using LuxMap.Shared.Contracts.Enums;

namespace LuxMap.Modules.Survey.Entities;

public sealed class ArtifactVersion : IImmutableRecord
{
    public long VersionId { get; set; }
    public required string Component { get; set; }
    public required string Version { get; set; }
    public required string ArtifactHash { get; set; }
    public string Metadata { get; set; } = "{}";
    public DateTime CreatedAt { get; set; }
}

/// <summary>Inserted once at completion. Mutable lease state belongs to SurveySweep (D-09).</summary>
public sealed class SurveyProcessingRun : ICommuneScoped, IImmutableRecord
{
    public long RunId { get; set; }
    public required string SweepId { get; set; }
    public required string CommuneId { get; set; }
    public int Attempt { get; set; }
    public Guid LeaseOwner { get; set; }
    public DateTime LeaseExpiresAt { get; set; }
    public long AlgorithmVersionId { get; set; }
    public long ClockVersionId { get; set; }
    public required string InputHash { get; set; }
    public required string SettingsSnapshot { get; set; }
    public string GisSnapshot { get; set; } = "{}";
    public string ClockFit { get; set; } = "{}";
    public required string ResultState { get; set; }
    public required string Stage { get; set; }
    public string? ErrorCode { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime FinishedAt { get; set; }
    public double? CoveragePct { get; set; }
    public double? DetectionCoveragePct { get; set; }
    public double? DimCoveragePct { get; set; }
    public long? ModelVersionId { get; set; }
    public long? ExtractorVersionId { get; set; }
    public long? ClassificationVersionId { get; set; }
    public string? CoverageReason { get; set; }
}

public sealed class SurveyPass : IImmutableRecord
{
    public long PassId { get; set; }
    public SurveyProcessingRun Run { get; set; } = null!;
    public long RunId { get; set; }
    public required string SegmentId { get; set; }
    public int PassNo { get; set; }
    public required string Direction { get; set; }
    public double FromFraction { get; set; }
    public double ToFraction { get; set; }
    public long StartElapsedNs { get; set; }
    public long EndElapsedNs { get; set; }
    public required string QualityFlags { get; set; }
}

public sealed class PoleObservation : ICommuneScoped, IImmutableRecord
{
    public long ObservationId { get; set; }
    public SurveyPass Pass { get; set; } = null!;
    public SurveyProcessingRun Run { get; set; } = null!;
    public long PassId { get; set; }
    public long RunId { get; set; }
    public required string PoleId { get; set; }
    public required string CommuneId { get; set; }
    public DataSource DataSource { get; set; }
    public long ObservedElapsedNs { get; set; }
    public DateTime ObservedAt { get; set; }
    public double ChainageM { get; set; }
    public long? PeakAtElapsedNs { get; set; }
    public double? PeakLux { get; set; }
    public double SpeedMps { get; set; }
    public double AssociationConfidence { get; set; }
    public string? CvState { get; set; }
    public double? CvConfidence { get; set; }
    public string? RepresentativeFrameId { get; set; }
    public FixtureStatus ClassifiedAs { get; set; } = FixtureStatus.Unknown;
    public long? BaselineId { get; set; }
    public double? BaselineValue { get; set; }
    public double? BaselineRatio { get; set; }
    public bool DimEvaluationEligible { get; set; }
    public string ReasonCodes { get; set; } = "[]";
    public required string QualityFlags { get; set; }
}
