using LuxMap.Shared.Contracts.Enums;

namespace LuxMap.Modules.Map.Features;

/// <summary>
/// The flat <c>properties</c> block of one pole feature — Contract section 5.1, fifteen fields.
/// </summary>
/// <remarks>
/// <para>
/// <b>Flat, never nested.</b> The front end assigns this straight onto a MapLibre layer, and
/// MapLibre cannot read a nested object in a data expression. <c>mock-poles.geojson</c> is the
/// reference the Contract points at, and its key order is this one.
/// </para>
/// <para>
/// 🔴 <b>Three fields are NOT emitted and adding them is a Contract violation:</b>
/// <c>data_source</c>, <c>external_ref</c> and <c>feeder_id</c> (section 5.1). <c>data_source</c> is
/// filterable but invisible; that is the point of the default that hides the calibration rig.
/// </para>
/// </remarks>
public sealed record PoleProperties
{
    public required string PoleId { get; init; }

    public required string SegmentId { get; init; }

    /// <summary>
    /// <c>unknown</c> when no status row exists, which is the CORRECT answer rather than a placeholder.
    /// </summary>
    /// <remarks>
    /// Contract section 3.1: <c>unknown</c> means the latest sweep did not cover this pole — exactly
    /// the state of a pole nothing has ever classified. CLAUDE.md forbids folding it into <c>out</c>
    /// in any statistic, and the front end draws it with its own symbol.
    /// <para>
    /// ⚠️ Until BE-15/BE-17 write <c>pole_current_status</c>, EVERY pole reports <c>unknown</c>. The
    /// shape is right and the data is absent; see the drift entry for BE-14.
    /// </para>
    /// </remarks>
    public required FixtureStatus FixtureStatus { get; init; }

    /// <summary><c>null</c> if and ONLY if <see cref="FixtureStatus"/> is <c>unknown</c> (section 5.1).</summary>
    public double? StatusConfidence { get; init; }

    // The installation fields come from the lamp IN SERVICE — unique per pole since BE-REVIEW-02
    // constraint 3, so there is one to read and no aggregation rule to invent. Null on a pole that
    // carries no lamp at all.
    public PowerSource? PowerSource { get; init; }

    public FixtureType? FixtureType { get; init; }

    public int? LampWatt { get; init; }

    public DateOnly? InstallDate { get; init; }

    public DateOnly? WarrantyExpiry { get; init; }

    public required string CommuneId { get; init; }

    public DateTime? LastSeenAt { get; init; }

    public string? LastSweepId { get; init; }

    /// <summary>Counted with <c>FaultStatusSets.Open</c> — never a second hand-written status list.</summary>
    public required int OpenFaultCount { get; init; }

    /// <summary>
    /// ⚠️ Always <c>false</c> until the <c>iot_node</c> table exists.
    /// </summary>
    /// <remarks>
    /// The key is emitted rather than omitted, the precedent BE-42 set with <c>nearest_luminance</c>:
    /// a consumer can bind the final shape now, and the day the table arrives no response shape
    /// changes. <c>false</c> is also literally true of the database today — no pole has a node.
    /// </remarks>
    public required bool HasIotNode { get; init; }

    public required bool NearSensitivePoi { get; init; }
}

/// <summary>The flat <c>properties</c> block of one road segment — Contract section 5.2, seven fields.</summary>
public sealed record SegmentProperties
{
    public required string SegmentId { get; init; }

    public required string SegmentName { get; init; }

    public required RoadClass RoadClass { get; init; }

    /// <summary>
    /// The DECLARED length, never <c>ST_Length</c> of the geometry.
    /// </summary>
    /// <remarks>
    /// BE-10 rule 4. Deriving it would shift the number the front end shows by roughly 73 ppm — the
    /// UTM grid scale factor — for a reason nobody could explain from the screen.
    /// </remarks>
    public required double LengthM { get; init; }

    public required int PoleCount { get; init; }

    /// <summary>⚠️ Always <c>null</c> until the <c>iot_node</c> table exists. See <see cref="PoleProperties.HasIotNode"/>.</summary>
    public string? ControllerNodeId { get; init; }

    /// <summary>
    /// <c>true</c> → the front end highlights the WHOLE road, not one lamp.
    /// </summary>
    /// <remarks>
    /// The output of CV-15's clustering: a segment-level outage is ONE cause, not N lamp faults
    /// (CLAUDE.md). Computed here as an open fault of type <c>segment_outage</c> on this segment.
    /// ⚠️ Nothing writes those yet — CV-15 depends on BE-13 — so it is <c>false</c> everywhere today.
    /// </remarks>
    public required bool HasActiveSegmentFault { get; init; }
}
