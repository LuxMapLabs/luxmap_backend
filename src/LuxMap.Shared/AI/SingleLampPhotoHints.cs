namespace LuxMap.Shared.AI;

/// <summary>ON is not a photometric classification; a single image cannot establish dim.</summary>
public sealed record SingleLampPhotoHint(string CvState, double? Confidence, string ModelVersion, string ArtifactVersion);

/// <summary>Optional, lazy inference. Null means unavailable; cancellation is propagated.</summary>
public interface ISingleLampPhotoHinter
{
    Task<SingleLampPhotoHint?> HintAsync(Stream photo, Stream thumbnail, CancellationToken ct);
}
