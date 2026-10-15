using System.Security.Cryptography;
using System.Text;
using LuxMap.Infrastructure.Storage;
using LuxMap.Modules.AI.DTOs;
using LuxMap.Shared.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.PixelFormats;

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
            return new SingleLampPhotoHint(label, confidence, detector.Artifact.Version, $"{detector.Artifact.Version}+{rules}");
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
