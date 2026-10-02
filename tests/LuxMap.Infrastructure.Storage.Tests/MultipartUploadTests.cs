using System.Security.Cryptography;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using LuxMap.Shared.Http;
using LuxMap.Shared.Storage;
using Microsoft.Extensions.Logging.Abstractions;

namespace LuxMap.Infrastructure.Storage.Tests;

public class MultipartUploadTests
{
    [Fact]
    public async Task Forward_only_stream_is_hashed_counted_and_split_into_bounded_parts()
    {
        var bytes = Video(MultipartUpload.PartBytes * 2 + 31);
        using var client = new FakeS3();
        using var stream = new ForwardOnly(bytes);
        var result = await MultipartUpload.StoreAsync(client, StorageBucket.Video, "clip", stream,
            new(bytes.Length, Hash(bytes), bytes.Length, "video/mp4", true), NullLogger.Instance, default);
        Assert.Equal(Hash(bytes), result.Sha256);
        Assert.Equal(bytes.Length, result.ByteCount);
        Assert.Equal(new[] { MultipartUpload.PartBytes, MultipartUpload.PartBytes, 31 }, client.Parts.Select(x => x.Length));
        Assert.Equal(bytes, client.Parts.SelectMany(x => x).ToArray());
        Assert.True(client.Completed);
        Assert.False(client.Aborted);
    }

    [Theory]
    [InlineData("hash", "HASH_MISMATCH", 400)]
    [InlineData("length", "CONTENT_LENGTH_MISMATCH", 400)]
    [InlineData("magic", "UNSUPPORTED_VIDEO_FORMAT", 415)]
    [InlineData("limit", "UPLOAD_TOO_LARGE", 413)]
    public async Task Invalid_stream_is_aborted_and_never_completed(string fault, string code, int status)
    {
        var bytes = Video(100);
        if (fault == "magic") bytes[4] = 0;
        using var client = new FakeS3();
        using var stream = new ForwardOnly(bytes);
        var error = await Assert.ThrowsAsync<LuxMapException>(() => MultipartUpload.StoreAsync(client, StorageBucket.Video, "clip", stream,
            new(fault is "length" or "limit" ? 99 : 100, fault == "hash" ? new string('0', 64) : Hash(bytes), fault == "limit" ? 99 : 100,
                "video/mp4", true), NullLogger.Instance, default));
        Assert.Equal(code, error.Code);
        Assert.Equal(status, (int)error.StatusCode);
        Assert.True(client.Aborted);
        Assert.False(client.Completed);
    }

    [Fact]
    public async Task A_failing_abort_does_not_replace_the_reason_the_upload_failed()
    {
        var bytes = Video(100);
        using var client = new FakeS3 { AbortFails = true };
        using var stream = new ForwardOnly(bytes);
        var error = await Assert.ThrowsAsync<LuxMapException>(() => MultipartUpload.StoreAsync(client, StorageBucket.Video, "clip", stream,
            new(99, Hash(bytes), 99, "video/mp4", true), NullLogger.Instance, default));
        Assert.Equal("UPLOAD_TOO_LARGE", error.Code);
        Assert.True(client.Aborted);
    }

    [Fact]
    public async Task Cancellation_aborts_with_a_fresh_token()
    {
        using var cancellation = new CancellationTokenSource();
        using var client = new FakeS3 { OnPart = () => cancellation.Cancel() };
        var bytes = Video(MultipartUpload.PartBytes + 1);
        using var stream = new ForwardOnly(bytes);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => MultipartUpload.StoreAsync(client, StorageBucket.Video, "clip", stream,
            new(bytes.Length, Hash(bytes), bytes.Length, "video/mp4", true), NullLogger.Instance, cancellation.Token));
        Assert.True(client.Aborted);
        Assert.False(client.AbortTokenCancelled);
        Assert.False(client.Completed);
    }

    public static byte[] Video(int size)
    {
        var bytes = new byte[size];
        bytes[3] = 24;
        "ftypisom"u8.CopyTo(bytes.AsSpan(4));
        return bytes;
    }
    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    private sealed class ForwardOnly(byte[] bytes) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override long Length => throw new NotSupportedException();
    }
    private sealed class FakeS3() : AmazonS3Client(new AnonymousAWSCredentials(), new AmazonS3Config { ServiceURL = "http://unused.invalid" })
    {
        public List<byte[]> Parts { get; } = [];
        public bool Completed { get; private set; }
        public bool Aborted { get; private set; }
        public bool AbortTokenCancelled { get; private set; }
        public bool AbortFails { get; init; }
        public Action? OnPart { get; init; }
        public override Task<InitiateMultipartUploadResponse> InitiateMultipartUploadAsync(InitiateMultipartUploadRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(new InitiateMultipartUploadResponse { UploadId = "fake" });
        public override async Task<UploadPartResponse> UploadPartAsync(UploadPartRequest request, CancellationToken cancellationToken = default)
        {
            using var output = new MemoryStream();
            await request.InputStream.CopyToAsync(output, cancellationToken);
            Parts.Add(output.ToArray());
            OnPart?.Invoke();
            return new UploadPartResponse { ETag = "fake" };
        }
        public override Task<CompleteMultipartUploadResponse> CompleteMultipartUploadAsync(CompleteMultipartUploadRequest request, CancellationToken cancellationToken = default)
        { Completed = true; return Task.FromResult(new CompleteMultipartUploadResponse()); }
        public override Task<AbortMultipartUploadResponse> AbortMultipartUploadAsync(AbortMultipartUploadRequest request, CancellationToken cancellationToken = default)
        {
            Aborted = true; AbortTokenCancelled = cancellationToken.IsCancellationRequested;
            return AbortFails ? throw new AmazonS3Exception("abort failed") : Task.FromResult(new AbortMultipartUploadResponse());
        }
    }
}
