using LuxMap.Modules.IOT;
using LuxMap.Modules.IOT.Jobs;
using LuxMap.Shared.Contracts.Enums;

namespace LuxMap.Api.Tests;

public sealed class IotDiagnosticRuleTests
{
    [Fact]
    public void Stale_electricity_with_fresh_contact_is_not_a_node_offline_fault()
    {
        var now = DateTime.UtcNow;
        var result = new IotDiagnosticResult("NODE-test", "FDR-test", "stale",
            new(now.AddMinutes(-5), "ON", 5, 100, 500), [new("telemetry_stale")])
        {
            ConnectionStatus = "online", ElectricalStatus = "stale",
            LastSeenAt = now, LastCommandAckAt = now, MeasurementKey = "s/1",
        };
        Assert.False(IotDiagnosticRules.IsActive(result, "telemetry_stale"));
        Assert.True(IotDiagnosticRules.TryObservation(result, "telemetry_stale", new(), now, out _));
        var offline = result with { ConnectionStatus = "offline", LastSeenAt = now.AddMinutes(-5) };
        Assert.True(IotDiagnosticRules.IsActive(offline, "telemetry_stale"));
        Assert.True(IotDiagnosticRules.TryObservation(offline, "telemetry_stale", new(), now, out _));
        Assert.False(IotDiagnosticRules.TryObservation(result with { LastSeenAt = now.AddMinutes(1) },
            "telemetry_stale", new(), now, out _));
    }

    [Fact]
    public void Repeated_report_cannot_satisfy_hardware_debounce()
    {
        var state = new DiagnosticCodeEvidence();
        var now = new DateTime(2026, 11, 18, 0, 0, 0, DateTimeKind.Utc);
        Assert.True(state.Observe("session/1", true, null, now, "ina226_unavailable"));
        Assert.False(state.Observe("session/1", true, null, now.AddMinutes(6), "ina226_unavailable"));
        Assert.False(state.Ready("ina226_unavailable", now.AddMinutes(4)));
        Assert.True(state.Observe("session/2", true, null, now.AddMinutes(6), "ina226_unavailable"));
        Assert.False(state.Ready("ina226_unavailable", now.AddMinutes(6)));
    }

    [Theory]
    [InlineData(95, 95, false)]
    [InlineData(95, 94.9, true)]
    [InlineData(86.6, 86.6, true)]
    [InlineData(110, 110, false)]
    public void Mean_two_current_has_a_strict_five_percent_threshold(double first, double second, bool alert)
    {
        var now = DateTime.UtcNow;
        var profile = new NodeProfile { NominalVoltage = 5, NominalCurrentMa = 100, VoltageTolerancePercent = 10 };
        var (window, anomalies) = ElectricalWindowEvaluator.Evaluate(
            [new(now, "ON", 5, first, first * 5), new(now, "ON", 5, second, second * 5)], profile);
        Assert.Equal(2, window.RequiredSamples);
        Assert.Equal((first + second) / 2, window.CurrentMaAvg);
        Assert.Equal(alert, anomalies.Any(a => a.Code == "current_drop"));
        if (alert) Assert.Equal(5, Assert.Single(anomalies).Limit);
    }

    [Fact]
    public void Voltage_uses_two_sample_mean_and_suppresses_current_drop_during_bad_supply()
    {
        var now = DateTime.UtcNow;
        var profile = new NodeProfile { NominalVoltage = 5, NominalCurrentMa = 100, VoltageTolerancePercent = 10 };
        IotMeasurement Sample(double v, double i = 0) => new(now, "ON", v, i, v * i);
        var missing = ElectricalWindowEvaluator.Evaluate([Sample(0)], profile);
        Assert.Equal("insufficient_samples", missing.Window.Status);
        Assert.Empty(missing.Anomalies);
        var loss = ElectricalWindowEvaluator.Evaluate([Sample(0.04), Sample(0.06)], profile);
        Assert.Equal("voltage_loss", Assert.Single(loss.Anomalies).Code);
        var deviation = ElectricalWindowEvaluator.Evaluate([Sample(4), Sample(4.4)], profile);
        Assert.Equal("voltage_deviation", Assert.Single(deviation.Anomalies).Code);
        Assert.Equal(4.2, deviation.Window.VoltageAvg);
        var mixed = ElectricalWindowEvaluator.Evaluate([Sample(4), Sample(6)], profile);
        Assert.Empty(mixed.Anomalies); // Mean 5 V, but neither individual voltage proves a valid load.
    }

    [Theory]
    [InlineData("current_drop")]
    [InlineData("voltage_loss")]
    [InlineData("voltage_deviation")]
    public void Valid_electrical_window_can_raise_a_fault_without_old_debounce(string code)
    {
        var evidence = new DiagnosticCodeEvidence();
        var now = DateTime.UtcNow;
        evidence.Observe("s/2", true, 2, now, code);
        Assert.True(evidence.Ready(code, now));
    }

    [Fact]
    public void Replay_cannot_prove_an_active_electrical_fault_or_recovery()
    {
        var now = DateTime.UtcNow;
        var reading = new IotDiagnosticResult("NODE-1", "FDR-1", "anomaly",
            new(now, "ON", 0, 0, 0), [new("voltage_loss")])
        {
            MeasuredAt = now.AddHours(-1), MeasurementKey = "s/1",
        };
        Assert.False(IotDiagnosticRules.TryObservation(reading, "voltage_loss", new(), now, out _));
        Assert.Equal(FaultType.IotHardwareFault, IotDiagnosticRules.TypeOf("bh1750_unavailable"));
    }
}
