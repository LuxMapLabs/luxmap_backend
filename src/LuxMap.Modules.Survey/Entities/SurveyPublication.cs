using LuxMap.Persistence.Audit;
using LuxMap.Persistence.Conventions;
using LuxMap.Shared.Authorization;
using LuxMap.Shared.Contracts.Enums;

namespace LuxMap.Modules.Survey.Entities;

public sealed class LuminanceBaseline : ICommuneScoped, IImmutableRecord, IAudited
{
    public long BaselineId { get; set; }
    public required string PoleId { get; set; }
    public required string CommuneId { get; set; }
    public string? FixtureId { get; set; }
    public int Version { get; set; }
    public DataSource DataSource { get; set; }
    public required string Direction { get; set; }
    public double Value { get; set; }
    public int MemberCount { get; set; }
    public long AlgorithmVersionId { get; set; }
    public DateTime CreatedAt { get; set; }
    public required string CreatedBy { get; set; }
}

public sealed class BaselineMember : IImmutableRecord, IAudited
{
    public LuminanceBaseline Baseline { get; set; } = null!;
    public long BaselineId { get; set; }
    public long ObservationId { get; set; }
}

public sealed class LuminanceHistory : ICommuneScoped, IImmutableRecord, IAudited
{
    public required string SweepId { get; set; }
    public required string PoleId { get; set; }
    public required string CommuneId { get; set; }
    public long RunId { get; set; }
    public long? ObservationId { get; set; }
    public long? BaselineId { get; set; }
    public DateTime EvaluatedAt { get; set; }
    public double? PeakLux { get; set; }
    public string? CvState { get; set; }
    public double? BaselineRatio { get; set; }
    public double? StatusConfidence { get; set; }
    public double AssociationConfidence { get; set; }
    public FixtureStatus ClassifiedAs { get; set; }
    public bool DimEvaluationEligible { get; set; }
    public DataSource DataSource { get; set; }
    public DateTime PublishedAt { get; set; }
    public required string PublishedBy { get; set; }
    public required string ReasonCodes { get; set; }
}
