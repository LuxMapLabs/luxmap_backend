using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LuxMap.Shared.Storage;

namespace LuxMap.Modules.Survey.Processing.Frames;

public sealed record FrameWindow(long CenterNs, long StartNs, long EndNs);
public sealed record ExtractedFrame(long PtsNs, long PhoneElapsedNs, int Width, int Height, byte[] Jpeg);
public interface IFrameExtractor
{
    Task<MediaArtifact> DescribeAsync(CancellationToken ct);
    Task ExtractAsync(string objectKey, long expectedBytes, string expectedHash, ClipClock clock,
        IReadOnlyList<FrameWindow> windows, Func<ExtractedFrame, CancellationToken, Task> consume, CancellationToken ct);
}

/// <summary>Local files only, bounded subprocesses, actual frame PTS (never average FPS).</summary>
public sealed class FfmpegFrameExtractor(IObjectStore store, SurveyFrameOptions options, IMediaProcessRunner? processRunner = null) : IFrameExtractor, IDisposable
{
    // Bound the depth of ffmpeg's select expression parser, independently of clip/frame count.
    public const int MaximumSelectionTerms = 64;
    private readonly IMediaProcessRunner runner = processRunner ?? new LocalMediaProcessRunner();
    private readonly SemaphoreSlim slots = new(options.MaximumConcurrentClips);
    public void Dispose() => slots.Dispose();

    public async Task<MediaArtifact> DescribeAsync(CancellationToken ct)
    {
        var ffmpeg = Resolve(options.FfmpegPath);
        var ffprobe = Resolve(options.FfprobePath);
        var version = await runner.RunAsync(ffmpeg, ["-version"], options.TimeoutSeconds, ct);
        var probeVersion = await runner.RunAsync(ffprobe, ["-version"], options.TimeoutSeconds, ct);
        var metadata = JsonSerializer.Serialize(new { ffmpeg, ffprobe, version, probeVersion });
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var file in new[] { ffmpeg, ffprobe, typeof(FfmpegFrameExtractor).Assembly.Location })
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

