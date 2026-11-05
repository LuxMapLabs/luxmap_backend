using System.Net;
using LuxMap.Shared.Http;
using LuxMap.Shared.Media;
using LuxMap.Shared.Storage;
using Microsoft.Extensions.Options;

namespace LuxMap.Modules.Survey.Processing.Frames;

/// <summary>
/// <see cref="IClipFrameSampler"/> on the survey module's ffmpeg steps, with its OWN slot count and work directory so a request does
/// not queue behind a night's survey processing. A <see cref="ProcessingFailure"/> from those steps is translated here to the
/// <see cref="LuxMapException"/> the request path expects.
/// </summary>
public sealed class FfmpegClipFrameSampler(IObjectStore store, SurveyFrameOptions tools, IOptions<PoleVideoOptions> sampling,
    IMediaProcessRunner? processRunner = null) : IClipFrameSampler, IDisposable
{
    private readonly PoleVideoOptions options = sampling.Value;
    private readonly FfmpegClipTools clip = new(store, new(tools.FfmpegPath, tools.FfprobePath, sampling.Value.TempRoot,
        sampling.Value.TemporaryBytesPerClip, sampling.Value.MaximumFrameBytes, sampling.Value.TimeoutSeconds),
        processRunner ?? new LocalMediaProcessRunner());
    private readonly SemaphoreSlim slots = new(sampling.Value.MaximumConcurrentClips);
    public void Dispose() => slots.Dispose();

    // The tools do not change while the process runs; hashing ffmpeg's binary on every request would cost more than cutting the frames.
    // Only a success is remembered — a missing ffmpeg must be noticed again once it is installed.
    private ClipSamplerArtifact? described;

    public async Task<ClipSamplerArtifact> DescribeAsync(CancellationToken ct)
    {
        if (described is { } known) return known;
        var artifact = await Translate(() => clip.DescribeAsync(ct));
        return described = new(artifact.Version, artifact.Hash, artifact.Metadata);
    }

    public async Task<ClipInfo> SampleAsync(ClipSampleRequest request, Func<SampledFrame, CancellationToken, Task> consume, CancellationToken ct)
    {
        if (!await slots.WaitAsync(TimeSpan.FromSeconds(options.SlotWaitSeconds), ct))
            throw new LuxMapException("VIDEO_PROCESSING_BUSY", HttpStatusCode.ServiceUnavailable, "Video processing is busy; try again shortly.");
        try
        {
            return await Translate(() => clip.WithWorkDirectoryAsync(async directory =>
            {
                var input = await clip.DownloadAsync(request.ObjectKey, request.ExpectedBytes, request.ExpectedSha256, directory, ct);
                var probe = await clip.ProbeAsync(input, ct);
                var pts = probe.PtsNs;
                // Duplicate or backwards display times mean the stream is not one clip of frames we can place in time.
                if (pts.Length == 0 || pts.Zip(pts.Skip(1)).Any(pair => pair.First >= pair.Second))
                    throw new ProcessingFailure("VIDEO_INVALID", "extractor");
                var indices = ClipSamplePlanner.Select(pts, options);
                var number = 0;
                foreach (var batch in indices.Chunk(FfmpegClipTools.MaximumSelectionTerms))
                    await clip.ExtractBatchAsync(input, directory, request.ExpectedBytes, probe, batch, (index, bytes, token) =>
                        consume(new(number++, (int)Math.Round((pts[index] - pts[0]) / 1e6), probe.Width, probe.Height, bytes), token), ct);
                return new ClipInfo((int)Math.Round(ClipSamplePlanner.DurationNs(pts) / 1e6), probe.Width, probe.Height,
                    probe.PixelFormat, probe.ColorTransfer, IsHdr(probe.ColorTransfer), indices.Length);
            }, ct));
        }
        finally { slots.Release(); }
    }

    /// <summary>HLG (<c>arib-std-b67</c>) and PQ (<c>smpte2084</c>) are the transfer functions of HDR video.</summary>
    public static bool IsHdr(string? colorTransfer) => colorTransfer is "arib-std-b67" or "smpte2084";

    private static async Task<T> Translate<T>(Func<Task<T>> work)
    {
        try { return await work(); }
        catch (ProcessingFailure failure)
        {
            throw failure.Code switch
            {
                "VIDEO_INVALID" or "FRAME_COUNT_MISMATCH" or "FRAME_SEQUENCE_INVALID" or "FRAME_SIZE_LIMIT" or "VIDEO_OUTPUT_LIMIT"
                    => new LuxMapException("VIDEO_INVALID", HttpStatusCode.BadRequest, "The clip could not be read as video."),
                "VIDEO_ROTATION_UNSUPPORTED" or "VIDEO_DIMENSIONS"
                    => new LuxMapException(failure.Code, HttpStatusCode.BadRequest, "The clip's rotation or size is not supported."),
                "VIDEO_TIMEOUT"
                    => new LuxMapException("VIDEO_PROCESSING_TIMEOUT", HttpStatusCode.ServiceUnavailable, "Video processing took too long."),
                "VIDEO_HASH_MISMATCH"
                    => new LuxMapException("VIDEO_STORAGE_MISMATCH", HttpStatusCode.InternalServerError, "The stored clip does not match its recorded hash."),
                _ => new LuxMapException("VIDEO_PROCESSING_UNAVAILABLE", HttpStatusCode.ServiceUnavailable, "Video processing is unavailable."),
            };
        }
    }
}
