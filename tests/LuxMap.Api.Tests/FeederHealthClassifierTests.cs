using LuxMap.Modules.IOT;
using LuxMap.Shared.Contracts.Enums;

namespace LuxMap.Api.Tests;

public class FeederHealthClassifierTests
{
    [Fact]
    public void An_online_node_with_only_three_samples_is_warning_not_healthy_or_offline()
    {
        var result = Diagnosis() with {
            Status = "insufficient_samples",
            ElectricalWindow = new("insufficient_samples", 3, 4, null, null, null),
        };
        Assert.Equal(FeederHealthState.Warning, FeederHealthClassifier.Classify(result));
    }

    private static IotDiagnosticResult Diagnosis(params IotAnomaly[] anomalies) => new("NODE-test", "FDR-test",
        anomalies.Length == 0 ? "normal" : "anomaly", new(DateTime.UtcNow, "ON", 5, 100, 500), anomalies)
    {
        ConnectionStatus = "online", ElectricalStatus = "fresh",
        ElectricalWindow = new("evaluated", 4, 4, 5, 100, 500),
    };

    [Theory]
    [InlineData("ds3231_unavailable", FeederHealthState.Warning)]
    [InlineData("ds3231_read_failed", FeederHealthState.Warning)]
    [InlineData("ds3231_time_invalid", FeederHealthState.Warning)]
    [InlineData("bh1750_unavailable", FeederHealthState.Warning)]
    [InlineData("bh1750_read_failed", FeederHealthState.Warning)]
    [InlineData("microsd_unavailable", FeederHealthState.Warning)]
    [InlineData("microsd_read_failed", FeederHealthState.Warning)]
    [InlineData("microsd_write_failed", FeederHealthState.Warning)]
    [InlineData("lora_tx_blocked", FeederHealthState.Warning)]
    [InlineData("lora_rx_frame_error", FeederHealthState.Warning)]
    [InlineData("telemetry_stale", FeederHealthState.Warning)]
    [InlineData("ina226_unavailable", FeederHealthState.Incident)]
    [InlineData("ina226_read_failed", FeederHealthState.Incident)]
    [InlineData("ina226_shunt_out_of_range", FeederHealthState.Incident)]
    [InlineData("voltage_loss", FeederHealthState.Incident)]
    public void Codes_have_the_agreed_severity(string code, FeederHealthState expected)
        => Assert.Equal(expected, FeederHealthClassifier.Classify(Diagnosis(new IotAnomaly(code))));

    [Theory]
    [InlineData("current_drop", 94.0, 100.0, FeederHealthState.Warning)]
    [InlineData("current_drop", 80.0001, 100.0, FeederHealthState.Warning)]
    [InlineData("current_drop", 80.0, 100.0, FeederHealthState.Incident)]
    [InlineData("current_drop", 0.0, 100.0, FeederHealthState.Incident)]
    [InlineData("voltage_deviation", 4.4, 5.0, FeederHealthState.Warning)]
    [InlineData("voltage_deviation", 4.000001, 5.0, FeederHealthState.Warning)]
    [InlineData("voltage_deviation", 4.0, 5.0, FeederHealthState.Incident)]
    [InlineData("voltage_deviation", 6.0, 5.0, FeederHealthState.Incident)]
    public void Electrical_incidents_start_at_twenty_percent_using_unrounded_values(string code, double observed,
        double reference, FeederHealthState expected)
        => Assert.Equal(expected, FeederHealthClassifier.Classify(Diagnosis(new IotAnomaly(code, observed, reference))));

    [Fact]
    public void Offline_overrides_incident_and_incident_overrides_warning()
    {
        var diagnosis = Diagnosis(new IotAnomaly("microsd_unavailable"), new IotAnomaly("ina226_unavailable"));
        Assert.Equal(FeederHealthState.Incident, FeederHealthClassifier.Classify(diagnosis));
        Assert.Equal(FeederHealthState.Offline, FeederHealthClassifier.Classify(diagnosis with { ConnectionStatus = "offline" }));
    }

    [Fact]
    public void Fresh_healthy_on_and_intentional_off_are_normal()
    {
        Assert.Equal(FeederHealthState.Normal, FeederHealthClassifier.Classify(Diagnosis()));
        var off = Diagnosis() with { Measurement = new(DateTime.UtcNow, "OFF", 0, 0, 0), ElectricalWindow = null };
        Assert.Equal(FeederHealthState.Normal, FeederHealthClassifier.Classify(off));
    }

    [Fact]
    public void Recent_ack_with_stale_electricity_and_unverified_samples_are_warnings()
    {
        Assert.Equal(FeederHealthState.Warning, FeederHealthClassifier.Classify(Diagnosis(new IotAnomaly("telemetry_stale")) with {
            Status = "stale", ElectricalStatus = "stale", LastCommandAckAt = DateTime.UtcNow }));
        Assert.Equal(FeederHealthState.Warning, FeederHealthClassifier.Classify(Diagnosis() with { ElectricalWindow = null }));
        Assert.Equal(FeederHealthState.Warning, FeederHealthClassifier.Classify(Diagnosis() with { ConnectionStatus = "no_data", Status = "no_data" }));
    }
}
