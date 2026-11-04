using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LuxMap.Shared.Storage;

namespace LuxMap.Modules.Survey.Processing.Frames;

/// <summary>What <see cref="FfmpegClipTools"/> needs to know; each caller (survey extractor, pole-video sampler) supplies its own.</summary>
internal sealed record ClipToolSettings(string FfmpegPath, string FfprobePath, string TempRoot, long TemporaryBytesPerClip,
    int MaximumFrameBytes, double TimeoutSeconds);

/// <summary>
/// One clip as ffprobe describes it. <see cref="SourcePts"/> are displayed-frame PTS in stream ticks, ascending;
/// <see cref="PtsNs"/> the same in nanoseconds. <see cref="Width"/> / <see cref="Height"/> are AFTER display rotation.
/// </summary>
internal sealed record ClipProbe(int Num, int Den, int Width, int Height, long[] SourcePts, long[] PtsNs,
    string? PixelFormat, string? ColorTransfer);

/// <summary>
/// The ffmpeg / ffprobe steps shared by the survey frame extractor and the pole-video sampler: describe the tools, fetch a clip
/// into a work directory, probe actual frame PTS, cut chosen frames. Local files and bounded subprocesses only. Moved out of
/// <see cref="FfmpegFrameExtractor"/> unchanged so both callers fail the same way (POLE-VIDEO PV-b).
/// </summary>
internal sealed class FfmpegClipTools(IObjectStore store, ClipToolSettings settings, IMediaProcessRunner runner)
{
    // Bound the depth of ffmpeg's select expression parser, independently of clip/frame count.
    public const int MaximumSelectionTerms = 64;

