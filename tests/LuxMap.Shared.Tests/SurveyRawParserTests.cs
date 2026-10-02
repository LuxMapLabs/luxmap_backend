using System.Text;
using LuxMap.Modules.Survey.Entities;
using LuxMap.Modules.Survey.Ingest;
using LuxMap.Shared.Contracts.Enums;
using LuxMap.Shared.Http;

namespace LuxMap.Shared.Tests;

public class SurveyRawParserTests
{
    private static readonly Guid Boot = Guid.NewGuid();
    private static SurveySweep Sweep() => new() { SweepId = "SWP-test", WorkOrderId = "WO-test", CommuneId = "COM-test",
        CapturedBy = "USR-test", CreateRequestHash = new string('0', 64), BootSessionId = Boot, DataSource = DataSource.Simulated };
    private static byte[] Gps(string lines) => Encoding.UTF8.GetBytes($$"""
        {"kind":"gps_track","schema_version":1,"boot_session_id":"{{Boot}}","time_unit":"ns"}
        {{lines}}
        """);
    private const string Sample = """{"sample_no":0,"phone_elapsed_ns":"9007199254740993","lat":16,"lng":108,"accuracy_m":4,"heading_deg":359.9,"speed_mps":3,"provider":"gps"}""";

    [Fact]
    public void Survey_work_orders_never_link_faults()
    {
        foreach (var status in Enum.GetValues<FaultStatus>())
            Assert.False(LuxMap.Modules.WorkOrders.WorkOrderRules.Eligible(LuxMap.Modules.WorkOrders.Entities.TaskKind.Survey, status));
    }

    [Fact]
    public void Bigint_above_javascript_precision_is_preserved()
    {
        var parsed = SurveyRawParser.Parse(Gps(Sample), SurveyRawKind.GpsTrack, Sweep());
        Assert.Equal(9007199254740993L, Assert.Single(parsed.Gps).PhoneElapsedNs);
        Assert.Equal(4326, parsed.Gps[0].Geom.SRID);
    }

    [Theory]
    [InlineData("\"NaN\"")]
    [InlineData("\"Infinity\"")]
    [InlineData("\"-Infinity\"")]
    [InlineData("1e999")]
    [InlineData("-1")]
    public void Invalid_measurement_reports_line_and_field(string value)
    {
        var e = Assert.Throws<LuxMapException>(() => SurveyRawParser.Parse(Gps(Sample.Replace("\"accuracy_m\":4", "\"accuracy_m\":" + value)), SurveyRawKind.GpsTrack, Sweep()));
        Assert.Equal(400, (int)e.StatusCode);
        Assert.Equal(2, e.Details["line"]);
        Assert.Contains("accuracy_m", e.Details["field"]!.ToString());
    }

    [Fact]
    public void A_broken_final_line_rejects_the_whole_document()
    {
        var e = Assert.Throws<LuxMapException>(() => SurveyRawParser.Parse(Gps(Sample + "\n{"), SurveyRawKind.GpsTrack, Sweep()));
        Assert.Equal(3, e.Details["line"]);
    }

    [Theory]
    [InlineData("9223372036854775808")]
    [InlineData("-1")]
    [InlineData("1.0")]
    [InlineData("١")]
    public void Nanoseconds_must_be_ascii_digits_in_signed_bigint_range(string ns)
        => Assert.Throws<LuxMapException>(() => SurveyRawParser.Parse(Gps(Sample.Replace("9007199254740993", ns)), SurveyRawKind.GpsTrack, Sweep()));

    [Fact]
    public void Lux_preserves_module_clock_and_rejects_nonfinite_value()
    {
        var text = $$"""
            {"kind":"lux_log","schema_version":1,"boot_session_id":"{{Boot}}","module_firmware_version_id":12}
            {"sample_no":0,"module_epoch":1,"seq":7,"module_ms":"9007199254740993","phone_elapsed_ns":"9007199254740994","lux":12.4}
            """;
        var parsed = SurveyRawParser.Parse(Encoding.UTF8.GetBytes(text), SurveyRawKind.LuxLog, Sweep());
        Assert.Equal(9007199254740993L, Assert.Single(parsed.Lux).ModuleMs);
        Assert.Throws<LuxMapException>(() => SurveyRawParser.Parse(Encoding.UTF8.GetBytes(text.Replace("12.4", "\"NaN\"")), SurveyRawKind.LuxLog, Sweep()));
    }

    [Fact]
    public void Duplicate_identical_sample_is_deduplicated_but_conflicting_key_is_rejected()
    {
        Assert.Single(SurveyRawParser.Parse(Gps(Sample + "\n" + Sample), SurveyRawKind.GpsTrack, Sweep()).Gps);
        Assert.Throws<LuxMapException>(() => SurveyRawParser.Parse(Gps(Sample + "\n" + Sample.Replace("\"lat\":16", "\"lat\":17")), SurveyRawKind.GpsTrack, Sweep()));
    }
}
