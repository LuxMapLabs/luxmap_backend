using System.Diagnostics;
using System.Text;
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
    public const int MaximumSelectionTerms = FfmpegClipTools.MaximumSelectionTerms;
    private readonly FfmpegClipTools tools = new(store, new(options.FfmpegPath, options.FfprobePath, options.TempRoot,
        options.TemporaryBytesPerClip, options.MaximumFrameBytes, options.TimeoutSeconds), processRunner ?? new LocalMediaProcessRunner());
    private readonly SemaphoreSlim slots = new(options.MaximumConcurrentClips);
    public void Dispose() => slots.Dispose();

    public Task<MediaArtifact> DescribeAsync(CancellationToken ct) => tools.DescribeAsync(ct);

    public async Task ExtractAsync(string objectKey, long expectedBytes, string expectedHash, ClipClock clock,
        IReadOnlyList<FrameWindow> windows, Func<ExtractedFrame, CancellationToken, Task> consume, CancellationToken ct)
    {
        await slots.WaitAsync(ct);
        try
        {
            await tools.WithWorkDirectoryAsync<object?>(async directory =>
            {
                var input = await tools.DownloadAsync(objectKey, expectedBytes, expectedHash, directory, ct);
                var probe = await tools.ProbeAsync(input, ct, (num, den) =>
                {
                    if ((long)num * clock.TimeBaseDen != (long)den * clock.TimeBaseNum)
                        throw new ProcessingFailure("CLOCK_VIDEO_MAPPING", "video_clock");
                });
                var pts = probe.PtsNs;
                // The phone declares PTS from its encoder (µs); the file stores integer ticks of the time base (1/90000 s =
                // 11.1 µs on Android). They agree within one tick, never exactly — the first real capture was 1 333 ns apart.
                // Exact rational comparison: |difference| ≤ 1e9·num/den without rounding the tick.
                bool WithinTick(long file, long declared) => checked(Math.Abs(file - declared) * probe.Den) <= checked(1_000_000_000L * probe.Num);
                if (pts.Length == 0 || !WithinTick(pts[0], clock.FirstPtsNs) || !WithinTick(pts[^1], clock.LastPtsNs)
                    || pts.Zip(pts.Skip(1)).Any(pair => pair.First >= pair.Second))
                    throw new ProcessingFailure("CLOCK_VIDEO_MAPPING", "video_clock");
                var indices = SelectFrames(pts, clock, windows, options.FramesPerSecond);
                if (indices.Length == 0) return null;
                // Assign the sorted union to windows in chronological order. Overlaps emit each PTS
                // once, including samples selected by a later window inside the current window.
                int next = 0;
                foreach (var window in windows.Where(w => clock.Covers(w.CenterNs)).OrderBy(w => w.StartNs).ThenBy(w => w.EndNs))
                {
                    int through = next;
                    while (through < indices.Length && clock.ToPhone(pts[indices[through]]) <= window.EndNs) through++;
                    foreach (var batch in indices[next..through].Chunk(MaximumSelectionTerms))
                        await tools.ExtractBatchAsync(input, directory, expectedBytes, probe, batch,
                            (index, bytes, token) => consume(new(pts[index], clock.ToPhone(pts[index]), probe.Width, probe.Height, bytes), token), ct);
                    next = through;
                }
                return null;
            }, ct);
        }
        finally { slots.Release(); }
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
