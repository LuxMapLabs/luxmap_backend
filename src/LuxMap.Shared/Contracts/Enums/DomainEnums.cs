namespace LuxMap.Shared.Contracts.Enums;

// Contract v1.1 section 1 — FROZEN. Web and mobile already hardcode these values.
// Do not add values, do not rename, do not switch to int.
// The wire strings are produced by LuxMapJsonOptions (SnakeCaseLower) and every single value is
// pinned by DomainEnumSerializationTests.

/// <summary>fixture_status : normal | dim | out | unknown</summary>
public enum FixtureStatus
{
    Normal,
    Dim,
    Out,

    /// <summary>The latest sweep did not cover this pole. NOT a fault — never fold into <see cref="Out"/>.</summary>
    Unknown,
}

/// <summary>power_source : grid | solar</summary>
public enum PowerSource
{
    Grid,
    Solar,
}

/// <summary>fixture_type : led_road_lamp | solar_all_in_one</summary>
public enum FixtureType
{
    LedRoadLamp,
    SolarAllInOne,
}

/// <summary>fault_type : lamp_out | lamp_dim | segment_outage | node_offline | runtime_decline</summary>
public enum FaultType
{
    LampOut,
    LampDim,
    SegmentOutage,
    NodeOffline,
    RuntimeDecline,
}

/// <summary>fault_status : detected | confirmed | rejected | in_progress | resolved | verified</summary>
public enum FaultStatus
{
    Detected,
    Confirmed,
    Rejected,
    InProgress,
    Resolved,
    Verified,
}

/// <summary>severity : low | medium | high | critical</summary>
public enum Severity
{
    Low,
    Medium,
    High,
    Critical,
}

/// <summary>
/// source_channel : cv | iot | field_report — <em>which channel detected it</em>.
/// The v1.0 value <c>manual</c> was dropped and must not come back.
/// </summary>
public enum SourceChannel
{
    Cv,
    Iot,
    FieldReport,
}

/// <summary>
/// data_source : field | public_imagery | calibration_rig | simulated — <em>where the data came
/// from</em>. A different axis from <see cref="SourceChannel"/>; one record carries both at once.
/// </summary>
public enum DataSource
{
    /// <summary>Reserved for later — Branch C produces no records with this value.</summary>
    Field,
    PublicImagery,
    CalibrationRig,
    Simulated,
}

/// <summary>wo_status : open | assigned | in_progress | done | verified | cancelled</summary>
public enum WorkOrderStatus
{
    Open,
    Assigned,
    InProgress,
    Done,
    Verified,
    Cancelled,
}

/// <summary>node_role : segment_controller | sampled_fixture</summary>
public enum NodeRole
{
    SegmentController,
    SampledFixture,
}

/// <summary>node_status : online | offline | never_reported</summary>
public enum NodeStatus
{
    Online,
    Offline,
    NeverReported,
}

/// <summary>road_class : inter_commune | inter_village</summary>
public enum RoadClass
{
    InterCommune,
    InterVillage,
}
