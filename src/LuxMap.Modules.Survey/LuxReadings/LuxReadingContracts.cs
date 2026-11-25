using System.ComponentModel.DataAnnotations;
using LuxMap.Shared.Contracts.Enums;

namespace LuxMap.Modules.Survey.LuxReadings;

/// <summary>
/// Body of <c>POST /api/v1/lux-readings</c> — Contract section 2.9.
/// </summary>
/// <remarks>
/// ⚠️ <c>lux_id</c> and <c>commune_id</c> are declared here ONLY so a client that sends them gets a
/// clear 400 instead of silence. They are never read as values. The server owns both: the id comes
/// from the database sequence, and the commune is read from the pole.
/// <para>
/// <c>measured_by</c> is absent by design — it comes from the JWT, the same shape as
/// <c>reported_by</c> in Contract section 2.8.
/// </para>
/// </remarks>
public sealed record CreateLuxReadingRequest
{
    [Required]
    [MaxLength(64)]
    public string? ClientOpId { get; init; }

    [Required]
    [MaxLength(32)]
    public string? PoleId { get; init; }

    [Required]
    public DateTime? MeasuredAt { get; init; }

    /// <summary>Contract section 2.9: a real number, non-negative. No upper bound.</summary>
    [Required]
    [Range(0, double.MaxValue, ErrorMessage = "lux_value must be zero or greater.")]
    public double? LuxValue { get; init; }

    /// <summary>
    /// Optional <c>PPH-…</c> id of the pole photo or clip this reading was taken with (LUX-PHOTO). Must be a live photo of
    /// the same pole, inside the caller's scope, with no reading linked yet.
    /// </summary>
    [MaxLength(32)]
    public string? PhotoId { get; init; }

    [MaxLength(128)]
    public string? MeterModel { get; init; }

    [Required]
    public DataSource? DataSource { get; init; }

    [MaxLength(1024)]
    public string? Note { get; init; }

    /// <summary>Server-owned. Present only to be rejected — see the remarks on this type.</summary>
    public string? LuxId { get; init; }

    /// <summary>Server-owned. Present only to be rejected — see the remarks on this type.</summary>
    public string? CommuneId { get; init; }

    /// <summary>
    /// LUX-BASELINE D-4, optional: the stationary protocol this reading followed. Absent → a reference reading, stored as before and
    /// never classified (D-3). Present → checked against <c>LuxProtocol</c> and refused with a reason when off (D-5).
    /// </summary>
    public LuxProtocolRequest? Protocol { get; init; }
}

/// <summary>The stationary protocol of one reading (LUX-BASELINE D-4). A class with init, not a positional record (500 trap).</summary>
public sealed class LuxProtocolRequest
{
    /// <summary>The raw window, in the order measured. <c>lux_value</c> must be its median.</summary>
    [Required]
    [MaxLength(10_000)]
    public double[]? Samples { get; init; }

    [Required]
    public double? SensorHeightM { get; init; }

    [Required]
    [MaxLength(64)]
    public string? ModuleId { get; init; }

    public long? ModuleFirmwareVersionId { get; init; }

    /// <summary>D-12: set only during a neutral-density filter test; 0 &lt; x ≤ 1.</summary>
    public double? OpticalFilterTransmittance { get; init; }
}

/// <summary>The protocol as stored; <c>window_spread</c> is the server's, the samples stay server-side.</summary>
public sealed record LuxProtocolResponse(double SensorHeightM, string ModuleId, long? ModuleFirmwareVersionId,
    double? OpticalFilterTransmittance, double WindowSpread, int SampleCount);

/// <summary>
/// The verdict on a protocol reading (LUX-BASELINE D-8, D-13), written when the Manager decides its photo; null until then and on
/// every reference reading. Computed on the backend — clients never derive a ratio themselves.
/// </summary>
public sealed record LuxEvaluationResponse(string ClassifiedAs, double? BaselineRatio, double? BaselineValue, int? BaselineMemberCount,
    double DimThresholdRatio, bool DimEvaluationEligible, double? ExpectedRatio, IReadOnlyList<string> ReasonCodes, DateTime EvaluatedAt);

/// <summary>
/// One lux reading as returned by the API.
/// </summary>
/// <remarks>
/// <c>commune_id</c> and <c>measured_by</c> are deliberately NOT here. The commune is an
/// authorization detail the Contract never publishes for this resource, and <c>measured_by</c>
/// follows <c>reported_by</c>: recorded by the server, not echoed back.
/// </remarks>
public sealed record LuxReadingResponse(
    string LuxId,
    string ClientOpId,
    string PoleId,
    string? PhotoId,
    DateTime MeasuredAt,
    double LuxValue,
    string? MeterModel,
    string DataSource,
    string? Note,
    LuxProtocolResponse? Protocol,
    LuxEvaluationResponse? Evaluation);

/// <summary>
/// A lux reading plus the luminance point nearest to it in TIME — Contract section 2.9, for CV-12.
/// </summary>
/// <remarks>
/// ⚠️ <see cref="NearestLuminance"/> is ALWAYS <c>null</c> today, and that is not the same statement
/// as "no point within ±48 hours": the <c>luminance_history</c> table does not exist yet. It arrives
/// with BE-15/BE-17, which owns wiring this up and removing the drift entry that records it.
/// <para>
/// The key is still emitted — never omitted — so CV-12 can bind against the published shape now and
/// see values appear later without a contract change.
/// </para>
/// </remarks>
public sealed record LuxReadingWithLuminanceResponse(
    string LuxId,
    string ClientOpId,
    string PoleId,
    string? PhotoId,
    DateTime MeasuredAt,
    double LuxValue,
    string? MeterModel,
    string DataSource,
    string? Note,
    NearestLuminance? NearestLuminance,
    LuxProtocolResponse? Protocol,
    LuxEvaluationResponse? Evaluation);

/// <summary>The shape section 2.9 specifies for <c>nearest_luminance</c>. Nothing produces one yet.</summary>
public sealed record NearestLuminance(double BaselineRatio, string ClassifiedAs, DateTime ObservedAt);
