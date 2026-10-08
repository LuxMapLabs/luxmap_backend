namespace LuxMap.Modules.Survey.Processing.Frames;

public sealed class SurveyFrameOptions
{
    /// <summary>
    /// The keyed <see cref="IOnOffDetector"/> the AI module registers (AI-1). Survey does not reference AI: it asks the container
    /// for this key, so choosing <c>yolo</c> without the AI module loaded fails at startup, loudly.
    /// </summary>
    public const string YoloDetectorKey = "yolo";

    public string Detector { get; set; } = "unconfigured";
    public string? FakeManifestPath { get; set; }
    public string FfmpegPath { get; set; } = "ffmpeg";
    public string FfprobePath { get; set; } = "ffprobe";
    public string TempRoot { get; set; } = Path.Combine(Path.GetTempPath(), "luxmap-frames");
    public int MaximumConcurrentClips { get; set; } = 1;
    public long TemporaryBytesPerClip { get; set; } = 400 * 1024 * 1024;
    public int MaximumFrameBytes { get; set; } = 16 * 1024 * 1024;
    public double TimeoutSeconds { get; set; } = 60;
    public double DetectorTimeoutSeconds { get; set; } = 30;
    public double BeforeSeconds { get; set; } = 3;
    public double AfterSeconds { get; set; } = .5;
    public double FramesPerSecond { get; set; } = 5;
    public double MinimumConfidence { get; set; } = .7;
    public double MinimumBoxArea { get; set; } = .0001;
    public double DimThresholdRatio { get; set; } = .8;
    public bool IsValid() => GetType().GetProperties().Where(p => p.PropertyType == typeof(double))
        .All(p => p.GetValue(this) is double v && double.IsFinite(v) && v > 0)
        && MaximumConcurrentClips > 0 && MaximumConcurrentClips <= 8 && TemporaryBytesPerClip > MaximumFrameBytes
        && MaximumFrameBytes > 0 && MinimumConfidence <= 1 && MinimumBoxArea <= 1
        && DimThresholdRatio <= 1 && FramesPerSecond <= 30 && BeforeSeconds + AfterSeconds <= 30
        && Detector is "fake" or "unconfigured" or YoloDetectorKey;
    public void ValidateEnvironment(string environment)
    {
        if (Detector == "fake" && environment != "Development" && environment != "Test")
            throw new InvalidOperationException("The fake survey detector is allowed only in Development/Test.");
    }
}