    public async Task ExtractAsync(string objectKey, long expectedBytes, string expectedHash, ClipClock clock,
        IReadOnlyList<FrameWindow> windows, Func<ExtractedFrame, CancellationToken, Task> consume, CancellationToken ct)
    {
        await slots.WaitAsync(ct);
        var directory = Path.Combine(options.TempRoot, Guid.NewGuid().ToString("N"));
        try
        {
            if (expectedBytes <= 0 || expectedBytes + options.MaximumFrameBytes > options.TemporaryBytesPerClip)
                throw new ProcessingFailure("VIDEO_TEMP_QUOTA", "extractor");
            Directory.CreateDirectory(directory);
            var input = Path.Combine(directory, "clip.mp4");
            using var downloadTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            downloadTimeout.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds));
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
            var probe = await runner.RunAsync(options.FfprobePath,
                ["-v", "error", "-select_streams", "v:0", "-show_entries", "stream=width,height,time_base:stream_side_data=rotation:frame=pts", "-show_frames", "-of", "json", input], options.TimeoutSeconds, ct);
            using var doc = JsonDocument.Parse(probe);
            var streamInfo = doc.RootElement.GetProperty("streams")[0];
            var timeBase = streamInfo.GetProperty("time_base").GetString()!.Split('/');
            int num = int.Parse(timeBase[0], CultureInfo.InvariantCulture), den = int.Parse(timeBase[1], CultureInfo.InvariantCulture);
            if ((long)num * clock.TimeBaseDen != (long)den * clock.TimeBaseNum)
                throw new ProcessingFailure("CLOCK_VIDEO_MAPPING", "video_clock");
            int width = streamInfo.GetProperty("width").GetInt32(), height = streamInfo.GetProperty("height").GetInt32();
            int rotation = 0;
            if (streamInfo.TryGetProperty("side_data_list", out var sideData))
                foreach (var side in sideData.EnumerateArray())
                    if (side.TryGetProperty("rotation", out var angle)) rotation = angle.GetInt32();
            if (rotation % 90 != 0) throw new ProcessingFailure("VIDEO_ROTATION_UNSUPPORTED", "extractor");
            if (Math.Abs(rotation % 180) == 90) (width, height) = (height, width);
            if (width <= 0 || height <= 0 || (long)width * height > 3840L * 2160)
                throw new ProcessingFailure("VIDEO_DIMENSIONS", "extractor");
            var sourcePts = doc.RootElement.GetProperty("frames").EnumerateArray()
                .Select(f => f.GetProperty("pts").GetInt64()).ToArray();
            var pts = sourcePts.Select(value => checked((long)decimal.Round((decimal)value * num * 1_000_000_000 / den, 0, MidpointRounding.AwayFromZero))).ToArray();
            // The phone declares PTS from its encoder (µs); the file stores integer ticks of the time base (1/90000 s =
            // 11.1 µs on Android). They agree within one tick, never exactly — the first real capture was 1 333 ns apart.
            // Exact rational comparison: |difference| ≤ 1e9·num/den without rounding the tick.
            bool WithinTick(long file, long declared) => checked(Math.Abs(file - declared) * den) <= checked(1_000_000_000L * num);
            if (pts.Length == 0 || !WithinTick(pts[0], clock.FirstPtsNs) || !WithinTick(pts[^1], clock.LastPtsNs)
                || pts.Zip(pts.Skip(1)).Any(pair => pair.First >= pair.Second))
                throw new ProcessingFailure("CLOCK_VIDEO_MAPPING", "video_clock");
            var indices = SelectFrames(pts, clock, windows, options.FramesPerSecond);
            if (indices.Length == 0) return;
            var outputPattern = Path.Combine(directory, "frame_%08d.jpg");
            void CheckOutputQuota()
            {
                long totalBytes = expectedBytes;
                foreach (var file in Directory.EnumerateFiles(directory, "frame_*.jpg"))
                {
                    var length = new FileInfo(file).Length;
                    if (length >= options.MaximumFrameBytes) throw new ProcessingFailure("FRAME_SIZE_LIMIT", "extractor");
                    totalBytes += length;
                    if (totalBytes > options.TemporaryBytesPerClip) throw new ProcessingFailure("VIDEO_TEMP_QUOTA", "extractor");
                }
            }
            // Assign the sorted union to windows in chronological order. Overlaps emit each PTS
            // once, including samples selected by a later window inside the current window.
            int next = 0;
            foreach (var window in windows.Where(w => clock.Covers(w.CenterNs)).OrderBy(w => w.StartNs).ThenBy(w => w.EndNs))
            {
                int through = next;
                while (through < indices.Length && clock.ToPhone(pts[indices[through]]) <= window.EndNs) through++;
                foreach (var batch in indices[next..through].Chunk(MaximumSelectionTerms))
                {
                    // Absolute input seek plus copyts retains the original stream time base/PTS,
                    // even for clips whose first PTS is nonzero. Select discards keyframe pre-roll;
                    // noaccurate_seek avoids ffmpeg discarding additional frames at nonzero starts.
                    var seek = Math.Max((decimal)sourcePts[0] * num / den,
                        (decimal)sourcePts[batch[0]] * num / den - 1).ToString("0.#########", CultureInfo.InvariantCulture);
                    var selection = "select=" + string.Join("+", batch.Select(index =>
                        "eq(pts\\," + sourcePts[index].ToString(CultureInfo.InvariantCulture) + ")"));
                    await runner.RunAsync(options.FfmpegPath,
                        ["-nostdin", "-hide_banner", "-loglevel", "error", "-xerror", "-threads", "1", "-seek_timestamp", "1", "-ss", seek, "-noaccurate_seek", "-copyts", "-i", input, "-map", "0:v:0",
                            "-vf", selection, "-fps_mode", "passthrough", "-frames:v", batch.Length.ToString(CultureInfo.InvariantCulture),
                            "-threads", "1", "-q:v", "2", "-pix_fmt", "yuvj420p", "-f", "image2", "-start_number", "1", "-y", outputPattern],
                        options.TimeoutSeconds, ct, CheckOutputQuota);
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
                        var index = batch[i];
                        var bytes = await File.ReadAllBytesAsync(outputs[i], ct);
                        await consume(new(pts[index], clock.ToPhone(pts[index]), width, height, bytes), ct);
                        File.Delete(outputs[i]);
                    }
                }
                next = through;
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        { throw new ProcessingFailure("VIDEO_TIMEOUT", "extractor"); }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or FormatException or IndexOutOfRangeException or OverflowException)
        { throw new ProcessingFailure("VIDEO_INVALID", "extractor"); }
        finally
        {
            try { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
            finally { slots.Release(); }
        }
    }

    public static int[] SelectFrames(long[] pts, ClipClock clock, IReadOnlyList<FrameWindow> windows, double fps)
    {
        var selected = new SortedSet<int>();
        foreach (var window in windows.Where(w => clock.Covers(w.CenterNs)))
        {
            long? last = null;
            for (int i = 0; i < pts.Length; i++)
            {
                var time = clock.ToPhone(pts[i]);
                if (time < window.StartNs || time > window.EndNs) continue;
                if (last is null || (time - last.Value) / 1e9 >= 1 / fps)
                { selected.Add(i); last = time; }
            }
        }
        return selected.ToArray();
    }
}

