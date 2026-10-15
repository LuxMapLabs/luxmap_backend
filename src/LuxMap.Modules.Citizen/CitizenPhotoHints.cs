using LuxMap.Modules.AI.Detection;
using LuxMap.Modules.AI.DTOs;
using LuxMap.Modules.Citizen.Entities;
using LuxMap.Shared.AI;

namespace LuxMap.Modules.Citizen;

public sealed record PhotoHint(CitizenPhotoHint Label, double? Confidence, string ArtifactVersion);

public interface ICitizenPhotoHinter
{
    Task<PhotoHint?> HintAsync(Stream photo, Stream thumbnail, CancellationToken ct);
}

/// <summary>Citizen keeps its wire labels and the exact legacy artifact hash; rules live in AI.</summary>
public sealed class YoloCitizenPhotoHinter(ISingleLampPhotoHinter hinter) : ICitizenPhotoHinter
{
    public async Task<PhotoHint?> HintAsync(Stream photo, Stream thumbnail, CancellationToken ct)
    {
        var result = await hinter.HintAsync(photo, thumbnail, ct);
        return result is null ? null : new PhotoHint(Label(result.CvState), result.Confidence, result.ArtifactVersion);
    }

    public static (CitizenPhotoHint Label, double? Confidence) Classify(
        IReadOnlyList<DetectionResult> detections, double meanLuma, double daylightLuma)
    {
        var result = YoloSingleLampPhotoHinter.Classify(detections, meanLuma, daylightLuma);
        return (Label(result.Label), result.Confidence);
    }

    public static Task<double> MeanLumaAsync(Stream thumbnail, CancellationToken ct)
        => YoloSingleLampPhotoHinter.MeanLumaAsync(thumbnail, ct);

    private static CitizenPhotoHint Label(string state) => state switch
    {
        "on" => CitizenPhotoHint.LampOn,
        "off" => CitizenPhotoHint.LampOff,
        _ => CitizenPhotoHint.Inconclusive,
    };
}
