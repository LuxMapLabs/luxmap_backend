using System.Text.Json;
using LuxMap.Shared.Contracts.Enums;
using LuxMap.Shared.Serialization;

namespace LuxMap.Shared.Tests;

/// <summary>
/// Contract v1.1 mục 1 khoá cứng từng giá trị enum, và mục 5.5 cấm trả enum dạng số.
/// FE/mobile đã hardcode các chuỗi này, nên khoá lại TỪNG giá trị chứ không chỉ kiểm tra "là chuỗi".
/// </summary>
public class DomainEnumSerializationTests
{
    private static readonly JsonSerializerOptions Options = LuxMapJsonOptions.Default;

    private static string Wire<T>(T value) => JsonSerializer.Serialize(value, Options).Trim('"');

    [Theory]
    [InlineData(FixtureStatus.Normal, "normal")]
    [InlineData(FixtureStatus.Dim, "dim")]
    [InlineData(FixtureStatus.Out, "out")]
    [InlineData(FixtureStatus.Unknown, "unknown")]
    public void FixtureStatus_matches_contract(FixtureStatus value, string expected)
        => Assert.Equal(expected, Wire(value));

    [Theory]
    [InlineData(PowerSource.Grid, "grid")]
    [InlineData(PowerSource.Solar, "solar")]
    public void PowerSource_matches_contract(PowerSource value, string expected)
        => Assert.Equal(expected, Wire(value));

    [Theory]
    [InlineData(FixtureType.LedRoadLamp, "led_road_lamp")]
    [InlineData(FixtureType.SolarAllInOne, "solar_all_in_one")]
    public void FixtureType_matches_contract(FixtureType value, string expected)
        => Assert.Equal(expected, Wire(value));

    [Theory]
    [InlineData(FaultType.LampOut, "lamp_out")]
    [InlineData(FaultType.LampDim, "lamp_dim")]
    [InlineData(FaultType.SegmentOutage, "segment_outage")]
    [InlineData(FaultType.NodeOffline, "node_offline")]
    [InlineData(FaultType.RuntimeDecline, "runtime_decline")]
    public void FaultType_matches_contract(FaultType value, string expected)
        => Assert.Equal(expected, Wire(value));

    [Theory]
    [InlineData(FaultStatus.Detected, "detected")]
    [InlineData(FaultStatus.Confirmed, "confirmed")]
    [InlineData(FaultStatus.Rejected, "rejected")]
    [InlineData(FaultStatus.InProgress, "in_progress")]
    [InlineData(FaultStatus.Resolved, "resolved")]
    [InlineData(FaultStatus.Verified, "verified")]
    public void FaultStatus_matches_contract(FaultStatus value, string expected)
        => Assert.Equal(expected, Wire(value));

    [Theory]
    [InlineData(Severity.Low, "low")]
    [InlineData(Severity.Medium, "medium")]
    [InlineData(Severity.High, "high")]
    [InlineData(Severity.Critical, "critical")]
    public void Severity_matches_contract(Severity value, string expected)
        => Assert.Equal(expected, Wire(value));

    [Theory]
    [InlineData(SourceChannel.Cv, "cv")]
    [InlineData(SourceChannel.Iot, "iot")]
    [InlineData(SourceChannel.FieldReport, "field_report")]
    public void SourceChannel_matches_contract(SourceChannel value, string expected)
        => Assert.Equal(expected, Wire(value));

    [Theory]
    [InlineData(DataSource.Field, "field")]
    [InlineData(DataSource.PublicImagery, "public_imagery")]
    [InlineData(DataSource.CalibrationRig, "calibration_rig")]
    [InlineData(DataSource.Simulated, "simulated")]
    public void DataSource_matches_contract(DataSource value, string expected)
        => Assert.Equal(expected, Wire(value));

    [Theory]
    [InlineData(WorkOrderStatus.Open, "open")]
    [InlineData(WorkOrderStatus.Assigned, "assigned")]
    [InlineData(WorkOrderStatus.InProgress, "in_progress")]
    [InlineData(WorkOrderStatus.Done, "done")]
    [InlineData(WorkOrderStatus.Verified, "verified")]
    [InlineData(WorkOrderStatus.Cancelled, "cancelled")]
    public void WorkOrderStatus_matches_contract(WorkOrderStatus value, string expected)
        => Assert.Equal(expected, Wire(value));

    [Theory]
    [InlineData(NodeRole.SegmentController, "segment_controller")]
    [InlineData(NodeRole.SampledFixture, "sampled_fixture")]
    public void NodeRole_matches_contract(NodeRole value, string expected)
        => Assert.Equal(expected, Wire(value));

    [Theory]
    [InlineData(NodeStatus.Online, "online")]
    [InlineData(NodeStatus.Offline, "offline")]
    [InlineData(NodeStatus.NeverReported, "never_reported")]
    public void NodeStatus_matches_contract(NodeStatus value, string expected)
        => Assert.Equal(expected, Wire(value));

    [Theory]
    [InlineData(RoadClass.InterCommune, "inter_commune")]
    [InlineData(RoadClass.InterVillage, "inter_village")]
    public void RoadClass_matches_contract(RoadClass value, string expected)
        => Assert.Equal(expected, Wire(value));

    [Fact]
    public void Enum_never_serializes_as_number()
    {
        // Mục 5.5: "int enum của .NET sẽ làm hỏng FE".
        var json = JsonSerializer.Serialize(new { fault_status = FaultStatus.InProgress }, Options);
        Assert.Contains("\"in_progress\"", json);
        Assert.DoesNotContain("3", json);
    }

    [Fact]
    public void Every_enum_member_is_covered_by_a_case()
    {
        // Chốt chặn: thêm giá trị enum mới mà quên khoá chuỗi trên dây thì test này đỏ.
        (Type Type, int Expected)[] enums =
        [
            (typeof(FixtureStatus), 4), (typeof(PowerSource), 2), (typeof(FixtureType), 2),
            (typeof(FaultType), 5), (typeof(FaultStatus), 6), (typeof(Severity), 4),
            (typeof(SourceChannel), 3), (typeof(DataSource), 4), (typeof(WorkOrderStatus), 6),
            (typeof(NodeRole), 2), (typeof(NodeStatus), 3), (typeof(RoadClass), 2),
        ];

        foreach (var (type, expected) in enums)
        {
            Assert.Equal(expected, Enum.GetValues(type).Length);
        }
    }

    [Fact]
    public void SourceChannel_no_longer_carries_the_v1_0_manual_value()
    {
        Assert.DoesNotContain("manual", Enum.GetNames<SourceChannel>(), StringComparer.OrdinalIgnoreCase);
        Assert.Equal(SourceChannel.FieldReport, JsonSerializer.Deserialize<SourceChannel>("\"field_report\"", Options));
    }
}
