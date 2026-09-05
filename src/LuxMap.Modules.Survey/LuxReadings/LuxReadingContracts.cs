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
}

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
    DateTime MeasuredAt,
    double LuxValue,
    string? MeterModel,
    string DataSource,
    string? Note);

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
    DateTime MeasuredAt,
    double LuxValue,
    string? MeterModel,
    string DataSource,
    string? Note,
    NearestLuminance? NearestLuminance);

/// <summary>The shape section 2.9 specifies for <c>nearest_luminance</c>. Nothing produces one yet.</summary>
public sealed record NearestLuminance(double BaselineRatio, string ClassifiedAs, DateTime ObservedAt);
