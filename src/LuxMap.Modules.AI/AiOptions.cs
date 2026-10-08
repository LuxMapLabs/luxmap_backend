namespace LuxMap.Modules.AI;

/// <summary>Configuration section <c>Ai</c> — the YOLO ON / OFF model (AI-1, SELF-SIGNED).</summary>
public sealed record AiOptions
{
    public const string SectionName = "Ai";

    /// <summary>Relative paths resolve against the app's base directory, where the build copies <c>Models/best.onnx</c>.</summary>
    public string ModelPath { get; init; } = Path.Combine("Models", "best.onnx");

    /// <summary>
    /// SHA-256 of the model file the deployment expects. Set ⇒ a different file STOPS startup instead of silently changing
    /// every detection (BE-34). The model lives in git for the pilot; a swapped file must be a reviewed change of this value.
    /// </summary>
    public string? ModelSha256 { get; init; }

    public float ConfidenceThreshold { get; init; } = 0.25f;

    public float IouThreshold { get; init; } = 0.45f;

    /// <summary>Inferences run at once; the rest wait. CPU inference of 640×640 takes a core for a few hundred ms.</summary>
    public int MaxConcurrency { get; init; } = 2;

    /// <summary>Refused before decoding (read from the header), so a 30 000 × 30 000 JPEG never allocates its pixels.</summary>
    public long MaxPixels { get; init; } = 40_000_000;

    public void Validate()
    {
        if (!float.IsFinite(ConfidenceThreshold) || ConfidenceThreshold is <= 0 or >= 1
            || !float.IsFinite(IouThreshold) || IouThreshold is <= 0 or >= 1
            || MaxConcurrency is < 1 or > 16 || MaxPixels <= 0 || string.IsNullOrWhiteSpace(ModelPath))
        {
            throw new InvalidOperationException($"Invalid {SectionName} options.");
        }
    }
}
