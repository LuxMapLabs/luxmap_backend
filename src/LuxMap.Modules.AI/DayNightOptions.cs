namespace LuxMap.Modules.AI;

/// <summary>Configuration section <c>Ai:DayNight</c> — the day/night classification gate.</summary>
public sealed record DayNightOptions
{
    public const string SectionName = "Ai:DayNight";

    /// <summary>Relative paths resolve against the app's base directory, where the build copies <c>Models/daynight.onnx</c>.</summary>
    public string ModelPath { get; init; } = Path.Combine("Models", "daynight.onnx");

    /// <summary>
    /// SHA-256 of the model file the deployment expects. Set ⇒ a different file stops first load instead of silently changing
    /// every detection (BE-34). The model lives in git for the pilot; a swapped file must be a reviewed change of this value.
    /// </summary>
    public string? ModelSha256 { get; init; } = "14ae7b00ab20c1ab185dbe95b08b668d949aefac160e408ae49df94aeea4edc0";

    public float ConfidenceThreshold { get; init; } = 0.7f;

    /// <summary>Classifications running at once; the rest wait, including image decoding.</summary>
    public int MaxConcurrency { get; init; } = 2;

    /// <summary>Refused before decoding (read from the header), so a 30 000 × 30 000 JPEG never allocates its pixels.</summary>
    public long MaxPixels { get; init; } = 40_000_000;

    public void Validate()
    {
        if (!float.IsFinite(ConfidenceThreshold) || ConfidenceThreshold is < 0 or > 1
            || MaxConcurrency is < 1 or > 16 || MaxPixels <= 0 || string.IsNullOrWhiteSpace(ModelPath))
        {
            throw new InvalidOperationException($"Invalid {SectionName} options.");
        }
    }
}
