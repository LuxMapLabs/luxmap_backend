using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LuxMap.Modules.AI.DTOs;
using LuxMap.Modules.Survey.Processing;
using LuxMap.Modules.Survey.Processing.Frames;
using LuxMap.Shared.Http;
using LuxMap.Shared.Serialization;

namespace LuxMap.Modules.AI.Detection;

/// <summary>
/// The YOLO model as the survey pipeline's detector (BE-15 §6) — chosen with <c>SurveyProcessing:Frames:Detector = yolo</c>.
/// </summary>
/// <remarks>
/// <para>
/// <c>normal</c> → <c>on</c>, <c>out</c> → <c>off</c>: CV decides ON / OFF only; Dim comes from the lux sensor (Phiếu v1.4).
/// Boxes become top-left + size normalised to [0, 1]. No box is "no detection", never "off".
/// </para>
/// <para>
/// <see cref="Artifact"/> identifies the MODEL FILE and every setting that changes the output (thresholds, preprocessing,
/// class mapping): its version is a hash of all of them, so a threshold change is a new <c>artifact_version</c> row and
/// every detection stays traceable (BE-34).
/// </para>
/// </remarks>
public sealed class YoloOnOffDetector : IOnOffDetector
{
    public static readonly IReadOnlyDictionary<string, string> Labels = new Dictionary<string, string>
    {
        ["normal"] = "on",
        ["out"] = "off",
    };

    private readonly YoloModel model;

    public YoloOnOffDetector(YoloModel model)
    {
        this.model = model;
        var metadata = JsonSerializer.Serialize(new
        {
            provider = "onnxruntime",
            model_sha256 = model.Sha256,
            classes = Labels,
            input_size = YoloModel.InputSize,
            letterbox_fill = 114,
            resampler = "bilinear",
            exif_orientation = true,
            confidence_threshold = model.Options.ConfidenceThreshold,
            iou_threshold = model.Options.IouThreshold,
        }, LuxMapJsonOptions.Default);
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(metadata)));
        Artifact = new MediaArtifact("yolo-" + hash[..16], hash, metadata);
    }

    public bool IsFake => false;

    public MediaArtifact Artifact { get; }

    /// <summary>
    /// Pixel corners → top-left + size in [0, 1], in DOUBLE throughout. Codex review: computed in float, a box from x = 128 to the
    /// right edge of a 640-wide frame gave x = 0.20000000298 and width 0.8 — a sum above 1, and the pipeline's validation
    /// threw away the whole frame.
    /// </summary>
    public static Prediction Normalise(int itemNo, DetectionResult detection, int width, int height)
    {
        var box = detection.BoundingBox;
        var (x, w) = Span(box.X1, box.X2, width);
        var (y, h) = Span(box.Y1, box.Y2, height);
        return new Prediction(itemNo, Labels[detection.ClassName], detection.Confidence, x, y, w, h);
    }

    private static (double Start, double Length) Span(float from, float to, int size)
    {
        var start = Math.Clamp((double)from / size, 0, 1);
        var length = Math.Min(((double)to - from) / size, 1 - start);

        // start + (1 - start) can still round one ulp above 1.
        while (start + length > 1)
        {
            length = Math.BitDecrement(length);
        }

        return (start, length);
    }

    public async Task<DetectorOutput> DetectAsync(FrameInput frame, CancellationToken ct)
    {
        YoloResult result;
        try
        {
            result = await model.DetectAsync(new MemoryStream(frame.Jpeg, writable: false), ct);
        }
        catch (LuxMapException)
        {
            // A frame the pipeline extracted itself that does not decode is the pipeline's failure, not the client's.
            throw new ProcessingFailure("DETECTOR_ERROR", "detector");
        }

        var predictions = result.Detections
            .Select((detection, index) => Normalise(index, detection, result.Width, result.Height))
            .ToArray();

        return DetectorValidation.Validate(frame, new DetectorOutput(frame.RequestId, frame.FrameId, Artifact.Version,
            result.Width, result.Height, predictions,
            JsonSerializer.Serialize(new { detections = result.Detections }, LuxMapJsonOptions.Default)));
    }
}
