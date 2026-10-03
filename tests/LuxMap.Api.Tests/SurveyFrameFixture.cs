using System.Security.Cryptography;
using System.Text.Json;
using LuxMap.Infrastructure.Storage;
using LuxMap.Modules.Survey.Entities;
using LuxMap.Modules.Survey.Processing.Frames;
using LuxMap.Persistence;
using LuxMap.Shared.Storage;

namespace LuxMap.Api.Tests;

/// <summary>Generated media and memory objects only. The caller owns the PostGIS rows.</summary>
internal sealed class SurveyFrameFixture : IObjectStore, IDisposable
{
    private readonly Dictionary<(StorageBucket, string), byte[]> objects = [];
    private readonly string directory = Path.Combine(Path.GetTempPath(), "luxmap-worker-test-" + Guid.NewGuid().ToString("N"));
    private FfmpegFrameExtractor? extractor;
    public SurveyFramePipeline Pipeline { get; private set; } = null!;
    public bool FailThumbnail { get; set; }
    public int ImageWrites { get; private set; }
    public async Task Initialize(LuxMapDbContext db, string sweep)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "clip.mp4");
        await LuxMap.Modules.Survey.Processing.Frames.MediaProcess.RunAsync("ffmpeg",
            ["-v", "error", "-f", "lavfi", "-i", "color=c=yellow:size=64x32:rate=1", "-t", "21", "-c:v", "libx264", "-bf", "0", "-video_track_timescale", "1000", "-y", path], 30, default);
        byte[] video = await File.ReadAllBytesAsync(path);
        objects[(StorageBucket.Video, "clip")] = video;
        var config = JsonSerializer.SerializeToUtf8Bytes(new { sensor_timestamp_source = "REALTIME", mount = new { camera_side = "front" },
            clips = new[] { new { clip_no = 0, first_pts_ns = 0, last_pts_ns = 20_000_000_000L,
                first_sensor_timestamp_ns = 0, last_sensor_timestamp_ns = 20_000_000_000L, time_base_num = 1, time_base_den = 1000 } } });
        objects[(StorageBucket.Video, "config")] = config;
        db.Add(new SurveyVideoClip { SweepId = sweep, ClipNo = 0, ObjectKey = "clip", ByteCount = video.Length,
            Sha256 = Hash(video), ContentType = "video/mp4", StoredAt = DateTime.UtcNow });
        db.Add(new SurveyRawFile { SweepId = sweep, Kind = SurveyRawKind.CaptureConfig, ObjectKey = "config",
            ByteCount = config.Length, Sha256 = Hash(config), SchemaVersion = 1 });
        var options = new SurveyFrameOptions { TempRoot = Path.Combine(directory, "extracted") };
        extractor = new(this, options);
        var manifest = new Dictionary<string, FakeDetectionCase>();
        await extractor.ExtractAsync("clip", video.Length, Hash(video), new(0, 0, 20_000_000_000L, 0, 20_000_000_000L, 1, 1000),
            [new(10_000_000_000L, 0, 20_000_000_000L)], (frame, _) =>
            {
                manifest[Hash(frame.Jpeg)] = new("success", [new(0, "on", .95, .1, .1, .2, .2), new(1, "off", .95, .7, .1, .2, .2)]);
                return Task.CompletedTask;
            }, default);
        Pipeline = new(this, extractor, new ManifestOnOffDetector(JsonSerializer.Serialize(manifest)), new EmptySurveyBaselineLookup(), options);
    }
    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    public Task<Stream> OpenAsync(StorageBucket bucket, string key, CancellationToken cancellationToken = default)
        => Task.FromResult<Stream>(new MemoryStream(objects[(bucket, key)], false));
    public Task<bool> ExistsAsync(StorageBucket bucket, string key, CancellationToken cancellationToken = default)
        => Task.FromResult(objects.ContainsKey((bucket, key)));
    public async Task<StoredImage> StoreImageAsync(StorageBucket bucket, string id, Stream content, CancellationToken cancellationToken = default)
    {
        using var prepared = await ImagePipeline.PrepareAsync(content, cancellationToken);
        var original = StorageKeys.KeyFor(ObjectVariant.Original, id); var thumbnail = StorageKeys.KeyFor(ObjectVariant.Thumbnail, id);
        objects[(bucket, original)] = prepared.Original.ToArray();
        ImageWrites++;
        if (FailThumbnail) throw new IOException("Injected thumbnail failure after original object.");
        objects[(bucket, thumbnail)] = prepared.Thumbnail;
        return new(bucket, original, prepared.OriginalBytes, thumbnail, prepared.ThumbnailBytes);
    }
    public Task<StoredObject> StoreStreamAsync(StorageBucket bucket, string key, Stream content, StreamUpload upload, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();
    public void Dispose() { extractor?.Dispose(); if (Directory.Exists(directory)) Directory.Delete(directory, true); }
}
