namespace LuxMap.Shared.Contracts;

/// <summary>
/// Quy ước ID của Contract mục 0.1–0.4: <c>&lt;PREFIX&gt;-&lt;số đã pad 0&gt;</c>, sinh bằng
/// sequence PostgreSQL và format ngay ở tầng DB.
/// </summary>
/// <param name="Prefix">Chữ in hoa, xem bảng Contract mục 0.2.</param>
/// <param name="Digits">Số chữ số pad: 4 cho thực thể khối lượng lớn, 3 cho khối lượng nhỏ, 6 cho frame và detection.</param>
/// <param name="SequenceName">Tên sequence trong Postgres, snake_case theo Contract mục 5.1.</param>
public sealed record PrefixedIdSpec(string Prefix, int Digits, string SequenceName)
{
    /// <summary>
    /// SQL cho <c>DEFAULT</c> của cột. Vượt ngưỡng chữ số thì <c>LPAD</c> trả nguyên số dài hơn,
    /// đúng như Contract mục 0.3: cột thứ 10000 là <c>POLE-10000</c>, không cắt bớt, không tràn.
    /// </summary>
    public string DefaultValueSql
        => $"'{Prefix}-' || LPAD(nextval('{SequenceName}')::text, {Digits}, '0')";

    /// <summary>Dựng ID thủ công — chỉ dùng cho test và assertion, đường ghi thật do DB sinh.</summary>
    public string Format(long value) => $"{Prefix}-{value.ToString().PadLeft(Digits, '0')}";
}

/// <summary>
/// Bảng prefix đầy đủ ở Contract mục 0.2. Khai sẵn cả 16 dòng để BE-09 trở đi chỉ việc dùng,
/// không ai phải tự gõ lại prefix hay số chữ số — gõ sai một chỗ là sai toàn bộ entity đó.
/// </summary>
/// <remarks>
/// <c>LuminanceBaseline</c> và <c>TelemetryReading</c> cố ý KHÔNG có mặt: Contract mục 0.2 ghi rõ
/// hai bảng này không có ID hiển thị, khoá theo <c>(pole_id, ...)</c> và <c>(node_id, reading_time)</c>.
/// <c>RefreshToken</c> cũng không có, vì nó không bao giờ lộ ra FE.
/// </remarks>
public static class PrefixedIds
{
    public static readonly PrefixedIdSpec Pole = new("POLE", 4, "pole_id_seq");
    public static readonly PrefixedIdSpec Fault = new("FAULT", 4, "fault_id_seq");
    public static readonly PrefixedIdSpec RoadSegment = new("SEG", 3, "segment_id_seq");
    public static readonly PrefixedIdSpec AdministrativeUnit = new("COM", 3, "commune_id_seq");
    public static readonly PrefixedIdSpec Fixture = new("FIX", 4, "fixture_id_seq");
    public static readonly PrefixedIdSpec Feeder = new("FDR", 3, "feeder_id_seq");
    public static readonly PrefixedIdSpec IotNode = new("NODE", 3, "node_id_seq");
    public static readonly PrefixedIdSpec SurveySweep = new("SWP", 3, "sweep_id_seq");
    public static readonly PrefixedIdSpec SurveyFrame = new("FRM", 6, "frame_id_seq");
    public static readonly PrefixedIdSpec Detection = new("DET", 6, "detection_id_seq");
    public static readonly PrefixedIdSpec LuxReading = new("LUX", 4, "lux_id_seq");
    public static readonly PrefixedIdSpec WorkOrder = new("WO", 4, "work_order_id_seq");
    public static readonly PrefixedIdSpec RepairEvidence = new("EVD", 4, "evidence_id_seq");
    public static readonly PrefixedIdSpec ExternalUnit = new("EXT", 3, "external_unit_id_seq");
    public static readonly PrefixedIdSpec AppUser = new("USR", 3, "user_id_seq");
    public static readonly PrefixedIdSpec FaultCluster = new("CLS", 3, "cluster_id_seq");

    public static IReadOnlyList<PrefixedIdSpec> All { get; } =
    [
        Pole, Fault, RoadSegment, AdministrativeUnit, Fixture, Feeder, IotNode, SurveySweep,
        SurveyFrame, Detection, LuxReading, WorkOrder, RepairEvidence, ExternalUnit, AppUser, FaultCluster,
    ];
}
