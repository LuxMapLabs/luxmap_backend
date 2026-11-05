using System.Security.Cryptography;
using System.Text;
using LuxMap.Infrastructure.Storage;
using LuxMap.Modules.AI.DTOs;
using LuxMap.Shared.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LuxMap.Modules.AI.Detection;

/// <summary>Shared single-lamp/daylight rules, including the unchanged Citizen cq rules hash.</summary>
public sealed class YoloSingleLampPhotoHinter(IServiceProvider services, IConfiguration configuration, ILogger<YoloSingleLampPhotoHinter> log)
    : ISingleLampPhotoHinter
{
    private readonly double daylightLuma = configuration.GetValue("Citizen:DaylightMeanLuma", 0.45);
    private readonly string rules = RulesVersion(configuration.GetValue("Citizen:DaylightMeanLuma", 0.45));

    public async Task<SingleLampPhotoHint?> HintAsync(Stream photo, Stream thumbnail, CancellationToken ct)
    {
        try
        {
            var model = services.GetRequiredService<YoloModel>();
            var detector = services.GetRequiredService<YoloOnOffDetector>();
            var luma = await MeanLumaAsync(thumbnail, ct);
            var result = await model.DetectAsync(photo, ct);
            var (label, confidence) = Classify(result.Detections, luma, daylightLuma);
            return new SingleLampPhotoHint(label, confidence, detector.Artifact.Version, $"{detector.Artifact.Version}+{rules}",
                label == "inconclusive" ? Reason(result.Detections, luma, daylightLuma) : null);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            log.LogWarning(error, "No AI hint for a single-lamp photo: {Reason}", error.Message);
            return null;
        }
    }

    /// <summary>
    /// <c>on</c> / <c>off</c> only for exactly one lamp found in a photo no brighter than <paramref name="daylightLuma"/>,
    /// with that lamp's confidence; <c>inconclusive</c> (no confidence) otherwise.
    /// </summary>
    public static (string Label, double? Confidence) Classify(
        IReadOnlyList<DetectionResult> detections, double meanLuma, double daylightLuma)
    {
        if (detections.Count != 1 || meanLuma > daylightLuma) return ("inconclusive", null);

        var lamp = detections[0];
        // Math.Clamp passes NaN through; a non-finite score must never reach the CHECK (the double precision rule).
        double confidence = lamp.Confidence;
        if (!double.IsFinite(confidence)) return ("inconclusive", null);
        return lamp.ClassName switch
        {
            "normal" => ("on", Math.Clamp(confidence, 0, 1)),
            "out" => ("off", Math.Clamp(confidence, 0, 1)),
            _ => ("inconclusive", null),
        };
    }

    /// <summary>Mean luma (0..1) of a JPEG thumbnail, decoded only through the JPEG-only configuration.</summary>
    public static Task<double> MeanLumaAsync(Stream thumbnail, CancellationToken ct) => JpegLuma.MeanAsync(thumbnail, ct);

    /// <summary>
    /// Why <see cref="Classify"/> gave no verdict, or null when it gave one: a photo too bright to judge (daylight) first, then no lamp
    /// (or one the model cannot name), then more than one lamp.
    /// </summary>
    public static string? Reason(IReadOnlyList<DetectionResult> detections, double meanLuma, double daylightLuma)
    {
        if (meanLuma > daylightLuma) return "too_bright";
        if (detections.Count > 1) return "multiple_lamps";
        if (detections.Count == 0) return "no_lamp";
        return detections[0].ClassName is "normal" or "out" && double.IsFinite(detections[0].Confidence) ? null : "no_lamp";
    }

    private static string RulesVersion(double daylightLuma)
    {
        var rules = FormattableString.Invariant($"citizen-hint:single-lamp;daylight-mean-luma<={daylightLuma:R}");
        return "cq-" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(rules)))[..12];
    }
}
