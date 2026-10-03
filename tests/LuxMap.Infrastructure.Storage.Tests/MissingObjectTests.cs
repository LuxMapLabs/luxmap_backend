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

    private sealed class FakeS3(Exception error) : AmazonS3Client(new AnonymousAWSCredentials(),
        new AmazonS3Config { ServiceURL = "http://localhost:1", ForcePathStyle = true })
    {
        public override Task<GetObjectResponse> GetObjectAsync(GetObjectRequest request, CancellationToken cancellationToken = default)
            => Task.FromException<GetObjectResponse>(error);
    }
}
