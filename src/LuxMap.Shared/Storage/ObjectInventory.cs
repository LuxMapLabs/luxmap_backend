namespace LuxMap.Shared.Storage;

/// <summary>One object as the store lists it: what it costs and when it was written.</summary>
public sealed record ListedObject(string Key, long Bytes, DateTime LastModifiedUtc);

/// <summary>
/// Read-only listing of what a bucket actually holds (BE-35, D-6) — the other half of the orphan check, against the keys the
/// rows hold. Kept apart from <see cref="IObjectStore"/>: nothing on a request path needs it, and no write path may come to rely on
/// listing a bucket.
/// </summary>
public interface IObjectInventory
{
    /// <summary>Every object of <paramref name="bucket"/>, in whatever order the store returns them — never sort by key.</summary>
    IAsyncEnumerable<ListedObject> ListAsync(StorageBucket bucket, CancellationToken cancellationToken = default);
}

/// <summary>
/// One column that holds an object key, and the column that holds what that object cost (BE-35, D-2.5). The module that owns the
/// table registers it as a singleton; monitoring adds the bytes up per bucket and compares the keys with the store, without referencing
/// the module.
/// </summary>
/// <remarks>
/// Identifiers, never input: they are spliced into SQL. <c>StoredObjectColumnTests</c> fails when a <c>*object_key</c> /
/// <c>*thumbnail_key</c> column in the database has no registration, so a new photo table cannot drop out of the totals.
/// </remarks>
public sealed record StoredObjectColumn(StorageBucket Bucket, string Table, string KeyColumn, string BytesColumn)
{
    /// <summary>The original of an image row: <c>object_key</c> / <c>byte_count</c>.</summary>
    public static StoredObjectColumn Original(StorageBucket bucket, string table) => new(bucket, table, "object_key", "byte_count");

    /// <summary>The thumbnail of an image row: <c>thumbnail_key</c> / <c>thumbnail_bytes</c>.</summary>
    public static StoredObjectColumn Thumbnail(StorageBucket bucket, string table) => new(bucket, table, "thumbnail_key", "thumbnail_bytes");
}
