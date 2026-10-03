namespace LuxMap.Modules.Survey.Processing;

/// <summary>Provisional pilot parameters. Every run persists the complete effective options.</summary>
public sealed class SurveyProcessingOptions
{
    public bool Enabled { get; set; }
    public double PollSeconds { get; set; } = 5;
    public double LeaseSeconds { get; set; } = 120;
    public int MaxAttempts { get; set; } = 3;
    public double MaximumKmh { get; set; } = 25;
    public double SpeedWindowSeconds { get; set; } = 3;
    public int ClockMinimumSamples { get; set; } = 8;
    public double ClockMinimumSpanMs { get; set; } = 1000;
    public double ClockReceiptBatchWindowMs { get; set; } = 5;
    public double ClockResidualMs { get; set; } = 80;
    public double ClockMaximumDriftPpm { get; set; } = 2000;
    public double ClockMinimumInlierRatio { get; set; } = .8;
    public double GpsGapSeconds { get; set; } = 2.5;
    public double LuxGapSeconds { get; set; } = .5;
    public double MaximumAccuracyM { get; set; } = 15;
    public double TurnHysteresisM { get; set; } = 8;
    public double PeakWindowSeconds { get; set; } = 2;
    public double PeakMinimumProminenceLux { get; set; } = .5;
    public double PeakNoiseMultiplier { get; set; } = 6;
    public double PeakMinimumWidthSeconds { get; set; } = .2;
    public double PeakMaximumWidthSeconds { get; set; } = 2.5;
    public double SaturationLux { get; set; } = 65000;
    public double AssociationToleranceSeconds { get; set; } = .6;
    public double MaximumGpsOffsetSeconds { get; set; } = 2;
    public int MinimumOffsetAnchors { get; set; } = 3;
    public double OffsetAmbiguitySeconds { get; set; } = .05;
    public double AmbiguousPoleDistanceM { get; set; } = 3;
    public double BboxPaddingDegrees { get; set; } = .001;
    public double RouteAmbiguityM { get; set; } = 2;
    public double RouteExitSeconds { get; set; } = 5;
    public double RouteCorridorM { get; set; } = 25;

    public bool IsValid() => GetType().GetProperties().Where(p => p.PropertyType == typeof(double))
        .All(p => p.GetValue(this) is double v && double.IsFinite(v) && v > 0)
        && MaxAttempts > 0 && ClockMinimumSamples >= 3 && ClockMinimumInlierRatio <= 1
        && MinimumOffsetAnchors >= 2 && AssociationToleranceSeconds < MaximumGpsOffsetSeconds && PeakMinimumWidthSeconds < PeakMaximumWidthSeconds;
}
