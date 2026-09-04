using Amazon.S3;
using Amazon.S3.Model;
using LuxMap.Shared.Storage;
using Microsoft.Extensions.Logging;

namespace LuxMap.Infrastructure.Storage;

/// <summary>
/// <see cref="IObjectStore"/> backed by MinIO over the S3 API.
/// </summary>
/// <remarks>
/// The only class in the solution that knows the object store exists. Everything above it sees
/// <see cref="IObjectStore"/>, which names no vendor.
/// <para>
/// Buckets are NOT created here. The <c>minio-mc</c> sidecar in docker compose creates them once the
/// server reports healthy, so a missing bucket is a deployment fault that surfaces at the first
/// write instead of turning application startup into a network dependency — a shape this repository
/// avoids everywhere, including for PostgreSQL.
/// </para>
/// </remarks>
public sealed class S3ObjectStore(IAmazonS3 client, ILogger<S3ObjectStore> logger) : IObjectStore
{
    public async Task<StoredImage> StoreImageAsync(
        StorageBucket bucket, string id, Stream content, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(content);

        using var prepared = await ImagePipeline.PrepareAsync(content, cancellationToken);

        var bucketName = StorageKeys.NameOf(bucket);
        var originalKey = StorageKeys.KeyFor(ObjectVariant.Original, id);
        var thumbnailKey = StorageKeys.KeyFor(ObjectVariant.Thumbnail, id);

        // The original goes first. If the thumbnail write fails, the research data is already safe
        // and only the derived object is missing — the recoverable half of the failure.
        await PutAsync(bucketName, originalKey, prepared.Original, cancellationToken);

        using var thumbnailStream = new MemoryStream(prepared.Thumbnail, writable: false);
        await PutAsync(bucketName, thumbnailKey, thumbnailStream, cancellationToken);

        logger.LogInformation(
            "Stored {Bucket}/{OriginalKey} ({OriginalBytes} bytes) and its thumbnail ({ThumbnailBytes} bytes).",
            bucketName, originalKey, prepared.OriginalBytes, prepared.ThumbnailBytes);

        return new StoredImage(
            bucket, originalKey, prepared.OriginalBytes, thumbnailKey, prepared.ThumbnailBytes);
    }

    public async Task<Stream> OpenAsync(
        StorageBucket bucket, string key, CancellationToken cancellationToken = default)
    {
        var response = await client.GetObjectAsync(
            new GetObjectRequest { BucketName = StorageKeys.NameOf(bucket), Key = key },
            cancellationToken);

        return response.ResponseStream;
    }

    public async Task<bool> ExistsAsync(
        StorageBucket bucket, string key, CancellationToken cancellationToken = default)
    {
        try
        {
            await client.GetObjectMetadataAsync(
                new GetObjectMetadataRequest { BucketName = StorageKeys.NameOf(bucket), Key = key },
                cancellationToken);

            return true;
        }
        catch (AmazonS3Exception exception) when (exception.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    /// <summary>
    /// Uploads one object.
    /// </summary>
    /// <remarks>
    /// <c>ContentType</c> is set from what was VERIFIED, never echoed from the client: the magic bytes
    /// already proved this is a JPEG, and the header the caller sent proved nothing.
    /// <c>DisablePayloadSigning</c> is off, so the SDK signs the payload — MinIO checks it, which
    /// catches a truncated upload that a plain length check would not.
    /// </remarks>
    private async Task PutAsync(
        string bucketName, string key, Stream body, CancellationToken cancellationToken)
    {
        body.Position = 0;

        await client.PutObjectAsync(
            new PutObjectRequest
            {
                BucketName = bucketName,
                Key = key,
                InputStream = body,
                ContentType = StorageKeys.JpegContentType,
                AutoCloseStream = false,
            },
            cancellationToken);
    }
}
