using System.Net;
using System.Security.Cryptography;
using LuxMap.Modules.Survey.Processing;
using LuxMap.Modules.Survey.Processing.Frames;
using LuxMap.Shared.Http;
using LuxMap.Shared.Media;
using LuxMap.Shared.Storage;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace LuxMap.Infrastructure.Storage.Tests;

/// <summary>POLE-VIDEO PV-b: which frames of a one-lamp clip are cut, and how a bad clip is refused.</summary>
public sealed class PoleVideoSamplerTests
{
    // ---- planning: pure, on real display times ------------------------------------------------------------------------------

    private static long[] Uniform(double seconds, int fps) => Enumerable.Range(0, (int)Math.Round(seconds * fps)).Select(n => n * (1_000_000_000L / fps)).ToArray();

    [Fact]
    public void A_short_clip_is_sampled_from_its_start_one_frame_per_second_at_the_middle_of_each_second()
    {
        var pts = Uniform(5, 25);
        var picked = ClipSamplePlanner.Select(pts, new());
        // 5 s at 1 fps = 5 frames, no leading skip (5 s is not above 6 s), each at the middle of its second.
        Assert.Equal([12, 37, 62, 87, 112], picked);
        Assert.Equal(5_000_000_000, ClipSamplePlanner.DurationNs(pts));
    }

    [Fact]
    public void A_long_clip_skips_the_first_second_and_is_capped_at_the_maximum_frames_spread_evenly()
    {
        var pts = Uniform(30, 30);
        var picked = ClipSamplePlanner.Select(pts, new());
        Assert.Equal(12, picked.Length);
        Assert.True(picked.SequenceEqual(picked.Order()));
        Assert.True(picked[0] >= 30, "the first second is skipped");
        var gaps = picked.Zip(picked.Skip(1), (a, b) => b - a).ToArray();
        Assert.InRange(gaps.Max() - gaps.Min(), 0, 2);
        Assert.True(picked[^1] > pts.Length - 3 * 30, "the last frame is near the end");
    }

    [Fact]
    public void The_leading_skip_applies_only_above_the_threshold()
    {
        Assert.Equal(0.5, ClipSamplePlanner.Select(Uniform(6, 20), new())[0] / 20.0, 1);   // 6 s is not above 6 s: starts at 0.5 s
        Assert.True(ClipSamplePlanner.Select(Uniform(7, 20), new())[0] / 20.0 >= 1.0);      // 7 s skips the first second
    }

    [Fact]
    public void Variable_frame_rate_picks_the_frame_nearest_each_target_by_display_time_and_never_repeats_one()
    {
        // Dense for 1 s, then sparse: the clip lasts 3.9 + 0.9 = 4.8 s, so 5 targets at 0.48, 1.44, 2.4, 3.36, 4.32 s.
        var pts = new long[] { 0, 100, 200, 300, 400, 500, 600, 700, 800, 900, 1_000, 2_000, 3_000, 3_900 }.Select(ms => ms * 1_000_000).ToArray();
        var picked = ClipSamplePlanner.Select(pts, new());
        Assert.Equal(picked.Distinct().Count(), picked.Length);
        Assert.Equal([5, 10, 11, 12, 13], picked);     // each the nearest displayed frame; none picked twice
    }

    [Theory]
    [InlineData(2.9, "VIDEO_TOO_SHORT")]
    [InlineData(30.6, "VIDEO_TOO_LONG")]
    public void A_clip_outside_the_allowed_length_is_refused_with_a_400_that_says_how_long_it_was(double seconds, string code)
    {
        var error = Assert.Throws<LuxMapException>(() => ClipSamplePlanner.Select(Uniform(seconds, 10), new()));
        Assert.Equal(code, error.Code);
        Assert.Equal(HttpStatusCode.BadRequest, error.StatusCode);
        Assert.Equal((int)Math.Round(seconds * 1000), error.Details!["duration_ms"]);
    }

    [Fact]
    public void The_clip_limit_has_a_grace_for_a_clip_recorded_as_30_seconds_and_a_single_frame_is_too_short()
    {
        Assert.NotEmpty(ClipSamplePlanner.Select(Uniform(30.4, 10), new()));
        Assert.Equal("VIDEO_TOO_SHORT", Assert.Throws<LuxMapException>(() => ClipSamplePlanner.Select([0], new())).Code);
    }

