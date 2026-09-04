using LuxMap.Infrastructure.Storage;
using LuxMap.Shared.Storage;

namespace LuxMap.Infrastructure.Storage.Tests;

/// <summary>
/// An <see cref="IObjectStore"/> that keeps objects in a dictionary.
/// </summary>
/// <remarks>
/// ⚠️ It runs the REAL <see cref="ImagePipeline"/>, so validation, the thumbnail and the byte count
/// are the production code paths. Only the transport is replaced. A fake that also faked the pipeline
/// would assert nothing worth asserting.
/// <para>
/// This is what keeps the whole assembly free of MinIO AND of PostgreSQL — it runs anywhere
/// <c>dotnet test</c> runs, which is the same reason BE-10's banned-API guard lives in a
/// database-free assembly.
/// </para>
/// </remarks>
public sealed class InMemoryObjectStore : IObjectStore
{
    private readonly Dictionary<(StorageBucket Bucket, string Key), byte[]> objects = [];

    public IReadOnlyDictionary<(StorageBucket Bucket, string Key), byte[]> Objects => objects;

    public byte[] this[StorageBucket bucket, string key] => objects[(bucket, key)];

    public async Task<StoredImage> StoreImageAsync(
        StorageBucket bucket, string id, Stream content, CancellationToken cancellationToken = default)
    {
        using var prepared = await ImagePipeline.PrepareAsync(content, cancellationToken);

        var originalKey = StorageKeys.KeyFor(ObjectVariant.Original, id);
        var thumbnailKey = StorageKeys.KeyFor(ObjectVariant.Thumbnail, id);

        objects[(bucket, originalKey)] = prepared.Original.ToArray();
        objects[(bucket, thumbnailKey)] = prepared.Thumbnail;

        return new StoredImage(
            bucket, originalKey, prepared.OriginalBytes, thumbnailKey, prepared.ThumbnailBytes);
    }

    public Task<Stream> OpenAsync(
        StorageBucket bucket, string key, CancellationToken cancellationToken = default)
        => Task.FromResult<Stream>(new MemoryStream(objects[(bucket, key)], writable: false));

    public Task<bool> ExistsAsync(
        StorageBucket bucket, string key, CancellationToken cancellationToken = default)
        => Task.FromResult(objects.ContainsKey((bucket, key)));
}
