namespace LuxMap.Modules.IOT;

public sealed record ElectricalWindowAssessment(
    string Status, int Samples, int RequiredSamples,
    double? VoltageAvg, double? CurrentMaAvg, double? PowerMwAvg)
{
    public bool CurrentEvaluationAvailable { get; init; }
}

internal static class ElectricalWindowEvaluator
{
    internal const int WindowSize = 5;
    internal const double CurrentDropLimit = 5;
    internal const double VoltageLossLimit = 0.1;
    internal const string Version = "electrical-mean5-v1";

    internal static bool ValidCurrentVoltage(double voltage, NodeProfile profile) =>
        double.IsFinite(voltage) && voltage > VoltageLossLimit &&
        Math.Abs(voltage - profile.NominalVoltage) <= profile.NominalVoltage * profile.VoltageTolerancePercent / 100;

    internal static (ElectricalWindowAssessment Window, IReadOnlyList<IotAnomaly> Anomalies)
        Evaluate(IReadOnlyList<IotMeasurement> samples, NodeProfile profile)
    {
        if (samples.Count != WindowSize)
            return (new("insufficient_samples", samples.Count, WindowSize, null, null, null), []);
        var voltage = samples.Average(s => s.Voltage);
        var current = samples.Average(s => s.CurrentMa);
        var power = samples.Average(s => s.PowerMw);
        var anomalies = new List<IotAnomaly>();
        if (Math.Abs(voltage) <= VoltageLossLimit)
            anomalies.Add(new("voltage_loss", voltage, null, null, VoltageLossLimit));
        else
        {
            var voltageDeviation = (voltage - profile.NominalVoltage) / profile.NominalVoltage * 100;
            if (Math.Abs(voltage - profile.NominalVoltage) > profile.NominalVoltage * profile.VoltageTolerancePercent / 100)
                anomalies.Add(new("voltage_deviation", voltage, profile.NominalVoltage,
                    Math.Round(voltageDeviation, 2), profile.VoltageTolerancePercent));
            if (samples.All(s => ValidCurrentVoltage(s.Voltage, profile)) && current < profile.NominalCurrentMa * 0.95)
                anomalies.Add(new("current_drop", current, profile.NominalCurrentMa,
                    Math.Round((current - profile.NominalCurrentMa) / profile.NominalCurrentMa * 100, 2), CurrentDropLimit));
        }
        return (new("evaluated", WindowSize, WindowSize, voltage, current, power) {
            CurrentEvaluationAvailable = samples.All(s => ValidCurrentVoltage(s.Voltage, profile)),
        }, anomalies);
    }
}
