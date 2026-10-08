using LuxMap.Modules.AI.DTOs;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace LuxMap.Modules.AI.Detection;

/// <summary>How the original image was fitted into the square model input: scaled by <c>Scale</c>, then padded.</summary>
public sealed record Letterbox(float Scale, int PadX, int PadY, int Width, int Height)
{
    public static Letterbox For(int width, int height, int size)
    {
        var scale = Math.Min(size / (float)width, size / (float)height);
        var resizedWidth = (int)Math.Round(width * scale);
        var resizedHeight = (int)Math.Round(height * scale);
        return new Letterbox(scale, (size - resizedWidth) / 2, (size - resizedHeight) / 2, width, height);
    }

    public int ResizedWidth => (int)Math.Round(Width * Scale);

    public int ResizedHeight => (int)Math.Round(Height * Scale);
}

/// <summary>
/// YOLO (Ultralytics, channel-first <c>[1, 4 + classes, N]</c>) output → boxes in ORIGINAL pixels, then per-class NMS. Pure,
/// so it is tested on hand-built tensors. The parsing and the inverse letterbox are the PR #118 code, unchanged in substance.
/// </summary>
public static class YoloPostprocess
{
    public static List<DetectionResult> Parse(Tensor<float> output, IReadOnlyList<string> classes, float threshold, Letterbox box)
    {
        var count = output.Dimensions[2];
        var detections = new List<DetectionResult>();

        for (var i = 0; i < count; i++)
        {
            var bestClass = -1;
            var bestScore = 0f;
            for (var classId = 0; classId < classes.Count; classId++)
            {
                var score = output[0, 4 + classId, i];
                if (float.IsFinite(score) && score > bestScore)
                {
                    bestScore = score;
                    bestClass = classId;
                }
            }

            if (bestClass < 0 || bestScore < threshold)
            {
                continue;
            }

            float centerX = output[0, 0, i], centerY = output[0, 1, i], width = output[0, 2, i], height = output[0, 3, i];
            if (!float.IsFinite(centerX) || !float.IsFinite(centerY) || !float.IsFinite(width) || !float.IsFinite(height))
            {
                continue;
            }

            // Centre/size in letterboxed input pixels → corners in original pixels, clamped to the image.
            var x1 = Math.Clamp((centerX - width / 2f - box.PadX) / box.Scale, 0, box.Width);
            var y1 = Math.Clamp((centerY - height / 2f - box.PadY) / box.Scale, 0, box.Height);
            var x2 = Math.Clamp((centerX + width / 2f - box.PadX) / box.Scale, 0, box.Width);
            var y2 = Math.Clamp((centerY + height / 2f - box.PadY) / box.Scale, 0, box.Height);
            if (x2 <= x1 || y2 <= y1)
            {
                continue;
            }

            detections.Add(new DetectionResult
            {
                ClassId = bestClass,
                ClassName = classes[bestClass],
                Confidence = bestScore,
                BoundingBox = new BoundingBox { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2 },
            });
        }

        return detections;
    }

    /// <summary>Non-maximum suppression per class, highest confidence first.</summary>
    public static List<DetectionResult> Nms(IEnumerable<DetectionResult> detections, float iouThreshold)
    {
        var kept = new List<DetectionResult>();
        foreach (var group in detections.GroupBy(detection => detection.ClassId))
        {
            var boxes = group.OrderByDescending(detection => detection.Confidence).ToList();
            while (boxes.Count > 0)
            {
                var best = boxes[0];
                kept.Add(best);
                boxes = [.. boxes.Skip(1).Where(other => IoU(best.BoundingBox, other.BoundingBox) < iouThreshold)];
            }
        }

        return [.. kept.OrderByDescending(detection => detection.Confidence)];
    }

    public static float IoU(BoundingBox a, BoundingBox b)
    {
        var width = Math.Max(0, Math.Min(a.X2, b.X2) - Math.Max(a.X1, b.X1));
        var height = Math.Max(0, Math.Min(a.Y2, b.Y2) - Math.Max(a.Y1, b.Y1));
        var intersection = width * height;
        var union = (a.X2 - a.X1) * (a.Y2 - a.Y1) + (b.X2 - b.X1) * (b.Y2 - b.Y1) - intersection;
        return union <= 0 ? 0 : intersection / union;
    }
}