    public async Task<MediaArtifact> DescribeAsync(CancellationToken ct)
    {
        var ffmpeg = Resolve(settings.FfmpegPath);
        var ffprobe = Resolve(settings.FfprobePath);
        var version = await runner.RunAsync(ffmpeg, ["-version"], settings.TimeoutSeconds, ct);
        var probeVersion = await runner.RunAsync(ffprobe, ["-version"], settings.TimeoutSeconds, ct);
        var metadata = JsonSerializer.Serialize(new { ffmpeg, ffprobe, version, probeVersion });
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var file in new[] { ffmpeg, ffprobe, typeof(FfmpegClipTools).Assembly.Location })
        {
            await using var stream = File.OpenRead(file);
            hash.AppendData(await SHA256.HashDataAsync(stream, ct));
        }
        hash.AppendData(Encoding.UTF8.GetBytes(metadata));
        var digest = Convert.ToHexStringLower(hash.GetHashAndReset());
        return new(digest, digest, metadata);
    }

    private static string Resolve(string executable)
    {
        if (Path.IsPathRooted(executable) && File.Exists(executable)) return executable;
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            var candidate = Path.Combine(directory, executable);
            if (File.Exists(candidate)) return Path.GetFullPath(candidate);
        }
        throw new ProcessingFailure("FFMPEG_NOT_INSTALLED", "extractor");
    }

    /// <summary>
    /// Runs <paramref name="body"/> with a fresh work directory that is always removed afterwards. A timeout the caller did not ask
    /// for becomes <c>VIDEO_TIMEOUT</c>; unreadable ffprobe output becomes <c>VIDEO_INVALID</c>.
    /// </summary>
    public async Task<T> WithWorkDirectoryAsync<T>(Func<string, Task<T>> body, CancellationToken ct)
    {
        var directory = Path.Combine(settings.TempRoot, Guid.NewGuid().ToString("N"));
        try { return await body(directory); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        { throw new ProcessingFailure("VIDEO_TIMEOUT", "extractor"); }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or FormatException or IndexOutOfRangeException or OverflowException)
        { throw new ProcessingFailure("VIDEO_INVALID", "extractor"); }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    /// <summary>Copies the stored clip into <paramref name="directory"/>, checking size and SHA-256 as it goes; returns the file path.</summary>
    public async Task<string> DownloadAsync(string objectKey, long expectedBytes, string expectedHash, string directory, CancellationToken ct)
    {
        if (expectedBytes <= 0 || expectedBytes + settings.MaximumFrameBytes > settings.TemporaryBytesPerClip)
            throw new ProcessingFailure("VIDEO_TEMP_QUOTA", "extractor");
        Directory.CreateDirectory(directory);
        var input = Path.Combine(directory, "clip.mp4");
        using var downloadTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        downloadTimeout.CancelAfter(TimeSpan.FromSeconds(settings.TimeoutSeconds));
        await using (var source = await store.OpenAsync(StorageBucket.Video, objectKey, downloadTimeout.Token))
        await using (var destination = File.Create(input))
        {
            var buffer = new byte[81920]; long count = 0;
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            int read;
            while ((read = await source.ReadAsync(buffer, downloadTimeout.Token)) != 0)
            {
                count += read;
                if (count > expectedBytes) throw new ProcessingFailure("VIDEO_TEMP_QUOTA", "extractor");
                hash.AppendData(buffer, 0, read);
                await destination.WriteAsync(buffer.AsMemory(0, read), downloadTimeout.Token);
            }
            if (count != expectedBytes || Convert.ToHexStringLower(hash.GetHashAndReset()) != expectedHash)
                throw new ProcessingFailure("VIDEO_HASH_MISMATCH", "extractor");
        }
        return input;
    }

    public async Task<ClipProbe> ProbeAsync(string input, CancellationToken ct, Action<int, int>? checkTimeBase = null)
    {
        // Packet PTS avoids decoding the entire clip during probe (frame=pts does decode).
        // Keep stream rotation and discard flags: edit lists retain non-displayed reference packets.
        // Packets arrive in decode order with B-frames; sort only the displayed PTS.
        var probe = await runner.RunAsync(settings.FfprobePath,
            ["-v", "error", "-select_streams", "v:0", "-show_entries", "stream=width,height,time_base,pix_fmt,color_transfer:stream_side_data=rotation:packet=pts,flags", "-of", "json=compact=1", input], settings.TimeoutSeconds, ct);
        using var doc = JsonDocument.Parse(probe);
        var streamInfo = doc.RootElement.GetProperty("streams")[0];
        var timeBase = streamInfo.GetProperty("time_base").GetString()!.Split('/');
        int num = int.Parse(timeBase[0], CultureInfo.InvariantCulture), den = int.Parse(timeBase[1], CultureInfo.InvariantCulture);
        // The caller's clock check ran here, before the rotation and size checks, in the extractor this was moved from.
        checkTimeBase?.Invoke(num, den);
        int width = streamInfo.GetProperty("width").GetInt32(), height = streamInfo.GetProperty("height").GetInt32();
        int rotation = 0;
        if (streamInfo.TryGetProperty("side_data_list", out var sideData))
            foreach (var side in sideData.EnumerateArray())
                if (side.TryGetProperty("rotation", out var angle)) rotation = angle.GetInt32();
        if (rotation % 90 != 0) throw new ProcessingFailure("VIDEO_ROTATION_UNSUPPORTED", "extractor");
        if (Math.Abs(rotation % 180) == 90) (width, height) = (height, width);
        if (width <= 0 || height <= 0 || (long)width * height > 3840L * 2160)
            throw new ProcessingFailure("VIDEO_DIMENSIONS", "extractor");
        var sourcePts = doc.RootElement.GetProperty("packets").EnumerateArray()
            .Where(p => !p.GetProperty("flags").GetString()!.Contains('D'))
            .Select(p => p.GetProperty("pts").GetInt64()).Order().ToArray();
        // Missing displayed PTS is VIDEO_INVALID; duplicates remain for the strict clock check by the caller.
        var pts = sourcePts.Select(value => checked((long)decimal.Round((decimal)value * num * 1_000_000_000 / den, 0, MidpointRounding.AwayFromZero))).ToArray();
        return new(num, den, width, height, sourcePts, pts,
            streamInfo.TryGetProperty("pix_fmt", out var pixelFormat) ? pixelFormat.GetString() : null,
            streamInfo.TryGetProperty("color_transfer", out var transfer) ? transfer.GetString() : null);
    }

    /// <summary>
    /// Cuts the frames at <paramref name="batch"/> (indices into the probe's PTS list, at most <see cref="MaximumSelectionTerms"/>)
    /// in one ffmpeg run and hands them to <paramref name="consume"/> in order. The whole batch is validated before any frame is exposed.
    /// </summary>
    public async Task ExtractBatchAsync(string input, string directory, long expectedBytes, ClipProbe probe, int[] batch,
        Func<int, byte[], CancellationToken, Task> consume, CancellationToken ct)
    {
        var outputPattern = Path.Combine(directory, "frame_%08d.jpg");
        void CheckOutputQuota()
        {
            long totalBytes = expectedBytes;
            foreach (var file in Directory.EnumerateFiles(directory, "frame_*.jpg"))
            {
                var length = new FileInfo(file).Length;
                if (length >= settings.MaximumFrameBytes) throw new ProcessingFailure("FRAME_SIZE_LIMIT", "extractor");
                totalBytes += length;
                if (totalBytes > settings.TemporaryBytesPerClip) throw new ProcessingFailure("VIDEO_TEMP_QUOTA", "extractor");
            }
        }
        // Absolute input seek plus copyts retains the original stream time base/PTS,
        // even for clips whose first PTS is nonzero. Select discards keyframe pre-roll;
        // noaccurate_seek avoids ffmpeg discarding additional frames at nonzero starts.
        var seek = Math.Max((decimal)probe.SourcePts[0] * probe.Num / probe.Den,
            (decimal)probe.SourcePts[batch[0]] * probe.Num / probe.Den - 1).ToString("0.#########", CultureInfo.InvariantCulture);
        var selection = "select=" + string.Join("+", batch.Select(index =>
            "eq(pts\\," + probe.SourcePts[index].ToString(CultureInfo.InvariantCulture) + ")"));
        await runner.RunAsync(settings.FfmpegPath,
            ["-nostdin", "-hide_banner", "-loglevel", "error", "-xerror", "-threads", "1", "-seek_timestamp", "1", "-ss", seek, "-noaccurate_seek", "-copyts", "-i", input, "-map", "0:v:0",
                "-vf", selection, "-fps_mode", "passthrough", "-frames:v", batch.Length.ToString(CultureInfo.InvariantCulture),
                "-threads", "1", "-q:v", "2", "-pix_fmt", "yuvj420p", "-f", "image2", "-start_number", "1", "-y", outputPattern],
            settings.TimeoutSeconds, ct, CheckOutputQuota);
        CheckOutputQuota();
        var outputs = Directory.GetFiles(directory, "frame_*.jpg").Order(StringComparer.Ordinal).ToArray();
        if (outputs.Length != batch.Length) throw new ProcessingFailure("FRAME_COUNT_MISMATCH", "extractor");
        // Validate this entire batch before exposing any of its frames to the consumer.
        for (int i = 0; i < outputs.Length; i++)
            if (Path.GetFileName(outputs[i]) != "frame_" + (i + 1).ToString("D8", CultureInfo.InvariantCulture) + ".jpg"
                || new FileInfo(outputs[i]).Length == 0)
                throw new ProcessingFailure("FRAME_SEQUENCE_INVALID", "extractor");
        for (int i = 0; i < outputs.Length; i++)
        {
            var bytes = await File.ReadAllBytesAsync(outputs[i], ct);
            await consume(batch[i], bytes, ct);
            File.Delete(outputs[i]);
        }
    }
}
