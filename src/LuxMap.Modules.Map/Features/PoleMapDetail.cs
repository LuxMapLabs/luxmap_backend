using LuxMap.Modules.Assets.Crud;
using LuxMap.Shared.Contracts.Enums;

namespace LuxMap.Modules.Map.Features;

/// <summary>
/// <c>GET /map/poles/{pole_id}</c> — everything the pole screen draws, in ONE request (Contract 5.1,
/// shape of <c>mock-pole-detail.json</c>). BE-20, <b>SELF-SIGNED, provisional until FW</b>: the places
/// where this differs from the Contract are listed in <c>docs/contract-drift.md</c> (BE-20).
/// </summary>
/// <remarks>
/// <c>data_source</c>, <c>external_ref</c> and <c>feeder_id</c> are NOT emitted, same as the map layer.
/// </remarks>
public sealed record PoleMapDetail
{
    public required string PoleId { get; init; }
    public required string SegmentId { get; init; }
    public required string SegmentName { get; init; }
    public required string CommuneId { get; init; }
    public required PoleMapLocation Location { get; init; }

    /// <summary>The lamp in use; <c>null</c> when the pole has no active fixture.</summary>
    public PoleMapFixture? Fixture { get; init; }

    public required PoleMapStatus CurrentStatus { get; init; }

    /// <summary>
    /// Always <c>null</c>: IoT devices sit at the feeder cabinet, never on a pole (drift I-1/I-8).
    /// Kept so the key the front end already reads still exists.
    /// </summary>
    public PoleMapIotNode? IotNode { get; init; }

    /// <summary>
    /// The newest baseline of the pole's current lamp (the one created last of the per-direction
    /// baselines below), or <c>null</c> when none exists yet. A pole can have two baselines — one per
    /// travel direction — so prefer <see cref="LuminanceBaselines"/>.
    /// </summary>
    public PoleMapBaseline? LuminanceBaseline { get; init; }

    /// <summary>ADDITIVE: the newest baseline of the current lamp for each travel direction.</summary>
    public required IReadOnlyList<PoleMapBaseline> LuminanceBaselines { get; init; }

    /// <summary>Newest 30 published points, oldest first. Includes <c>unknown</c> points (pole not observed).</summary>
    public required IReadOnlyList<PoleMapHistoryPoint> LuminanceHistory { get; init; }

    /// <summary>
    /// Always empty: runtime is measured per feeder, not per pole (drift I-9), and no per-pole series
    /// exists. Kept as an array so the key the front end reads is still a list.
    /// </summary>
    public required IReadOnlyList<PoleMapRuntimePoint> RuntimeHistory { get; init; }

    /// <summary>Open faults (<c>FaultStatusSets.Open</c>), most severe first.</summary>
    public required IReadOnlyList<PoleMapOpenFault> OpenFaults { get; init; }

    /// <summary>Representative frames of published passes the caller may open, newest first, at most 10.</summary>
    public required IReadOnlyList<PoleMapFrame> RecentFrames { get; init; }

    /// <summary>The free-text note on this spot (POLE-NOTE), the same text as the inventory's; <c>null</c> when none.</summary>
    public string? Note { get; init; }
}

public sealed record PoleMapLocation(double Lat, double Lng);

public sealed record PoleMapFixture
{
    public FixtureType FixtureType { get; init; }
    public PowerSource PowerSource { get; init; }
    public int LampWatt { get; init; }
    public DateOnly InstallDate { get; init; }

    /// <summary><c>null</c> = warranty expiry unknown, which must read differently from "still covered" (D-R16).</summary>
    public DateOnly? WarrantyExpiry { get; init; }
}

/// <summary>
/// <c>unknown</c> with null timestamps when no sweep has been published for the pole — the CORRECT
/// answer for a pole nothing has classified, not a placeholder (Contract 3.1).
/// </summary>
public sealed record PoleMapStatus
{
    public FixtureStatus FixtureStatus { get; init; }
    public double? StatusConfidence { get; init; }

    /// <summary>When the pole was evaluated (survey time, not review time).</summary>
    public DateTime? DeterminedAt { get; init; }

    /// <summary><c>cv</c> whenever a status row exists: only a published sweep writes it. Null otherwise.</summary>
    public SourceChannel? SourceChannel { get; init; }
}

public sealed record PoleMapBaseline
{
    /// <summary>Median relative lux peak of the members (not a ratio, not absolute lux).</summary>
    public double BaselineValue { get; init; }

    /// <summary>Accepted sweeps behind the value — one member per sweep, normally one per night.</summary>
    public int BaselineWindowNights { get; init; }

    public double DimThresholdRatio { get; init; }

    /// <summary>
    /// <c>null</c>: from Phiếu v1.4 <c>out</c> is decided by the CV ON/OFF call, not by a lux ratio, so
    /// there is no threshold to draw. The key stays so the front end does not break.
    /// </summary>
    public double? OutThresholdRatio { get; init; }

    public DateTime ComputedAt { get; init; }

    /// <summary>ADDITIVE: <c>forward</c> or <c>reverse</c>.</summary>
    public required string Direction { get; init; }
}

public sealed record PoleMapHistoryPoint
{
    public DateTime ObservedAt { get; init; }
    public required string SweepId { get; init; }

    /// <summary>Equal to <see cref="BaselineRatio"/>: the mock's baseline is 1.0, so the two coincide.</summary>
    public double? NormalizedLuminance { get; init; }

    /// <summary>Computed on the backend; <c>null</c> when the pole had no usable baseline or was not observed.</summary>
    public double? BaselineRatio { get; init; }

    public FixtureStatus ClassifiedAs { get; init; }

    /// <summary>ADDITIVE: relative lux peak behind the point, <c>null</c> when not observed.</summary>
    public double? PeakLux { get; init; }

    /// <summary>ADDITIVE: why — <c>not_observed</c>, <c>baseline_missing</c>, … .</summary>
    public required IReadOnlyList<string> ReasonCodes { get; init; }
}

public sealed record PoleMapOpenFault
{
    public required string FaultId { get; init; }

    /// <summary>The EFFECTIVE type: the reviewer's override when there is one.</summary>
    public FaultType FaultType { get; init; }

    public Severity Severity { get; init; }
    public FaultStatus FaultStatus { get; init; }
    public double? PriorityScore { get; init; }
}

public sealed record PoleMapFrame
{
    public required string FrameId { get; init; }
    public required string SweepId { get; init; }
    public DateTime CapturedAt { get; init; }

    /// <summary>Relative path through the API, never presigned.</summary>
    public required string ThumbnailUrl { get; init; }

    /// <summary><c>null</c>: frames are not stored with the distance to the pole.</summary>
    public double? DistanceM { get; init; }

    /// <summary><c>null</c>: frames are not stored with a heading.</summary>
    public double? HeadingDeg { get; init; }
}

/// <summary>Contract 5.1 shape of <c>iot_node</c>. Never populated: no device is attached to a pole.</summary>
public sealed record PoleMapIotNode
{
    public required string NodeId { get; init; }
    public NodeStatus NodeStatus { get; init; }
    public DateTime? LastReportAt { get; init; }
}

/// <summary>Contract 5.1 shape of a <c>runtime_history</c> point. Never populated: runtime is per feeder.</summary>
public sealed record PoleMapRuntimePoint
{
    public DateOnly NightOf { get; init; }
    public double RuntimeHours { get; init; }
    public string? OnAt { get; init; }
    public string? OffAt { get; init; }
    public required string Source { get; init; }
}