    [Fact]
    public void Defaults_are_valid_and_impossible_settings_are_not()
    {
        Assert.True(new PoleVideoOptions().IsValid());
        Assert.False(new PoleVideoOptions { MinimumSeconds = 40 }.IsValid());
        Assert.False(new PoleVideoOptions { MaximumFrames = 0 }.IsValid());
        Assert.False(new PoleVideoOptions { SkipLeadingSeconds = 7 }.IsValid());
        Assert.False(new PoleVideoOptions { FramesPerSecond = double.NaN }.IsValid());
        Assert.False(new PoleVideoOptions { TemporaryBytesPerClip = 1024, MaximumFrameBytes = 2048 }.IsValid());
    }

    // ---- the sampler on real clips -------------------------------------------------------------------------------------------

    private sealed class Clip(string directory, byte[] bytes)
    {
        public byte[] Bytes { get; } = bytes;
        public string Hash => Convert.ToHexStringLower(SHA256.HashData(Bytes));
        public string Directory { get; } = directory;
    }

    /// <summary>Luma rises 20 per second of clip time, so a frame's brightness says where in the clip it was cut.</summary>
    private static async Task<Clip> Generate(string directory, double seconds, int fps = 25, string encode = "-c:v libx264 -pix_fmt yuv420p", string? rotate = null)
    {
        var file = Path.Combine(directory, "clip-" + Guid.NewGuid().ToString("N") + ".mp4");
        string[] args = ["-v", "error", "-f", "lavfi", "-i",
            $"color=c=black:size=64x32:rate={fps}:duration={seconds.ToString(System.Globalization.CultureInfo.InvariantCulture)},geq=lum='min(255,T*20)':cb=128:cr=128",
            .. encode.Split(' '), "-y", file];
        await MediaProcess.RunAsync("ffmpeg", args, 120, default);
        if (rotate is not null)
        {
            var rotated = file + ".rot.mp4";
            await MediaProcess.RunAsync("ffmpeg", ["-v", "error", "-display_rotation", rotate, "-i", file, "-c", "copy", "-y", rotated], 60, default);
            file = rotated;
        }
        return new(directory, await File.ReadAllBytesAsync(file));
    }

    private static FfmpegClipFrameSampler Sampler(string directory, IObjectStore? store = null, Action<PoleVideoOptions>? tune = null, SurveyFrameOptions? tools = null,
        byte[]? bytes = null)
    {
        var options = new PoleVideoOptions { TempRoot = Path.Combine(directory, "work") };
        tune?.Invoke(options);
        return new(store ?? new ClipStore(bytes!), tools ?? new SurveyFrameOptions(), Options.Create(options));
    }

    private static async Task<(ClipInfo Info, List<SampledFrame> Frames)> Run(FfmpegClipFrameSampler sampler, Clip clip)
    {
        var frames = new List<SampledFrame>();
        var info = await sampler.SampleAsync(new("pole-videos/test.mp4", clip.Bytes.Length, clip.Hash), (frame, _) => { frames.Add(frame); return Task.CompletedTask; }, default);
        return (info, frames);
    }

    private static string NewDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "luxmap-pole-video-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void AssertWorkDirectoryEmpty(string directory)
    {
        var work = Path.Combine(directory, "work");
        Assert.True(!Directory.Exists(work) || Directory.GetFileSystemEntries(work).Length == 0);
    }

