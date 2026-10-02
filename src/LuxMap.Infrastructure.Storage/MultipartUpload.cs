using System.Net;
using System.Security.Cryptography;
using Amazon.S3;
using Amazon.S3.Model;
using LuxMap.Shared.Http;
using LuxMap.Shared.Storage;
using Microsoft.Extensions.Logging;

namespace LuxMap.Infrastructure.Storage;

/// <summary>One 5 MiB part in memory; never buffers or seeks the whole request. JPEG remains separate.</summary>
public static class MultipartUpload
{
    public const int PartBytes = 5 * 1024 * 1024;

    public static async Task<StoredObject> StoreAsync(IAmazonS3 client, StorageBucket bucket, string key,
        Stream content, StreamUpload upload, ILogger logger, CancellationToken ct)
    {
        if (upload.ExpectedBytes > upload.MaxBytes) throw Error("UPLOAD_TOO_LARGE", HttpStatusCode.RequestEntityTooLarge);
        if (upload.ExpectedBytes <= 0) throw Error("VALIDATION_FAILED", HttpStatusCode.BadRequest);
        var name = StorageKeys.NameOf(bucket);
        var initiated = await client.InitiateMultipartUploadAsync(new InitiateMultipartUploadRequest
        { BucketName = name, Key = key, ContentType = upload.ContentType }, ct);
        try
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[PartBytes];
            var parts = new List<PartETag>();
            long total = 0;
            while (true)
            {
                var count = 0;
                while (count < buffer.Length)
                {
                    var read = await content.ReadAsync(buffer.AsMemory(count), ct);
                    if (read == 0) break;
                    count += read;
                    if (total + count > upload.MaxBytes) throw Error("UPLOAD_TOO_LARGE", HttpStatusCode.RequestEntityTooLarge);
                    if (total + count > upload.ExpectedBytes) throw Error("CONTENT_LENGTH_MISMATCH", HttpStatusCode.BadRequest);
                }
                if (count == 0) break;
                if (total == 0 && upload.RequireMp4 && (count < 12 || !buffer.AsSpan(4, 4).SequenceEqual("ftyp"u8)))
                    throw Error("UNSUPPORTED_VIDEO_FORMAT", HttpStatusCode.UnsupportedMediaType);
                hash.AppendData(buffer, 0, count);
                total += count;
                using var part = new MemoryStream(buffer, 0, count, writable: false);
                var result = await client.UploadPartAsync(new UploadPartRequest { BucketName = name, Key = key,
                    UploadId = initiated.UploadId, PartNumber = parts.Count + 1, InputStream = part, PartSize = count }, ct);
                parts.Add(new PartETag(parts.Count + 1, result.ETag));
            }
            if (total != upload.ExpectedBytes) throw Error("CONTENT_LENGTH_MISMATCH", HttpStatusCode.BadRequest);
            var sha = Convert.ToHexStringLower(hash.GetHashAndReset());
            if (!string.Equals(sha, upload.ExpectedSha256, StringComparison.Ordinal))
                throw Error("HASH_MISMATCH", HttpStatusCode.BadRequest);
            await client.CompleteMultipartUploadAsync(new CompleteMultipartUploadRequest
            { BucketName = name, Key = key, UploadId = initiated.UploadId, PartETags = parts }, ct);
            return new(key, total, sha);
        }
        catch
        {
            // Request cancellation must NOT cancel cleanup of an already initiated multipart upload.
            // A failed abort must not replace the reason the upload failed (a 413 would surface as a 500).
            // The parts it leaves are reclaimable storage, not data: the row is never written.
            try
            {
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                await client.AbortMultipartUploadAsync(new AbortMultipartUploadRequest
                { BucketName = name, Key = key, UploadId = initiated.UploadId }, cleanup.Token);
            }
            catch (Exception abort)
            {
                logger.LogWarning(abort, "Could not abort multipart upload {UploadId} for {Bucket}/{Key}", initiated.UploadId, name, key);
            }
            throw;
        }
    }

    private static LuxMapException Error(string code, HttpStatusCode status) => new(code, status, code.Replace('_', ' '));
}
