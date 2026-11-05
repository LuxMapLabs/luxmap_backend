using System.Net;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using LuxMap.Shared.Http;
using LuxMap.Shared.Storage;
using Microsoft.Extensions.Logging.Abstractions;

namespace LuxMap.Infrastructure.Storage.Tests;

public sealed class MissingObjectTests
{
    [Theory]
    [InlineData(HttpStatusCode.NotFound, null)]
    [InlineData(HttpStatusCode.BadRequest, "NoSuchKey")]
    public async Task Missing_thumbnail_becomes_storage_503(HttpStatusCode status, string? code)
    {
        using var client = new FakeS3(new AmazonS3Exception("missing") { StatusCode = status, ErrorCode = code });
        IObjectStore store = new S3ObjectStore(client, NullLogger<S3ObjectStore>.Instance);
        var error = await Assert.ThrowsAsync<LuxMapException>(() => store.OpenAsync(StorageBucket.Survey, "thumb/frame.jpg"));
        Assert.Equal("STORAGE_OBJECT_MISSING", error.Code);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, error.StatusCode);
    }

    [Fact]
    public async Task Access_denied_is_not_misreported_as_missing()
    {
        var failure = new AmazonS3Exception("denied") { StatusCode = HttpStatusCode.Forbidden };
        using var client = new FakeS3(failure);
        var store = new S3ObjectStore(client, NullLogger<S3ObjectStore>.Instance);
        Assert.Same(failure, await Assert.ThrowsAsync<AmazonS3Exception>(() => store.OpenAsync(StorageBucket.Survey, "thumb/frame.jpg")));
    }

    // POLE-VIDEO D-9: a range read asks S3 for exactly the inclusive range, and a range the object cannot satisfy is the same
    // repair-required state as a missing object, not a client error.
    [Fact]
    public async Task A_range_read_asks_S3_for_the_inclusive_range_in_the_video_bucket()
    {
        using var client = new FakeS3(new InvalidOperationException("not used"), respond: new GetObjectResponse { ResponseStream = new MemoryStream([1, 2, 3]) });
        IObjectStore store = new S3ObjectStore(client, NullLogger<S3ObjectStore>.Instance);
        await using var body = await store.OpenRangeAsync(StorageBucket.Video, "pole-videos/a.mp4", 100, 199);
        var request = Assert.Single(client.Requests);
        Assert.Equal(StorageKeys.NameOf(StorageBucket.Video), request.BucketName);
        Assert.Equal("pole-videos/a.mp4", request.Key);
        Assert.Equal(("100", "199"), (request.ByteRange.Start.ToString(), request.ByteRange.End.ToString()));
        Assert.Equal(new byte[] { 1, 2, 3 }, ((MemoryStream)body).ToArray());
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, null)]
    [InlineData(HttpStatusCode.RequestedRangeNotSatisfiable, null)]
    [InlineData(HttpStatusCode.BadRequest, "InvalidRange")]
    public async Task A_range_the_object_cannot_satisfy_or_a_missing_object_becomes_storage_503(HttpStatusCode status, string? code)
    {
        using var client = new FakeS3(new AmazonS3Exception("gone") { StatusCode = status, ErrorCode = code });
        IObjectStore store = new S3ObjectStore(client, NullLogger<S3ObjectStore>.Instance);
        var error = await Assert.ThrowsAsync<LuxMapException>(() => store.OpenRangeAsync(StorageBucket.Video, "clip.mp4", 0, 9));
        Assert.Equal(("STORAGE_OBJECT_MISSING", HttpStatusCode.ServiceUnavailable), (error.Code, error.StatusCode));
    }

    [Fact]
    public async Task A_range_read_does_not_hide_access_denied_and_refuses_an_impossible_range_before_calling_S3()
    {
        var failure = new AmazonS3Exception("denied") { StatusCode = HttpStatusCode.Forbidden };
        using var client = new FakeS3(failure);
        var store = new S3ObjectStore(client, NullLogger<S3ObjectStore>.Instance);
        Assert.Same(failure, await Assert.ThrowsAsync<AmazonS3Exception>(() => store.OpenRangeAsync(StorageBucket.Video, "clip.mp4", 0, 9)));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => store.OpenRangeAsync(StorageBucket.Video, "clip.mp4", -1, 9));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => store.OpenRangeAsync(StorageBucket.Video, "clip.mp4", 10, 9));
        Assert.Single(client.Requests);
    }

    private sealed class FakeS3(Exception error, GetObjectResponse? respond = null) : AmazonS3Client(new AnonymousAWSCredentials(),
        new AmazonS3Config { ServiceURL = "http://localhost:1", ForcePathStyle = true })
    {
        public List<GetObjectRequest> Requests { get; } = [];

        public override Task<GetObjectResponse> GetObjectAsync(GetObjectRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return respond is null ? Task.FromException<GetObjectResponse>(error) : Task.FromResult(respond);
        }
    }
}
