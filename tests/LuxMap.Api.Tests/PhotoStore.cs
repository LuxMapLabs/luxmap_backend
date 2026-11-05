using LuxMap.Shared.Storage;

namespace LuxMap.Api.Tests;

/// <summary>
/// The default host's store is the S3 adapter, and CI runs no MinIO. Photo tests run a second host whose store keeps
/// objects in memory but runs the REAL image pipeline — magic bytes, decode and thumbnail included.
/// </summary>
internal sealed class PhotoStore : IObjectStore
{
    public readonly System.Collections.Concurrent.ConcurrentDictionary<(StorageBucket, string), byte[]> Objects = new();

    /// <summary>Runs WHILE the image is being written — where a slow upload gives someone else time to act.</summary>
    public Func<Task>? DuringWrite { get; set; }

    public async Task<StoredImage> StoreImageAsync(StorageBucket bucket, string id, Stream content, CancellationToken cancellationToken = default)
    {
        if (DuringWrite is { } during) await during();
        using var prepared = await LuxMap.Infrastructure.Storage.ImagePipeline.PrepareAsync(content, cancellationToken);
        var original = StorageKeys.KeyFor(ObjectVariant.Original, id); var thumbnail = StorageKeys.KeyFor(ObjectVariant.Thumbnail, id);
        Objects[(bucket, original)] = prepared.Original.ToArray(); Objects[(bucket, thumbnail)] = prepared.Thumbnail;
        return new(bucket, original, prepared.OriginalBytes, thumbnail, prepared.ThumbnailBytes);
    }

    public Task<Stream> OpenAsync(StorageBucket bucket, string key, CancellationToken cancellationToken = default)
        => Task.FromResult<Stream>(new MemoryStream(Objects[(bucket, key)], false));

    public Task<bool> ExistsAsync(StorageBucket bucket, string key, CancellationToken cancellationToken = default)
        => Task.FromResult(Objects.ContainsKey((bucket, key)));

    /// <summary>Keeps a clip as the S3 adapter would accept it: size, the MP4 signature, the declared length and the declared SHA-256 all checked.</summary>
    public async Task<StoredObject> StoreStreamAsync(StorageBucket bucket, string key, Stream content, StreamUpload upload, CancellationToken cancellationToken = default)
    {
        if (DuringWrite is { } during) await during();
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        var bytes = buffer.ToArray();
        if (bytes.Length > upload.MaxBytes) throw new LuxMap.Shared.Http.LuxMapException("UPLOAD_TOO_LARGE", System.Net.HttpStatusCode.RequestEntityTooLarge, "Too large.");
        if (upload.RequireMp4 && (bytes.Length < 12 || !bytes.AsSpan(4, 4).SequenceEqual("ftyp"u8)))
            throw new LuxMap.Shared.Http.LuxMapException("UNSUPPORTED_VIDEO_FORMAT", System.Net.HttpStatusCode.UnsupportedMediaType, "Not an MP4.");
        if (bytes.Length != upload.ExpectedBytes) throw new LuxMap.Shared.Http.LuxMapException("CONTENT_LENGTH_MISMATCH", System.Net.HttpStatusCode.BadRequest, "Length.");
        var sha = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes));
        if (sha != upload.ExpectedSha256) throw new LuxMap.Shared.Http.LuxMapException("HASH_MISMATCH", System.Net.HttpStatusCode.BadRequest, "Hash.");
        Objects[(bucket, key)] = bytes;
        return new(key, bytes.Length, sha);
    }

    /// <summary>A small but real JPEG; <paramref name="shade"/> makes two photos differ byte for byte.</summary>
    public static byte[] Jpeg(byte shade = 90)
    {
        using var image = new SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgb24>(640, 480);
        image[320, 240] = new SixLabors.ImageSharp.PixelFormats.Rgb24(shade, 200, 255);
        var buffer = new MemoryStream();
        image.Save(buffer, new SixLabors.ImageSharp.Formats.Jpeg.JpegEncoder { Quality = 90 });
        return buffer.ToArray();
    }
}
