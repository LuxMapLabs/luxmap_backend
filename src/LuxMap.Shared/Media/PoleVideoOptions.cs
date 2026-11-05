namespace LuxMap.Shared.Media;

/// <summary>
/// Provisional pole-video sampling parameters (POLE-VIDEO D-4, section <c>PoleVideo</c>). Numbers are placeholders until real one-lamp
/// clips and a measured inference time exist; every verdict records the complete effective set in its artifact version.
/// </summary>
public sealed class PoleVideoOptions
{
    public const string SectionName = "PoleVideo";

    /// <summary>Target density; a long clip gets at most <see cref="MaximumFrames"/> frames spread evenly instead.</summary>
    public double FramesPerSecond { get; set; } = 1;
    public int MaximumFrames { get; set; } = 12;

    /// <summary>Time for the camera to settle, skipped only when the clip is longer than <see cref="SkipLeadingOnlyAboveSeconds"/>.</summary>
    public double SkipLeadingSeconds { get; set; } = 1;
    public double SkipLeadingOnlyAboveSeconds { get; set; } = 6;

    /// <summary>A clip outside this window is refused. <see cref="DurationGraceSeconds"/> absorbs the one-frame rounding of "30 s".</summary>
    public double MinimumSeconds { get; set; } = 3;
    public double MaximumSeconds { get; set; } = 30;
    public double DurationGraceSeconds { get; set; } = .5;

    /// <summary>
    /// The verdict (D-4): at least this fraction of the sampled frames must say ON or OFF, and at least <see cref="MinimumAgreement"/> of those
    /// must say the same thing, or the clip gives no verdict. Read by the pole-video verdict, with the sampling numbers above, into the
    /// artifact version of every result.
    /// </summary>
    public double MinimumDecidedFraction { get; set; } = .6;
    public double MinimumAgreement { get; set; } = .7;

    /// <summary>Separate from the survey extractor so a request never waits behind a night's processing.</summary>
    public int MaximumConcurrentClips { get; set; } = 1;
    public double SlotWaitSeconds { get; set; } = 30;
    public double TimeoutSeconds { get; set; } = 60;
    public string TempRoot { get; set; } = Path.Combine(Path.GetTempPath(), "luxmap-pole-video");
    public long TemporaryBytesPerClip { get; set; } = 320L * 1024 * 1024;
    public int MaximumFrameBytes { get; set; } = 16 * 1024 * 1024;

    public bool IsValid() => FramesPerSecond is > 0 and <= 30 && double.IsFinite(FramesPerSecond)
        && MaximumFrames is >= 1 and <= 64
        && double.IsFinite(SkipLeadingSeconds) && SkipLeadingSeconds >= 0
        && double.IsFinite(SkipLeadingOnlyAboveSeconds) && SkipLeadingOnlyAboveSeconds > SkipLeadingSeconds
        && double.IsFinite(MinimumSeconds) && MinimumSeconds > 0
        && double.IsFinite(MaximumSeconds) && MaximumSeconds > MinimumSeconds
        && double.IsFinite(DurationGraceSeconds) && DurationGraceSeconds >= 0
        && MinimumDecidedFraction is > 0 and <= 1 && MinimumAgreement is > .5 and <= 1
        && MaximumConcurrentClips is >= 1 and <= 8
        && double.IsFinite(SlotWaitSeconds) && SlotWaitSeconds > 0
        && double.IsFinite(TimeoutSeconds) && TimeoutSeconds > 0
        && !string.IsNullOrWhiteSpace(TempRoot)
        && MaximumFrameBytes > 0 && TemporaryBytesPerClip > MaximumFrameBytes;
}
