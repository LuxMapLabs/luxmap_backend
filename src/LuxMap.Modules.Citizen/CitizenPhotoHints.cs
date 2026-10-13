using System.Security.Cryptography;
using System.Text;
using LuxMap.Infrastructure.Storage;
using LuxMap.Modules.AI.Detection;
using LuxMap.Modules.AI.DTOs;
using LuxMap.Modules.Citizen.Entities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.PixelFormats;

namespace LuxMap.Modules.Citizen;

/// <summary>The model's reading of one citizen photo.</summary>
public sealed record PhotoHint(CitizenPhotoHint Label, double? Confidence, string ArtifactVersion);

/// <summary>
/// Suggests whether a citizen photo shows a lit or a dark lamp (CITIZEN-QR P2c, D-9). A seam so tests can stand in for the model,
/// and so a host without a model file still serves the review queue — the hint is optional, the queue is not.
/// </summary>
public interface ICitizenPhotoHinter
{
    /// <summary>The hint, or null when it cannot be computed (no model file, an unreadable photo).</summary>
    /// <param name="photo">The stored (cleaned) photo, which the model reads.</param>
    /// <param name="thumbnail">Its thumbnail, which the daylight check reads.</param>
    Task<PhotoHint?> HintAsync(Stream photo, Stream thumbnail, CancellationToken ct);
}

/// <summary>
/// <see cref="ICitizenPhotoHinter"/> on the YOLO ON / OFF model (AI-1) — the same model, decoder and pixel cap as the survey pipeline
/// and <c>POST /ai/detect</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Conclusive only for ONE lamp in a dark photo</b> (D-9, Codex review of the P2c plan #5). Several lamps: which one is the QR's pole
/// is not known. A bright photo: probably taken by day, when a dark lamp is normal. Both are <c>inconclusive</c>, as is no lamp at all.
/// </para>
/// <para>
/// <c>artifact_version</c> = the detector's own version (model file, thresholds, preprocessing — BE-34) PLUS a hash of these rules,
/// so changing the daylight threshold is a new version and every stored hint stays traceable (AI-1).
/// </para>
/// <para>
/// The model is resolved on first use, not injected: <see cref="YoloModel"/> throws at construction when the model file is missing,
/// and injecting it would make every report detail a 500 on such a host. A failure to compute a hint is logged and leaves the photo
/// without one; it never fails the Manager's request.
/// </para>
/// </remarks>
public sealed class YoloCitizenPhotoHinter(IServiceProvider services, CitizenOptions options, ILogger<YoloCitizenPhotoHinter> log)
    : ICitizenPhotoHinter
{
    private readonly string rules = RulesVersion(options.DaylightMeanLuma);

    public async Task<PhotoHint?> HintAsync(Stream photo, Stream thumbnail, CancellationToken ct)
    {
        try
        {
            var model = services.GetRequiredService<YoloModel>();
            var detector = services.GetRequiredService<YoloOnOffDetector>();
            var luma = await MeanLumaAsync(thumbnail, ct);
            var result = await model.DetectAsync(photo, ct);
            var (label, confidence) = Classify(result.Detections, luma, options.DaylightMeanLuma);
            return new PhotoHint(label, confidence, $"{detector.Artifact.Version}+{rules}");
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            log.LogWarning(error, "No AI hint for a citizen photo: {Reason}", error.Message);
            return null;
        }
    }

    /// <summary>
    /// <c>lamp_on</c> / <c>lamp_off</c> only for exactly one lamp found in a photo no brighter than <paramref name="daylightLuma"/>,
    /// with that lamp's confidence; <c>inconclusive</c> (no confidence) otherwise.
    /// </summary>
    public static (CitizenPhotoHint Label, double? Confidence) Classify(
        IReadOnlyList<DetectionResult> detections, double meanLuma, double daylightLuma)
    {
        if (detections.Count != 1 || meanLuma > daylightLuma) return (CitizenPhotoHint.Inconclusive, null);

        var lamp = detections[0];
        // Math.Clamp passes NaN through; a non-finite score must never reach the CHECK (the double precision rule).
        double confidence = lamp.Confidence;
        if (!double.IsFinite(confidence)) return (CitizenPhotoHint.Inconclusive, null);
        return lamp.ClassName switch
        {
            "normal" => (CitizenPhotoHint.LampOn, Math.Clamp(confidence, 0, 1)),
            "out" => (CitizenPhotoHint.LampOff, Math.Clamp(confidence, 0, 1)),
            _ => (CitizenPhotoHint.Inconclusive, null),
        };
    }

    /// <summary>Mean luma (0..1) of a JPEG thumbnail, decoded only through the JPEG-only configuration.</summary>
    public static async Task<double> MeanLumaAsync(Stream thumbnail, CancellationToken ct)
    {
        thumbnail.Position = 0;
        await JpegMagicBytes.EnsureJpegAsync(thumbnail, ct);
        thumbnail.Position = 0;
        using var image = await Image.LoadAsync<L8>(new DecoderOptions { Configuration = ThumbnailFactory.JpegOnly }, thumbnail, ct);
        long sum = 0;
        image.ProcessPixelRows(rows =>
        {
            for (var y = 0; y < rows.Height; y++)
            {
                foreach (var pixel in rows.GetRowSpan(y)) sum += pixel.PackedValue;
            }
        });
        return sum / (255.0 * image.Width * image.Height);
    }

    private static string RulesVersion(double daylightLuma)
    {
        var rules = FormattableString.Invariant($"citizen-hint:single-lamp;daylight-mean-luma<={daylightLuma:R}");
        return "cq-" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(rules)))[..12];
    }
}
