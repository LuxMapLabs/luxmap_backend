using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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

        var predictions = result.Detections.Select((detection, index) =>
        {
            var box = detection.BoundingBox;
            var x = box.X1 / result.Width;
            var y = box.Y1 / result.Height;
            return new Prediction(index, Labels[detection.ClassName], detection.Confidence, x, y,
                Math.Min((double)box.Width / result.Width, 1 - x), Math.Min((double)box.Height / result.Height, 1 - y));
        }).ToArray();

        return DetectorValidation.Validate(frame, new DetectorOutput(frame.RequestId, frame.FrameId, Artifact.Version,
            result.Width, result.Height, predictions,
            JsonSerializer.Serialize(new { detections = result.Detections }, LuxMapJsonOptions.Default)));
    }
}
