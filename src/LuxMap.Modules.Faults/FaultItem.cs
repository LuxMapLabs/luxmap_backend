using LuxMap.Shared.Contracts.Enums;

namespace LuxMap.Modules.Faults;

/// <summary>
/// One row of <c>GET /faults</c> — the 18 keys of Contract section 5.4, in that order, every key
/// always present (nulls included), matching <c>mocks/mock-faults.json</c>.
/// </summary>
/// <remarks>
/// <c>commune_id</c> is deliberately absent: section 5.4 does not list it and the mock does not carry
/// it. BE-19 and BE-41 answer with this same shape.
/// </remarks>
public sealed record FaultItem
{
    public required string FaultId { get; init; }
    public string? PoleId { get; init; }
    public string? FixtureId { get; init; }
    public string? SegmentId { get; init; }
    public required FaultLocation Location { get; init; }
    public FaultType FaultType { get; init; }
    public FaultStatus FaultStatus { get; init; }
    public Severity Severity { get; init; }
    public SourceChannel SourceChannel { get; init; }
    public DataSource DataSource { get; init; }
    public double? PriorityScore { get; init; }
    public double? StatusConfidence { get; init; }
    public string? ClusterId { get; init; }
    public DateTime DetectedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
    public string? WorkOrderId { get; init; }
    public string? Note { get; init; }
    public string? ReportedBy { get; init; }
}

/// <summary><c>location{lat,lng}</c> — EPSG:4326 degrees.</summary>
public sealed record FaultLocation(double Lat, double Lng);