    [Fact]
    public async Task Frames_are_cut_in_time_order_across_the_clip_and_the_work_directory_is_left_empty()
    {
        var directory = NewDirectory();
        try
        {
            var clip = await Generate(directory, 10);
            using var sampler = Sampler(directory, bytes: clip.Bytes);
            var (info, frames) = await Run(sampler, clip);

            // 10 s is above 6 s: the first second is skipped; 9 usable seconds at 1 fps = 9 frames at 1.5, 2.5 ... 9.5 s.
            Assert.Equal(9, frames.Count);
            Assert.Equal(9, info.SampledFrames);
            Assert.InRange(info.DurationMs, 9_950, 10_050);
            Assert.Equal(Enumerable.Range(0, 9), frames.Select(f => f.Number));
            for (var n = 0; n < frames.Count; n++)
            {
                Assert.InRange(frames[n].OffsetMs, 1_500 + n * 1_000 - 41, 1_500 + n * 1_000 + 41);   // within one 25 fps frame
                using var image = Image.Load<Rgb24>(frames[n].Jpeg);
                Assert.Equal((64, 32), (image.Width, image.Height));
                Assert.Equal((64, 32), (frames[n].Width, frames[n].Height));
                // Y rises 20 per second as a limited-range code; the decoded RGB is (Y - 16) * 255 / 219.
                var expected = (frames[n].OffsetMs / 1000.0 * 20 - 16) * 255 / 219;
                Assert.InRange((int)image[32, 16].R, (int)expected - 14, (int)expected + 14);
            }
            Assert.Equal((64, 32), (info.Width, info.Height));
            Assert.False(info.Hdr);
            AssertWorkDirectoryEmpty(directory);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task A_thirty_second_clip_gives_the_maximum_number_of_frames_spread_over_it()
    {
        var directory = NewDirectory();
        try
        {
            var clip = await Generate(directory, 30, fps: 5);
            using var sampler = Sampler(directory, bytes: clip.Bytes);
            var (info, frames) = await Run(sampler, clip);
            Assert.Equal(12, frames.Count);
            Assert.InRange(info.DurationMs, 29_900, 30_100);
            Assert.True(frames[0].OffsetMs >= 1_000 && frames[^1].OffsetMs > 28_000);
            Assert.True(frames.Zip(frames.Skip(1)).All(pair => pair.Second.OffsetMs > pair.First.OffsetMs));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task A_display_rotated_clip_reports_and_cuts_the_rotated_size()
    {
        var directory = NewDirectory();
        try
        {
            var clip = await Generate(directory, 4, rotate: "-90");
            using var sampler = Sampler(directory, bytes: clip.Bytes);
            var (info, frames) = await Run(sampler, clip);
            Assert.Equal((32, 64), (info.Width, info.Height));
            Assert.All(frames, frame =>
            {
                using var image = Image.Load<Rgb24>(frame.Jpeg);
                Assert.Equal((32, 64), (image.Width, image.Height));
            });
        }
        finally { Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData("-vf setparams=color_trc=arib-std-b67:colorspace=bt2020nc:color_primaries=bt2020 -c:v libx265 -pix_fmt yuv420p10le -x265-params log-level=error", "arib-std-b67", true)]
    [InlineData("-vf setparams=color_trc=smpte2084:colorspace=bt2020nc:color_primaries=bt2020 -c:v libx265 -pix_fmt yuv420p10le -x265-params log-level=error", "smpte2084", true)]
    [InlineData("-vf setparams=color_trc=bt709:colorspace=bt709:color_primaries=bt709 -c:v libx264 -pix_fmt yuv420p", "bt709", false)]
    public async Task An_hdr_clip_is_accepted_and_flagged_and_an_sdr_clip_is_not(string encode, string transfer, bool hdr)
    {
        var directory = NewDirectory();
        try
        {
            var clip = await Generate(directory, 4, encode: encode);
            using var sampler = Sampler(directory, bytes: clip.Bytes);
            var (info, frames) = await Run(sampler, clip);
            Assert.Equal(transfer, info.ColorTransfer);
            Assert.Equal(hdr, info.Hdr);
            Assert.NotEmpty(frames);
            Assert.Equal(hdr ? "yuv420p10le" : "yuv420p", info.PixelFormat);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData(2, "VIDEO_TOO_SHORT")]
    [InlineData(32, "VIDEO_TOO_LONG")]
    public async Task A_clip_of_the_wrong_length_is_refused_after_probing_with_nothing_left_behind(double seconds, string code)
    {
        var directory = NewDirectory();
        try
        {
            var clip = await Generate(directory, seconds, fps: 5);
            using var sampler = Sampler(directory, bytes: clip.Bytes);
            var frames = 0;
            var error = await Assert.ThrowsAsync<LuxMapException>(() => sampler.SampleAsync(new("k", clip.Bytes.Length, clip.Hash), (_, _) => { frames++; return Task.CompletedTask; }, default));
            Assert.Equal((code, HttpStatusCode.BadRequest), (error.Code, error.StatusCode));
            Assert.Equal(0, frames);
            AssertWorkDirectoryEmpty(directory);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task A_truncated_clip_is_a_400_and_a_stored_object_that_does_not_match_its_hash_is_a_server_error()
    {
        var directory = NewDirectory();
        try
        {
            var clip = await Generate(directory, 5);
            var truncated = clip.Bytes[..(clip.Bytes.Length / 2)];
            using (var sampler = Sampler(directory, bytes: truncated))
            {
                var error = await Assert.ThrowsAsync<LuxMapException>(() => sampler.SampleAsync(
                    new("k", truncated.Length, Convert.ToHexStringLower(SHA256.HashData(truncated))), (_, _) => Task.CompletedTask, default));
                Assert.Equal(("VIDEO_INVALID", HttpStatusCode.BadRequest), (error.Code, error.StatusCode));
            }
            using (var sampler = Sampler(directory, bytes: clip.Bytes))
            {
                var error = await Assert.ThrowsAsync<LuxMapException>(() => sampler.SampleAsync(
                    new("k", clip.Bytes.Length, new string('0', 64)), (_, _) => Task.CompletedTask, default));
                Assert.Equal(("VIDEO_STORAGE_MISMATCH", HttpStatusCode.InternalServerError), (error.Code, error.StatusCode));
            }
            AssertWorkDirectoryEmpty(directory);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task Missing_ffmpeg_is_a_503_and_not_a_clip_problem()
    {
        var directory = NewDirectory();
        try
        {
            var clip = await Generate(directory, 5);
            using var sampler = Sampler(directory, bytes: clip.Bytes, tools: new SurveyFrameOptions { FfmpegPath = "ffmpeg-is-not-here", FfprobePath = "ffprobe-is-not-here" });
            var error = await Assert.ThrowsAsync<LuxMapException>(() => Run(sampler, clip));
            Assert.Equal(("VIDEO_PROCESSING_UNAVAILABLE", HttpStatusCode.ServiceUnavailable), (error.Code, error.StatusCode));
            var describe = await Assert.ThrowsAsync<LuxMapException>(() => sampler.DescribeAsync(default));
            Assert.Equal("VIDEO_PROCESSING_UNAVAILABLE", describe.Code);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task The_tools_are_described_by_one_digest_that_names_ffmpeg()
    {
        var directory = NewDirectory();
        try
        {
            using var sampler = Sampler(directory, bytes: []);
            var artifact = await sampler.DescribeAsync(default);
            Assert.Equal(64, artifact.Hash.Length);
            Assert.Equal(artifact.Hash, artifact.Version);
            Assert.Contains("ffmpeg", artifact.Metadata);
            Assert.Equal(artifact, await sampler.DescribeAsync(default));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task A_second_request_waits_for_the_single_slot_and_gets_a_503_when_it_waits_too_long_and_a_timeout_is_a_503()
    {
        var directory = NewDirectory();
        try
        {
            // The first request holds the slot while its (blocked) download is in flight.
            using var sampler = Sampler(directory, new ClipStore([], block: true), o => { o.MaximumConcurrentClips = 1; o.SlotWaitSeconds = .3; o.TimeoutSeconds = 30; });
            using var holder = new CancellationTokenSource();
            var first = sampler.SampleAsync(new("k", 10, new string('0', 64)), (_, _) => Task.CompletedTask, holder.Token);
            await Task.Delay(300);
            var busy = await Assert.ThrowsAsync<LuxMapException>(() => sampler.SampleAsync(new("k", 10, new string('0', 64)), (_, _) => Task.CompletedTask, default));
            Assert.Equal(("VIDEO_PROCESSING_BUSY", HttpStatusCode.ServiceUnavailable), (busy.Code, busy.StatusCode));
            // Cancelling the caller (not the tool's own timeout) is not translated, and frees the slot.
            holder.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);

            using var slow = Sampler(directory, new ClipStore([], block: true), o => o.TimeoutSeconds = .3);
            var timeout = await Assert.ThrowsAsync<LuxMapException>(() => slow.SampleAsync(new("k", 10, new string('0', 64)), (_, _) => Task.CompletedTask, default));
            Assert.Equal(("VIDEO_PROCESSING_TIMEOUT", HttpStatusCode.ServiceUnavailable), (timeout.Code, timeout.StatusCode));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task A_consumer_that_fails_stops_the_sampling_unchanged_and_frees_the_slot_and_the_work_directory()
    {
        var directory = NewDirectory();
        try
        {
            var clip = await Generate(directory, 5);
            using var sampler = Sampler(directory, bytes: clip.Bytes, tune: o => o.MaximumConcurrentClips = 1);
            var seen = 0;
            await Assert.ThrowsAsync<InvalidOperationException>(() => sampler.SampleAsync(new("k", clip.Bytes.Length, clip.Hash),
                (_, _) => ++seen == 2 ? throw new InvalidOperationException("detector failed") : Task.CompletedTask, default));
            Assert.Equal(2, seen);
            AssertWorkDirectoryEmpty(directory);
            var (_, frames) = await Run(sampler, clip);   // the slot was released
            Assert.NotEmpty(frames);
        }
        finally { Directory.Delete(directory, true); }
    }
}
