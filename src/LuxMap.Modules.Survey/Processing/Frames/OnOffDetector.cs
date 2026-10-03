using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LuxMap.Modules.Survey.Processing.Frames;

public sealed record FrameInput(string RequestId, string FrameId, byte[] Jpeg, int Width, int Height, string ModelVersion);
public sealed record Prediction(int ItemNo, string Label, double Confidence, double X, double Y, double Width, double Height);
public sealed record DetectorOutput(string RequestId, string FrameId, string ModelVersion, int Width, int Height, Prediction[] Predictions, string RawOutput);
public sealed record MediaArtifact(string Version, string Hash, string Metadata);
public interface IOnOffDetector
{
    bool IsFake { get; }
    MediaArtifact Artifact { get; }
    Task<DetectorOutput> DetectAsync(FrameInput frame, CancellationToken ct);
}

public static class DetectorValidation
{
    public static DetectorOutput Validate(FrameInput input, DetectorOutput output)
    {
        if (output.RequestId != input.RequestId || output.FrameId != input.FrameId || output.ModelVersion != input.ModelVersion
            || output.Width != input.Width || output.Height != input.Height || input.Width <= 0 || input.Height <= 0
            || output.Predictions is null || output.Predictions.Select(x => x.ItemNo).Distinct().Count() != output.Predictions.Length)
            throw new ProcessingFailure("DETECTOR_OUTPUT_INVALID", "detector");
        foreach (var p in output.Predictions)
            if (p.ItemNo < 0 || p.Label is not ("on" or "off") || !Unit(p.Confidence) || !Unit(p.X) || !Unit(p.Y)
                || !Unit(p.Width) || !Unit(p.Height) || p.Width <= 0 || p.Height <= 0 || p.X + p.Width > 1 || p.Y + p.Height > 1)
                throw new ProcessingFailure("DETECTOR_OUTPUT_INVALID", "detector");
        try
        {
            using var raw = JsonDocument.Parse(output.RawOutput);
            if (raw.RootElement.ValueKind != JsonValueKind.Object) throw new JsonException();
        }
        catch (JsonException) { throw new ProcessingFailure("DETECTOR_OUTPUT_INVALID", "detector"); }
        return output;
    }
    private static bool Unit(double value) => double.IsFinite(value) && value >= 0 && value <= 1;
}

public sealed record FakeDetectionCase(string Outcome, Prediction[] Predictions);

/// <summary>Explicit development fixture, keyed only by image hash, never by a pole ID.</summary>
public sealed class ManifestOnOffDetector : IOnOffDetector
{
    private readonly IReadOnlyDictionary<string, FakeDetectionCase> cases;
    public bool IsFake => true;
    public MediaArtifact Artifact { get; }
    public ManifestOnOffDetector(string manifest)
    {
        cases = JsonSerializer.Deserialize<Dictionary<string, FakeDetectionCase>>(manifest)
            ?? throw new InvalidOperationException("A detector fixture manifest is required.");
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(manifest)));
        Artifact = new("fake-" + hash, hash, "{\"provider\":\"fixture_manifest\"}");
    }
    public async Task<DetectorOutput> DetectAsync(FrameInput frame, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var hash = Convert.ToHexStringLower(SHA256.HashData(frame.Jpeg));
        if (!cases.TryGetValue(hash, out var fixture)) throw new ProcessingFailure("DETECTOR_FIXTURE_MISSING", "detector");
        if (fixture.Outcome == "timeout") await Task.Delay(Timeout.InfiniteTimeSpan, ct);
        if (fixture.Outcome == "error") throw new ProcessingFailure("DETECTOR_ERROR", "detector");
        var predictions = fixture.Outcome == "malformed" ? new[] { new Prediction(0, "unsupported", 1, 0, 0, 1, 1) } : fixture.Predictions;
        return DetectorValidation.Validate(frame, new(frame.RequestId, frame.FrameId, Artifact.Version,
            frame.Width, frame.Height, predictions, JsonSerializer.Serialize(new { predictions })));
    }
}

public sealed class UnconfiguredOnOffDetector : IOnOffDetector
{
    public bool IsFake => false;
    public MediaArtifact Artifact => throw new ProcessingFailure("DETECTOR_NOT_CONFIGURED", "detector");
    public Task<DetectorOutput> DetectAsync(FrameInput frame, CancellationToken ct)
        => throw new ProcessingFailure("DETECTOR_NOT_CONFIGURED", "detector");
}
