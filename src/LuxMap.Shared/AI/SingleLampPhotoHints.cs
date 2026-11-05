namespace LuxMap.Shared.AI;

/// <summary>ON is not a photometric classification; a single image cannot establish dim.</summary>
/// <param name="Reason">
/// For an <c>inconclusive</c> hint, why: <c>too_bright</c> (daylight), <c>no_lamp</c> or <c>multiple_lamps</c>; null otherwise. Added for the
/// pole-video verdict (POLE-VIDEO D-4); photo and citizen flows ignore it.
/// </param>
public sealed record SingleLampPhotoHint(string CvState, double? Confidence, string ModelVersion, string ArtifactVersion, string? Reason = null);

/// <summary>Optional, lazy inference. Null means unavailable; cancellation is propagated.</summary>
public interface ISingleLampPhotoHinter
{
    Task<SingleLampPhotoHint?> HintAsync(Stream photo, Stream thumbnail, CancellationToken ct);
}
