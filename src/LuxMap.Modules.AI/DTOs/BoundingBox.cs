namespace LuxMap.Modules.AI.DTOs;

public class BoundingBox
{
    public float X1 { get; set; }
    public float Y1 { get; set; }

    public float X2 { get; set; }
    public float Y2 { get; set; }

    public float Width => X2 - X1;
    public float Height => Y2 - Y1;
}
