using LuxMap.Shared.Storage;

namespace LuxMap.Infrastructure.Storage.Tests;

/// <summary>A one-clip object store for media tests; <c>block</c> makes every read wait until it is cancelled.</summary>
internal sealed class ClipStore(byte[] bytes, bool block = false) : IObjectStore
{
    public async Task<Stream> OpenAsync(StorageBucket bucket, string key, CancellationToken cancellationToken = default)
    { if (block) await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); return new MemoryStream(bytes, false); }
    public Task<bool> ExistsAsync(StorageBucket bucket, string key, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<StoredImage> StoreImageAsync(StorageBucket bucket, string id, Stream content, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<StoredObject> StoreStreamAsync(StorageBucket bucket, string key, Stream content, StreamUpload upload, CancellationToken cancellationToken = default) => throw new NotSupportedException();
}
