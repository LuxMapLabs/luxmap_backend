using LuxMap.Persistence.Audit;
using LuxMap.Persistence.Conventions;
using LuxMap.Shared.Authorization;
using LuxMap.Shared.Contracts.Enums;

namespace LuxMap.Modules.Survey.Entities;

/// <summary>
/// A pole's own reference brightness for STATIONARY readings (LUX-BASELINE D-7 B): the median of protocol readings from distinct
/// survey nights. Kept apart from <see cref="LuminanceBaseline"/> on purpose — a drive-by lux peak and a reading taken standing
/// under the lamp measure different things and must never share a median.
/// </summary>
public sealed class StationaryBaseline : ICommuneScoped, IImmutableRecord, IAudited
{
    public long BaselineId { get; set; }
    public required string PoleId { get; set; }
    public required string CommuneId { get; set; }
    public string? FixtureId { get; set; }
    public DataSource DataSource { get; set; }

    /// <summary><c>module_id@sensor_height_m</c>; a new module or height starts a new baseline (D-9).</summary>
    public required string ProtocolFingerprint { get; set; }

    public int Version { get; set; }
    public double Value { get; set; }
    public int MemberCount { get; set; }

    /// <summary>Coefficient of variation of the members; above <c>LuxProtocol:MaxBaselineCv</c> no dim is judged (D-11).</summary>
    public double MemberCv { get; set; }

    public long AlgorithmVersionId { get; set; }
    public DateTime CreatedAt { get; set; }
    public required string CreatedBy { get; set; }
}

public sealed class StationaryBaselineMember : IImmutableRecord, IAudited
{
    public StationaryBaseline Baseline { get; set; } = null!;
    public long BaselineId { get; set; }
    public required string LuxId { get; set; }
}

/// <summary>
/// The verdict on one protocol reading, written when a Manager decides the photo it was taken with (D-8). One row per reading,
/// immutable: a later baseline does not rewrite an old verdict.
/// </summary>
public sealed class LuxEvaluation : ICommuneScoped, IImmutableRecord, IAudited
{
    public required string LuxId { get; set; }
    public required string PoleId { get; set; }
    public required string CommuneId { get; set; }
    public required string PhotoId { get; set; }
    public required string WorkOrderId { get; set; }
    public DataSource DataSource { get; set; }

    /// <summary>The lamp in service at evaluation and the protocol fingerprint: a baseline only gathers rows that share both (D-9).</summary>
    public string? FixtureId { get; set; }
    public required string ProtocolFingerprint { get; set; }

    public DateTime MeasuredAt { get; set; }
    public DateTime EvaluatedAt { get; set; }
    public required string EvaluatedBy { get; set; }

    /// <summary>The photo's effective label, <c>on</c> or <c>off</c>.</summary>
    public required string CvState { get; set; }

    public FixtureStatus ClassifiedAs { get; set; }
    public double LuxValue { get; set; }
    public long? BaselineId { get; set; }
    public double? BaselineValue { get; set; }
    public double? BaselineRatio { get; set; }
    public double DimThresholdRatio { get; set; }
    public bool DimEvaluationEligible { get; set; }

    /// <summary>Whether this reading may join a baseline (D-9); stored so the next baseline does not re-derive old rules.</summary>
    public bool BaselineEligible { get; set; }

    /// <summary>D-12: the filter's transmittance, the ratio a correct system should report; null outside a filter test.</summary>
    public double? ExpectedRatio { get; set; }

    public required string ReasonCodes { get; set; }
    public long AlgorithmVersionId { get; set; }
}