public interface IMediaProcessRunner
{
    Task<string> RunAsync(string executable, IEnumerable<string> arguments, double timeoutSeconds,
        CancellationToken ct, Action? checkOutput = null);
}

public sealed class LocalMediaProcessRunner : IMediaProcessRunner
{
    public Task<string> RunAsync(string executable, IEnumerable<string> arguments, double timeoutSeconds,
        CancellationToken ct, Action? checkOutput = null) => MediaProcess.RunAsync(executable, arguments, timeoutSeconds, ct, checkOutput);
}

public static class MediaProcess
{
    public static async Task<string> RunAsync(string executable, IEnumerable<string> arguments, double timeoutSeconds, CancellationToken ct, Action? checkOutput = null)
    {
        var info = new ProcessStartInfo(executable) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        using var process = new Process { StartInfo = info };
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        Task? completion = null;
        try
        {
            if (!process.Start()) throw new ProcessingFailure("VIDEO_PROCESS_START", "extractor");
            async Task<string> Read(StreamReader reader, int limit)
            {
                try { return await ReadBounded(reader, limit, timeout.Token); }
                catch { await timeout.CancelAsync(); throw; }
            }
            var stdout = Read(process.StandardOutput, 16 * 1024 * 1024);
            var stderr = Read(process.StandardError, 1024 * 1024);
            var exit = process.WaitForExitAsync(timeout.Token);
            completion = Task.WhenAll(stdout, stderr, exit);
            if (checkOutput is not null)
            {
                while (!completion.IsCompleted)
                {
                    checkOutput();
                    await Task.WhenAny(completion, Task.Delay(10, timeout.Token));
                    timeout.Token.ThrowIfCancellationRequested();
                }
                checkOutput();
            }
            await completion;
            if (process.ExitCode != 0 || (await stderr).Length > 0) throw new ProcessingFailure("VIDEO_INVALID", "extractor");
            return await stdout;
        }
        catch (System.ComponentModel.Win32Exception) { throw new ProcessingFailure("FFMPEG_NOT_INSTALLED", "extractor"); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new ProcessingFailure("VIDEO_TIMEOUT", "extractor"); }
        finally
        {
            try { if (process.Id > 0 && !process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(CancellationToken.None); } }
            catch (InvalidOperationException) { /* Start failed. */ }
            await timeout.CancelAsync();
            if (completion is not null)
                try { await completion; } catch { /* Keep the original quota, timeout or process error. */ }
        }
    }
    private static async Task<string> ReadBounded(StreamReader reader, int limit, CancellationToken ct)
    {
        var result = new StringBuilder(); var buffer = new char[8192]; int read;
        while ((read = await reader.ReadAsync(buffer, ct)) > 0)
        {
            if (result.Length + read > limit) throw new ProcessingFailure("VIDEO_OUTPUT_LIMIT", "extractor");
            result.Append(buffer, 0, read);
        }
        return result.ToString();
    }
}
