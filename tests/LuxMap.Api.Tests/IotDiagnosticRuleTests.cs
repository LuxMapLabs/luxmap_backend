using LuxMap.Modules.IOT;
using LuxMap.Modules.IOT.Jobs;
using LuxMap.Shared.Contracts.Enums;

namespace LuxMap.Api.Tests;

public sealed class IotDiagnosticRuleTests
{
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

    [Fact]
    public void Lamp_fault_requires_two_non_overlapping_windows_and_same_session()
    {
        var state = new DiagnosticCodeEvidence();
        var now = DateTime.UtcNow;
        state.Observe("a/25", true, 25, now, "lamp_count_shortfall");
        state.Observe("a/26", true, 26, now.AddSeconds(30), "lamp_count_shortfall");
        Assert.False(state.Ready("lamp_count_shortfall", now.AddSeconds(30)));
        state.Observe("a/50", true, 50, now.AddSeconds(60), "lamp_count_shortfall");
        Assert.True(state.Ready("lamp_count_shortfall", now.AddSeconds(60)));
        state.Observe("b/25", true, 25, now.AddSeconds(75), "lamp_count_shortfall");
        Assert.False(state.Ready("lamp_count_shortfall", now.AddSeconds(75)));
    }

    [Theory]
    [InlineData(7, 68.75)]
    [InlineData(8, 80)]
    [InlineData(9, 86.25)]
    [InlineData(10, 96.25)]
    public void Quiet_calibration_reports_missing_lamps(int lit, double current)
    {
        var result = LampCountEstimator.Evaluate(Enumerable.Repeat(current, 25).ToArray(), LampCountEstimator.QuietFiveVolt);
        Assert.Equal("estimated", result.Status);
        Assert.Equal(lit, result.EstimatedLit);
        Assert.Equal(10 - lit, result.EstimatedMissing);
    }

    [Fact]
    public void Quiet_calibration_keeps_overlap_and_unmeasured_counts_undetermined()
    {
        foreach (var current in new[] { 40d, 73.5, 83.5, 91.25 })
            Assert.Equal("undetermined",
                LampCountEstimator.Evaluate(Enumerable.Repeat(current, 25).ToArray(), LampCountEstimator.QuietFiveVolt).Status);
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
