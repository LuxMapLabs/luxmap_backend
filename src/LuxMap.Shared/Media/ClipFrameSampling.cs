namespace LuxMap.Shared.Media;

/// <summary>A stored clip to sample: the object key in the video bucket, and the size and SHA-256 the caller already verified on upload.</summary>
public sealed record ClipSampleRequest(string ObjectKey, long ExpectedBytes, string ExpectedSha256);

/// <summary>One frame cut from a clip. <see cref="Number"/> counts from 0 in time order; <see cref="OffsetMs"/> is from the first displayed frame.</summary>
public sealed record SampledFrame(int Number, int OffsetMs, int Width, int Height, byte[] Jpeg);

/// <summary>
/// What the clip turned out to be. <see cref="Width"/> / <see cref="Height"/> are after display rotation. <see cref="Hdr"/> is true for an
/// HLG or PQ source (POLE-VIDEO D-14: accepted, flagged, never rejected); the raw ffprobe values stay for the record.
/// </summary>
public sealed record ClipInfo(int DurationMs, int Width, int Height, string? PixelFormat, string? ColorTransfer, bool Hdr, int SampledFrames);

/// <summary>The identity of the tools that cut the frames (BE-34): version and hash are one digest, metadata is the tool versions as JSON.</summary>
public sealed record ClipSamplerArtifact(string Version, string Hash, string Metadata);

/// <summary>
/// Cuts frames out of ONE short clip of ONE lamp for the pole video flow (POLE-VIDEO D-4). Owned by Survey, which owns ffmpeg; asked
/// through this port by WorkOrders, which cannot reference Survey. Unlike a survey sweep there is no GPS clock: frames are spread
/// evenly over the clip by their real display time.
/// </summary>
/// <remarks>
/// Failures are <see cref="Http.LuxMapException"/> so a request path needs no translation: 400 for a clip that is too short, too long or
/// unreadable, 503 when ffmpeg is missing, slow or busy. Frames reach <c>consume</c> one at a time, in order; at most
/// <see cref="PoleVideoOptions.MaximumFrames"/> of them.
/// </remarks>
public interface IClipFrameSampler
{
    Task<ClipSamplerArtifact> DescribeAsync(CancellationToken ct);

    Task<ClipInfo> SampleAsync(ClipSampleRequest request, Func<SampledFrame, CancellationToken, Task> consume, CancellationToken ct);
}
