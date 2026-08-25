namespace LuxMap.Shared.Contracts.Enums;

// Contract v1.1 mục 1 — KHOÁ CỨNG. FE/mobile đã hardcode các giá trị này.
// Không thêm giá trị, không đổi tên, không đổi sang int.
// Chuỗi trên dây do LuxMapJsonOptions sinh (SnakeCaseLower) và được khoá lại
// từng giá trị một trong DomainEnumSerializationTests.

/// <summary>fixture_status : normal | dim | out | unknown</summary>
public enum FixtureStatus
{
    Normal,
    Dim,
    Out,

    /// <summary>Sweep gần nhất không phủ được cột. KHÔNG phải lỗi, không gộp vào <see cref="Out"/>.</summary>
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
/// source_channel : cv | iot | field_report — <em>kênh nào phát hiện ra</em>.
/// Giá trị <c>manual</c> của v1.0 đã bị bỏ, không dùng lại.
/// </summary>
public enum SourceChannel
{
    Cv,
    Iot,
    FieldReport,
}

/// <summary>
/// data_source : field | public_imagery | calibration_rig | simulated — <em>dữ liệu đến từ đâu</em>.
/// Chiều khác với <see cref="SourceChannel"/>; một bản ghi mang cả hai cùng lúc.
/// </summary>
public enum DataSource
{
    /// <summary>Giữ cho tương lai — Nhánh C không sinh bản ghi nào mang giá trị này.</summary>
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
