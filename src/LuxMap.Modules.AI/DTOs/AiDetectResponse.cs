namespace LuxMap.Modules.AI.DTOs;

/// <summary>
/// <c>POST /ai/detect</c> — the shape of PR #118 (<c>count</c>, <c>detections[]</c> with pixel boxes in the uploaded image), plus
/// which model answered and the image size the boxes refer to (after EXIF orientation).
/// </summary>
public sealed record AiDetectResponse
{
    /// <summary>The model's <c>artifact_version</c> — the same value the survey pipeline records on each detection.</summary>
    public required string ModelVersion { get; init; }

    public required int Width { get; init; }

    public required int Height { get; init; }

    public required int Count { get; init; }

    public required IReadOnlyList<DetectionResult> Detections { get; init; }
}
