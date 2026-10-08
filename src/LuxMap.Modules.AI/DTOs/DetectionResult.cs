namespace LuxMap.Modules.AI.DTOs;

/// <summary>One box from the model, in pixels of the image as decoded (after EXIF orientation).</summary>
public sealed class DetectionResult
{
    public int ClassId { get; set; }

    /// <summary><c>normal</c> (lamp ON) or <c>out</c> (lamp OFF) — the model's own class names.</summary>
    public string ClassName { get; set; } = string.Empty;

    public float Confidence { get; set; }

    public BoundingBox BoundingBox { get; set; } = new();
}
