using System.Security.Cryptography;
using System.Text.Json;
using LuxMap.Modules.Survey.Processing;
using LuxMap.Modules.Survey.Processing.Frames;
using LuxMap.Shared.Storage;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace LuxMap.Infrastructure.Storage.Tests;

public sealed class SurveyFrameMediaTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Generated_vfr_frame_codes_and_rotation_match_actual_pts(bool rotate)
    {
        var directory = Path.Combine(Path.GetTempPath(), "luxmap-media-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var raw = Path.Combine(directory, "pixels.rgb");
            byte[] pixels = new byte[5 * 64 * 32 * 3];
            for (int n = 0; n < 5; n++)
            for (int y = 0; y < 32; y++)
            for (int x = 0; x < 64; x++)
            {
                int i = ((n * 32 + y) * 64 + x) * 3;
                pixels[i] = (byte)(30 + 30 * n); pixels[i + 1] = (byte)(x < 32 ? 20 : 200); pixels[i + 2] = 40;
            }
            await File.WriteAllBytesAsync(raw, pixels);
            var video = Path.Combine(directory, "vfr.mp4");
            await MediaProcess.RunAsync("ffmpeg", ["-v", "error", "-f", "rawvideo", "-pixel_format", "rgb24", "-video_size", "64x32", "-framerate", "25", "-i", raw,
                "-vf", "settb=1/1000,setpts=if(eq(N\\,0)\\,0\\,if(eq(N\\,1)\\,40\\,if(eq(N\\,2)\\,120\\,if(eq(N\\,3)\\,160\\,280))))",
                "-fps_mode", "vfr", "-enc_time_base", "1:1000", "-c:v", "libx264", "-crf", "0", "-bf", "0", "-video_track_timescale", "1000", "-y", video], 30, default);
            if (rotate)
            {
                var rotated = Path.Combine(directory, "rotated.mp4");
                await MediaProcess.RunAsync("ffmpeg", ["-v", "error", "-display_rotation", "-90", "-i", video, "-c", "copy", "-video_track_timescale", "1000", "-y", rotated], 30, default);
                video = rotated;
            }
            using var probe = JsonDocument.Parse(await MediaProcess.RunAsync("ffprobe", ["-v", "error", "-select_streams", "v:0", "-show_entries", "stream=time_base:frame=pts", "-of", "json", video], 30, default));
            var pts = probe.RootElement.GetProperty("frames").EnumerateArray().Select(f => f.GetProperty("pts").GetInt64() * 1_000_000).ToArray();
            Assert.Equal(new long[] { 0, 40_000_000, 120_000_000, 160_000_000, 280_000_000 }, pts);
            var bytes = await File.ReadAllBytesAsync(video); var store = new ClipStore(bytes);
            var options = new SurveyFrameOptions { TempRoot = Path.Combine(directory, "extract"), FramesPerSecond = 30 };
            var counter = new CountingMediaRunner();
            using var extractor = new FfmpegFrameExtractor(store, options, counter);
            var artifact = await extractor.DescribeAsync(default);
            Assert.Contains("ffmpeg", artifact.Metadata); Assert.Equal(64, artifact.Hash.Length);
            var clock = new ClipClock(0, 0, 280_000_000, 1_000_000_000, 1_280_000_000, 1, 1000);
            var output = new List<ExtractedFrame>();
            await extractor.ExtractAsync("ignored", bytes.Length, Convert.ToHexStringLower(SHA256.HashData(bytes)), clock,
                [new(1_120_000_000, 1_030_000_000, 1_180_000_000), new(1_160_000_000, 1_120_000_000, 1_180_000_000)], (frame, _) => { output.Add(frame); return Task.CompletedTask; }, default);
            Assert.Equal(1, counter.ExtractionStarts);
            Assert.Equal(new long[] { 40_000_000, 120_000_000, 160_000_000 }, output.Select(f => f.PtsNs));
            foreach (var frame in output)
            {
                using var image = Image.Load<Rgb24>(frame.Jpeg);
                Assert.Equal(rotate ? 32 : 64, image.Width); Assert.Equal(rotate ? 64 : 32, image.Height);
                Assert.Equal(image.Width, frame.Width); Assert.Equal(image.Height, frame.Height);
                int number = Array.IndexOf(pts, frame.PtsNs);
                Assert.InRange((int)image[image.Width / 2, image.Height / 2].R, 30 + 30 * number - 8, 30 + 30 * number + 8);
                // Display rotation -90 puts the original left half above the right half.
                var left = rotate ? image[16, 8] : image[8, 16];
                var right = rotate ? image[16, 56] : image[56, 16];
                Assert.InRange((int)left.G, 12, 28); Assert.InRange((int)right.G, 192, 208);
                Assert.Equal(frame.PtsNs + 1_000_000_000, frame.PhoneElapsedNs);
            }
            Assert.Empty(Directory.GetFileSystemEntries(options.TempRoot));
            if (!rotate)
            {
                var truncated = bytes[..(bytes.Length / 2)];
                using var broken = new FfmpegFrameExtractor(new ClipStore(truncated), options);
                Assert.Equal("VIDEO_INVALID", (await Assert.ThrowsAsync<ProcessingFailure>(() => broken.ExtractAsync("clip", truncated.Length,
                    Convert.ToHexStringLower(SHA256.HashData(truncated)), clock, [], (_, _) => Task.CompletedTask, default))).Code);
                Assert.Empty(Directory.GetFileSystemEntries(options.TempRoot));
            }
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task B_frames_sort_packet_pts_to_frame_pts_and_extract_the_matching_images()
    {
        var directory = Path.Combine(Path.GetTempPath(), "luxmap-b-frames-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            const int frameCount = 12, width = 64, height = 32;
            var raw = Path.Combine(directory, "codes.rgb");
            var pixels = new byte[frameCount * width * height * 3];
            for (int n = 0; n < frameCount; n++)
            for (int i = n * width * height * 3; i < (n + 1) * width * height * 3; i += 3)
            {
                pixels[i] = (byte)(30 + 15 * n); pixels[i + 1] = 80; pixels[i + 2] = 40;
            }
            await File.WriteAllBytesAsync(raw, pixels);
            var video = Path.Combine(directory, "b-frames.mp4");
            await MediaProcess.RunAsync("ffmpeg", ["-v", "error", "-f", "rawvideo", "-pixel_format", "rgb24",
                "-video_size", "64x32", "-framerate", "25", "-i", raw, "-c:v", "libx264", "-crf", "18",
                "-bf", "2", "-x264-params", "b-adapt=0:scenecut=0:keyint=30", "-video_track_timescale", "1000", "-y", video], 30, default);
            using var packets = JsonDocument.Parse(await MediaProcess.RunAsync("ffprobe", ["-v", "error", "-select_streams", "v:0",
                "-show_entries", "stream=width,height,time_base:stream_side_data=rotation:packet=pts", "-of", "json=compact=1", video], 30, default));
            using var frames = JsonDocument.Parse(await MediaProcess.RunAsync("ffprobe", ["-v", "error", "-select_streams", "v:0",
                "-show_entries", "frame=pts,pict_type", "-of", "json=compact=1", video], 30, default));
            var packetPts = packets.RootElement.GetProperty("packets").EnumerateArray().Select(p => p.GetProperty("pts").GetInt64()).ToArray();
            var framePts = frames.RootElement.GetProperty("frames").EnumerateArray().Select(f => f.GetProperty("pts").GetInt64()).ToArray();
            Assert.Contains(frames.RootElement.GetProperty("frames").EnumerateArray(), f => f.GetProperty("pict_type").GetString() == "B");
            Assert.False(packetPts.SequenceEqual(framePts)); // The fixture must actually require reordering.
            Assert.Equal(framePts, packetPts.Order().ToArray());
            Assert.Equal(Enumerable.Range(0, frameCount).Select(n => n * 40L), framePts);
            Assert.Equal("1/1000", packets.RootElement.GetProperty("streams")[0].GetProperty("time_base").GetString());

            var bytes = await File.ReadAllBytesAsync(video);
            long lastPtsNs = framePts[^1] * 1_000_000;
            var options = new SurveyFrameOptions { TempRoot = Path.Combine(directory, "extract"), FramesPerSecond = 30 };
            using var extractor = new FfmpegFrameExtractor(new ClipStore(bytes), options);
            var output = new List<ExtractedFrame>();
            await extractor.ExtractAsync("clip", bytes.Length, Convert.ToHexStringLower(SHA256.HashData(bytes)),
                new(0, 0, lastPtsNs, 1_000_000_000, 1_000_000_000 + lastPtsNs, 1, 1000),
                [new(1_000_000_000, 1_000_000_000, 1_000_000_000 + lastPtsNs)],
                (frame, _) => { output.Add(frame); return Task.CompletedTask; }, default);
            Assert.Equal(framePts.Select(p => p * 1_000_000), output.Select(f => f.PtsNs));
            for (int n = 0; n < output.Count; n++)
            {
                using var image = Image.Load<Rgb24>(output[n].Jpeg);
                Assert.Equal(width, image.Width); Assert.Equal(height, image.Height);
                Assert.Equal(image.Width, output[n].Width); Assert.Equal(image.Height, output[n].Height);
                Assert.InRange((int)image[width / 2, height / 2].R, 30 + 15 * n - 8, 30 + 15 * n + 8);
                Assert.Equal(1_000_000_000 + output[n].PtsNs, output[n].PhoneElapsedNs);
            }
            Assert.Empty(Directory.GetFileSystemEntries(options.TempRoot));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData("[{}, {\"pts\":40}]", "VIDEO_INVALID")]
    [InlineData("[{\"pts\":0}, {\"pts\":40}, {\"pts\":40}, {\"pts\":120}]", "CLOCK_VIDEO_MAPPING")]
    [InlineData("[]", "CLOCK_VIDEO_MAPPING")]
    public async Task Missing_duplicate_or_empty_packet_pts_fail_before_consumption(string packets, string code)
    {
        var directory = Path.Combine(Path.GetTempPath(), "luxmap-packet-pts-" + Guid.NewGuid().ToString("N"));
        byte[] clip = [0, 1, 2, 3];
        int consumed = 0;
        try
        {
            using var extractor = new FfmpegFrameExtractor(new ClipStore(clip), new() { TempRoot = directory }, new PacketProbeRunner(packets));
            var error = await Assert.ThrowsAsync<ProcessingFailure>(() => extractor.ExtractAsync("clip", clip.Length,
                Convert.ToHexStringLower(SHA256.HashData(clip)), new(0, 0, 120_000_000, 0, 120_000_000, 1, 1000),
                [new(40_000_000, 0, 120_000_000)], (_, _) => { consumed++; return Task.CompletedTask; }, default));
            Assert.Equal(code, error.Code); Assert.Equal(0, consumed);
            Assert.Empty(Directory.GetFileSystemEntries(directory));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    private sealed class PacketProbeRunner(string packets) : IMediaProcessRunner
    {
        public Task<string> RunAsync(string executable, IEnumerable<string> arguments, double timeoutSeconds,
            CancellationToken ct, Action? checkOutput = null)
        {
            Assert.Contains("-show_entries", arguments); // Invalid PTS must fail before extraction starts.
            return Task.FromResult("{\"streams\":[{\"width\":64,\"height\":32,\"time_base\":\"1/1000\"}],\"packets\":" + packets + "}");
        }
    }

    /// <summary>
    /// A phone declares PTS from its encoder (microseconds); the MP4 stores integer ticks of 1/90000 s (11.1 µs). They
    /// agree within one tick, never exactly: the first real capture (SM-A075F, 02/10/2026) declared a last PTS of
    /// 29 908 732 000 ns while the file holds 29 908 733 333 ns. Within a tick is the same frame; beyond it is not.
    /// </summary>
    [Theory]
    [InlineData(1_333, true)] [InlineData(-1_333, true)] [InlineData(11_111, true)]
    [InlineData(11_112, false)] [InlineData(-20_000, false)]
    public async Task Declared_pts_within_one_container_tick_of_the_file_is_the_same_frame(long declaredOffsetNs, bool accepted)
    {
        var directory = Path.Combine(Path.GetTempPath(), "luxmap-tick-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var raw = Path.Combine(directory, "pixels.rgb");
            await File.WriteAllBytesAsync(raw, Enumerable.Repeat((byte)90, 5 * 32 * 16 * 3).ToArray());
            var video = Path.Combine(directory, "phone-like.mp4");
            await MediaProcess.RunAsync("ffmpeg", ["-v", "error", "-f", "rawvideo", "-pixel_format", "rgb24", "-video_size", "32x16",
                "-framerate", "30", "-i", raw, "-c:v", "libx264", "-crf", "0", "-bf", "0", "-video_track_timescale", "90000", "-y", video], 30, default);
            using var probe = JsonDocument.Parse(await MediaProcess.RunAsync("ffprobe", ["-v", "error", "-select_streams", "v:0",
                "-show_entries", "stream=time_base:frame=pts", "-of", "json", video], 30, default));
            Assert.Equal("1/90000", probe.RootElement.GetProperty("streams")[0].GetProperty("time_base").GetString());
            var filePts = probe.RootElement.GetProperty("frames").EnumerateArray()
                .Select(f => (long)decimal.Round(f.GetProperty("pts").GetInt64() * 1_000_000_000m / 90000, 0, MidpointRounding.AwayFromZero)).ToArray();
            Assert.NotEqual(0, filePts[^1] % 1000); // the file's own ticks are not whole microseconds: exact equality was never possible

            const long sensorStart = 112_160_279_118_000; // magnitude of the real capture's elapsedRealtimeNanos
            long declaredLast = filePts[^1] + declaredOffsetNs;
            var clock = new ClipClock(0, filePts[0], declaredLast, sensorStart, sensorStart + declaredLast - filePts[0], 1, 90000);
            var bytes = await File.ReadAllBytesAsync(video);
            var options = new SurveyFrameOptions { TempRoot = Path.Combine(directory, "extract"), FramesPerSecond = 30 };
            using var extractor = new FfmpegFrameExtractor(new ClipStore(bytes), options);
            var output = new List<ExtractedFrame>();
            Task Run() => extractor.ExtractAsync("clip", bytes.Length, Convert.ToHexStringLower(SHA256.HashData(bytes)), clock,
                [new(sensorStart + filePts[2], sensorStart, sensorStart + filePts[^1])], (frame, _) => { output.Add(frame); return Task.CompletedTask; }, default);

            if (!accepted)
            {
                Assert.Equal("CLOCK_VIDEO_MAPPING", (await Assert.ThrowsAsync<ProcessingFailure>(Run)).Code);
                return;
            }
            await Run();
            // Which frames is SelectFrames' business (tested on its own); here: frames come out, and each is a real file frame.
            Assert.NotEmpty(output);
            Assert.All(output, f => Assert.Contains(f.PtsNs, filePts));
            // Phone time comes from the file's real PTS on the declared affine clock, never from the declared last value.
            Assert.All(output, f => Assert.Equal(sensorStart + f.PtsNs, f.PhoneElapsedNs));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Long_vfr_clip_extracts_over_300_frames_in_window_batches_with_original_pts(bool oversizedWindow)
    {
        var directory = Path.Combine(Path.GetTempPath(), "luxmap-long-vfr-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            // 50 seconds at nominal 30 fps, alternating 28.33/38.33 ms intervals.
            // Twelve large monochrome blocks encode the original frame number independently of PTS.
            const int frameCount = 1500, width = 96, height = 64;
            var raw = Path.Combine(directory, "codes.rgb");
            await using (var output = File.Create(raw))
            {
                var pixels = new byte[width * height * 3];
                for (int n = 0; n < frameCount; n++)
                {
                    for (int y = 0; y < height; y++)
                    for (int x = 0; x < width; x++)
                    {
                        int bit = y / 16 * 3 + x / 32;
                        byte color = (n & (1 << bit)) == 0 ? (byte)20 : (byte)230;
                        int offset = (y * width + x) * 3;
                        pixels[offset] = pixels[offset + 1] = pixels[offset + 2] = color;
                    }
                    await output.WriteAsync(pixels);
                }
            }
            var video = Path.Combine(directory, "long-vfr.mp4");
            await MediaProcess.RunAsync("ffmpeg", ["-v", "error", "-f", "rawvideo", "-pixel_format", "rgb24",
                "-video_size", "96x64", "-framerate", "30", "-i", raw,
                "-vf", "settb=1/30000,setpts=90000+N*1000+mod(N\\,2)*150", "-copyts",
                "-fps_mode", "vfr", "-enc_time_base", "1:30000", "-c:v", "libx264", "-crf", "0",
                "-g", "30", "-bf", "0", "-video_track_timescale", "30000", "-y", video], 60, default);
            using var probe = JsonDocument.Parse(await MediaProcess.RunAsync("ffprobe", ["-v", "error",
                "-select_streams", "v:0", "-show_entries", "stream=time_base:frame=pts", "-of", "json", video], 30, default));
            Assert.Equal("1/30000", probe.RootElement.GetProperty("streams")[0].GetProperty("time_base").GetString());
            var ticks = probe.RootElement.GetProperty("frames").EnumerateArray().Select(f => f.GetProperty("pts").GetInt64()).ToArray();
            Assert.Equal(frameCount, ticks.Length);
            Assert.Equal(90000, ticks[0]); // Nonzero start catches seek relative to the wrong timeline.
            Assert.True((ticks[^1] - ticks[0]) / 30000d >= 45);
            Assert.True(ticks.Zip(ticks.Skip(1)).Select(x => x.Second - x.First).Distinct().Count() > 1);
            var pts = ticks.Select(t => (long)decimal.Round(t * 1_000_000_000m / 30000, 0, MidpointRounding.AwayFromZero)).ToArray();
            var clock = new ClipClock(0, pts[0], pts[^1], 10_000_000_000, 10_000_000_000 + pts[^1] - pts[0], 1, 30000);
            var windows = Enumerable.Range(0, 18).Select(n =>
            {
                long start = 10_500_000_000 + n * 2_700_000_000L;
                return new FrameWindow(start + 1_200_000_000, start, start + 2_400_000_000);
            }).ToArray();
            if (oversizedWindow)
                windows = [new(clock.ToPhone(pts[750]), clock.ToPhone(pts[0]), clock.ToPhone(pts[^1]))];
            // Independent expected sequence: sample actual PTS at >= 1/8 s within each window.
            var expected = new List<int>();
            foreach (var window in windows)
            {
                long? last = null;
                for (int n = 0; n < pts.Length; n++)
                {
                    long phone = clock.ToPhone(pts[n]);
                    if (phone < window.StartNs || phone > window.EndNs || last is { } previous && phone - previous < 125_000_000) continue;
                    expected.Add(n); last = phone;
                }
            }
            Assert.True(expected.Count >= 300);
            var bytes = await File.ReadAllBytesAsync(video);
            var options = new SurveyFrameOptions { TempRoot = Path.Combine(directory, "extract"), FramesPerSecond = 8 };
            var counter = new CountingMediaRunner();
            using var extractor = new FfmpegFrameExtractor(new ClipStore(bytes), options, counter);
            var actual = new List<long>();
            await extractor.ExtractAsync("clip", bytes.Length, Convert.ToHexStringLower(SHA256.HashData(bytes)), clock, windows,
                (frame, _) =>
                {
                    using var image = Image.Load<Rgb24>(frame.Jpeg);
                    int number = 0;
                    for (int bit = 0; bit < 12; bit++)
                        if (image[bit % 3 * 32 + 16, bit / 3 * 16 + 8].R > 128) number |= 1 << bit;
                    Assert.Equal(expected[actual.Count], number);
                    Assert.Equal(pts[number], frame.PtsNs);
                    Assert.Equal(clock.ToPhone(pts[number]), frame.PhoneElapsedNs);
                    actual.Add(frame.PtsNs);
                    return Task.CompletedTask;
                }, default);
            Assert.Equal(expected.Select(n => pts[n]), actual);
            Assert.Equal(oversizedWindow ? (expected.Count + 63) / 64 : windows.Length, counter.ExtractionStarts);
            Assert.All(counter.Selections, selection =>
            {
                Assert.StartsWith("select=eq(pts", selection);
                Assert.InRange(selection.Split('+').Length, 1, 64);
            });
            Assert.Empty(Directory.GetFileSystemEntries(options.TempRoot));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task Large_frame_probe_stays_bounded_and_extracts_the_last_frame()
    {
        var directory = Path.Combine(Path.GetTempPath(), "luxmap-large-probe-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            // 30,000 tiny frames: the old -show_frames command emits over 16 MiB even at 64x64.
            // The compressed clip is under 1 MiB; no large fixture or real-time encoding is needed.
            const int frameCount = 30_000;
            var video = Path.Combine(directory, "long.mp4");
            await MediaProcess.RunAsync("ffmpeg", ["-v", "error", "-f", "lavfi", "-i", "color=size=64x64:rate=30",
                "-frames:v", frameCount.ToString(System.Globalization.CultureInfo.InvariantCulture), "-c:v", "libx264",
                "-preset", "ultrafast", "-bf", "0", "-video_track_timescale", "30000", "-y", video], 30, default);
            var bytes = await File.ReadAllBytesAsync(video);
            Assert.InRange(bytes.Length, 1, 1024 * 1024);
            // Prove the fixture triggers the original failure through the real bounded process runner.
            var oldProbe = await Assert.ThrowsAsync<ProcessingFailure>(() => MediaProcess.RunAsync("ffprobe",
                ["-v", "error", "-select_streams", "v:0", "-show_entries",
                    "stream=width,height,time_base:stream_side_data=rotation:frame=pts", "-show_frames", "-of", "json", video], 30, default));
            Assert.Equal("VIDEO_OUTPUT_LIMIT", oldProbe.Code);

            long lastPts = (long)decimal.Round((frameCount - 1) * 1_000_000_000m / 30, 0, MidpointRounding.AwayFromZero);
            var clock = new ClipClock(0, 0, lastPts, 0, lastPts, 1, 30000);
            var options = new SurveyFrameOptions { TempRoot = Path.Combine(directory, "extract") };
            using var extractor = new FfmpegFrameExtractor(new ClipStore(bytes), options);
            var output = new List<ExtractedFrame>();
            await extractor.ExtractAsync("clip", bytes.Length, Convert.ToHexStringLower(SHA256.HashData(bytes)), clock,
                [new(lastPts, lastPts, lastPts)], (frame, _) => { output.Add(frame); return Task.CompletedTask; }, default);
            var last = Assert.Single(output);
            Assert.Equal(lastPts, last.PtsNs); Assert.Equal(lastPts, last.PhoneElapsedNs);
            using var image = Image.Load<Rgb24>(last.Jpeg);
            Assert.Equal(64, image.Width); Assert.Equal(64, image.Height);
            Assert.Equal(image.Width, last.Width); Assert.Equal(image.Height, last.Height);
            Assert.Empty(Directory.GetFileSystemEntries(options.TempRoot));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task Corrupt_clip_and_download_cancellation_leave_no_temporary_files()
    {
        var directory = Path.Combine(Path.GetTempPath(), "luxmap-media-cleanup-" + Guid.NewGuid().ToString("N"));
        var options = new SurveyFrameOptions { TempRoot = directory };
        byte[] corrupt = [0, 1, 2, 3];
        try
        {
            using var extractor = new FfmpegFrameExtractor(new ClipStore(corrupt), options);
            var error = await Assert.ThrowsAsync<ProcessingFailure>(() => extractor.ExtractAsync("clip", 4, Convert.ToHexStringLower(SHA256.HashData(corrupt)),
                new(0, 0, 1, 0, 1, 1, 1), [], (_, _) => Task.CompletedTask, default));
            Assert.Equal("VIDEO_INVALID", error.Code); Assert.Empty(Directory.GetFileSystemEntries(directory));
            using var cancellation = new CancellationTokenSource(40);
            using var slow = new FfmpegFrameExtractor(new ClipStore(corrupt, true), options);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => slow.ExtractAsync("clip", 4, "unused",
                new(0, 0, 1, 0, 1, 1, 1), [], (_, _) => Task.CompletedTask, cancellation.Token));
            Assert.Empty(Directory.GetFileSystemEntries(directory));
            using var timeout = new FfmpegFrameExtractor(new ClipStore(corrupt, true), new() { TempRoot = directory, TimeoutSeconds = .04 });
            Assert.Equal("VIDEO_TIMEOUT", (await Assert.ThrowsAsync<ProcessingFailure>(() => timeout.ExtractAsync("clip", 4, "unused",
                new(0, 0, 1, 0, 1, 1, 1), [], (_, _) => Task.CompletedTask, default))).Code);
            Assert.Empty(Directory.GetFileSystemEntries(directory));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task Temporary_quota_is_checked_before_downloading()
    {
        var root = Path.Combine(Path.GetTempPath(), "luxmap-quota-" + Guid.NewGuid().ToString("N"));
        using var extractor = new FfmpegFrameExtractor(new ClipStore([], true),
            new() { TempRoot = root, TemporaryBytesPerClip = 100, MaximumFrameBytes = 10 });
        var error = await Assert.ThrowsAsync<ProcessingFailure>(() => extractor.ExtractAsync("clip", 100, "unused",
            new(0, 0, 1, 0, 1, 1, 1), [], (_, _) => Task.CompletedTask, default));
        Assert.Equal("VIDEO_TEMP_QUOTA", error.Code); Assert.False(Directory.Exists(root));
    }

    [Fact]
    public async Task Process_timeout_and_missing_binary_are_explicit_errors()
    {
        var ex = await Assert.ThrowsAsync<ProcessingFailure>(() => MediaProcess.RunAsync("ffmpeg",
            ["-v", "error", "-re", "-f", "lavfi", "-i", "color=size=64x32:rate=1", "-f", "null", "-"], .1, default));
        Assert.Equal("VIDEO_TIMEOUT", ex.Code);
        using var stop = new CancellationTokenSource(100);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => MediaProcess.RunAsync("ffmpeg",
            ["-v", "error", "-re", "-f", "lavfi", "-i", "color=size=64x32:rate=1", "-f", "null", "-"], 10, stop.Token));
        Assert.Equal("FFMPEG_NOT_INSTALLED", (await Assert.ThrowsAsync<ProcessingFailure>(() => MediaProcess.RunAsync("/nonexistent/luxmap-ffmpeg", [], 1, default))).Code);
    }

    [Theory]
    [InlineData("missing", "FRAME_COUNT_MISMATCH")]
    [InlineData("gap", "FRAME_SEQUENCE_INVALID")]
    [InlineData("quota", "VIDEO_TEMP_QUOTA")]
    [InlineData("large", "FRAME_SIZE_LIMIT")]
    public async Task Invalid_batch_is_rejected_before_consumption_and_cleans_files(string mode, string code)
    {
        var root = Path.Combine(Path.GetTempPath(), "luxmap-batch-" + Guid.NewGuid().ToString("N"));
        byte[] clip = [0, 1, 2, 3];
        var options = new SurveyFrameOptions { TempRoot = root, FramesPerSecond = 30,
            MaximumFrameBytes = 50, TemporaryBytesPerClip = 100 };
        int consumed = 0;
        try
        {
            using var extractor = new FfmpegFrameExtractor(new ClipStore(clip), options, new InvalidBatchRunner(mode));
            var error = await Assert.ThrowsAsync<ProcessingFailure>(() => extractor.ExtractAsync("clip", clip.Length,
                Convert.ToHexStringLower(SHA256.HashData(clip)), new(0, 0, 120_000_000, 0, 120_000_000, 1, 1000),
                [new(40_000_000, 0, 120_000_000)], (_, _) => { consumed++; return Task.CompletedTask; }, default));
            Assert.Equal(code, error.Code);
            Assert.Equal(0, consumed);
            Assert.Empty(Directory.GetFileSystemEntries(root));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private sealed class InvalidBatchRunner(string mode) : IMediaProcessRunner
    {
        public async Task<string> RunAsync(string executable, IEnumerable<string> arguments, double timeoutSeconds,
            CancellationToken ct, Action? checkOutput = null)
        {
            var args = arguments.ToArray();
            if (args.Contains("-show_entries"))
                return """{"streams":[{"width":64,"height":32,"time_base":"1/1000"}],"packets":[{"pts":0},{"pts":40},{"pts":120}]}""";
            int count = mode == "missing" ? 2 : 3;
            for (int i = 1; i <= count; i++)
            {
                int number = mode == "gap" && i == 3 ? 4 : i;
                var path = args[^1].Replace("%08d", number.ToString("D8", System.Globalization.CultureInfo.InvariantCulture));
                await File.WriteAllBytesAsync(path, new byte[mode == "large" ? 51 : mode == "quota" ? 40 : 1], ct);
                checkOutput?.Invoke();
            }
            return "";
        }
    }

    private sealed class CountingMediaRunner : IMediaProcessRunner
    {
        public int ExtractionStarts { get; private set; }
        public List<string> Selections { get; } = [];
        public Task<string> RunAsync(string executable, IEnumerable<string> arguments, double timeoutSeconds,
            CancellationToken ct, Action? checkOutput = null)
        {
            var args = arguments.ToArray();
            if (args.Contains("-vf"))
            {
                ExtractionStarts++;
                Selections.Add(args[Array.IndexOf(args, "-vf") + 1]);
                Assert.Contains("-copyts", args);
                Assert.True(Array.IndexOf(args, "-ss") >= 0 && Array.IndexOf(args, "-ss") < Array.IndexOf(args, "-i"));
            }
            return new LocalMediaProcessRunner().RunAsync(executable, args, timeoutSeconds, ct, checkOutput);
        }
    }

    private sealed class ClipStore(byte[] bytes, bool block = false) : IObjectStore
    {
        public async Task<Stream> OpenAsync(StorageBucket bucket, string key, CancellationToken cancellationToken = default)
        { if (block) await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); return new MemoryStream(bytes, false); }
        public Task<bool> ExistsAsync(StorageBucket bucket, string key, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<StoredImage> StoreImageAsync(StorageBucket bucket, string id, Stream content, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<StoredObject> StoreStreamAsync(StorageBucket bucket, string key, Stream content, StreamUpload upload, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
