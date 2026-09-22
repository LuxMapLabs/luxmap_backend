using LuxMap.Modules.Map.Bbox;
using LuxMap.Shared.Contracts.Enums;

namespace LuxMap.Modules.Map.Features;

/// <summary>What <c>GET /poles</c> was asked for, after parsing and scope narrowing.</summary>
/// <remarks>
/// A parsed shape rather than the raw query string, so the service never re-reads user input and
/// the controller is the single place that turns text into values and scope into a refusal.
/// </remarks>
public sealed record PoleMapQuery
{
    public required BoundingBox Bbox { get; init; }

    public IReadOnlyList<FixtureStatus>? Statuses { get; init; }

    public PowerSource? PowerSource { get; init; }

    public string? SegmentId { get; init; }

    public IReadOnlyList<string>? CommuneIds { get; init; }

    public bool? HasOpenFault { get; init; }

    /// <summary><c>null</c> means the default: everything EXCEPT <c>calibration_rig</c> (section 1.6).</summary>
    public IReadOnlyList<DataSource>? DataSource { get; init; }
}

/// <summary>What <c>GET /segments</c> was asked for.</summary>
public sealed record SegmentMapQuery
{
    public required BoundingBox Bbox { get; init; }

    public IReadOnlyList<string>? CommuneIds { get; init; }

    public IReadOnlyList<DataSource>? DataSource { get; init; }
}
