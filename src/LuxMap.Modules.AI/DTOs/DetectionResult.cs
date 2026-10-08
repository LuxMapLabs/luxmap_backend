namespace LuxMap.Modules.AI.DTOs;

public class DetectionResult
{
    public int ClassId { get; set; }

    public string ClassName { get; set; } = string.Empty;

    public float Confidence { get; set; }

    public BoundingBox BoundingBox { get; set; } = new();
}
