namespace LuxMap.Shared.Storage;

/// <summary>
/// The two image streams of Contract section 2.6 / CLAUDE.md: survey frames and repair evidence.
/// </summary>
/// <remarks>
/// Two buckets rather than one with a prefix, because the two are different KINDS of data, not two
/// folders. Survey imagery is the study's primary record and its retention is a research question;
/// evidence photos are operational attachments to a work order. Separate buckets keep retention,
/// quota and access policy separable later without moving a single object.
/// </remarks>
public enum StorageBucket
{
    /// <summary>SurveyFrame images (BE-15).</summary>
    Survey,

    /// <summary>RepairEvidence images (BE-24).</summary>
    Evidence,
}

/// <summary>Which of the two objects a stored image produces.</summary>
public enum ObjectVariant
{
    /// <summary>The bytes exactly as uploaded. Never re-encoded — BE-16 reads EXIF from these.</summary>
    Original,

    /// <summary>A derived JPEG for list and map views. A SEPARATE object; it never overwrites the original.</summary>
    Thumbnail,
}

/// <summary>
/// Bucket names and object keys, in one place so BE-15 and BE-24 cannot spell them differently.
/// </summary>
/// <remarks>
/// ⚠️ <b>Never sort by an object key.</b> The id inside it is a Contract prefixed id, whose width is a
/// MINIMUM and not a fixed length, so <c>FRM-100000</c> sorts before <c>FRM-999999</c> as a string.
/// Same trap as the one recorded against <c>ORDER BY pole_id</c> in CLAUDE.md section 0. Order by a
/// timestamp or a sequence in the database instead; the object store is not an index.
/// <para>
/// <c>commune_id</c> is deliberately NOT part of the key. Authorization has exactly one source of
/// truth — the <c>commune_id</c> column, which carries a real foreign key to
/// <c>administrative_unit</c>. A copy inside a key would be a second, unconstrained answer to the
/// same question, and nothing would detect the two drifting apart.
/// </para>
/// </remarks>
public static class StorageKeys
{
    public const string SurveyBucketName = "luxmap-survey";

    public const string EvidenceBucketName = "luxmap-evidence";

    public const string OriginalPrefix = "original";

    public const string ThumbnailPrefix = "thumb";

    /// <summary>The only accepted format (BE-11). Enforced by magic bytes, not by this extension.</summary>
    public const string JpegExtension = ".jpg";

    public const string JpegContentType = "image/jpeg";

    public static string NameOf(StorageBucket bucket) => bucket switch
    {
        StorageBucket.Survey => SurveyBucketName,
        StorageBucket.Evidence => EvidenceBucketName,
        _ => throw new ArgumentOutOfRangeException(nameof(bucket), bucket, "Unknown storage bucket."),
    };

    /// <summary><c>original/FRM-000001.jpg</c> or <c>thumb/FRM-000001.jpg</c>.</summary>
    public static string KeyFor(ObjectVariant variant, string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        var prefix = variant switch
        {
            ObjectVariant.Original => OriginalPrefix,
            ObjectVariant.Thumbnail => ThumbnailPrefix,
            _ => throw new ArgumentOutOfRangeException(nameof(variant), variant, "Unknown object variant."),
        };

        return $"{prefix}/{id}{JpegExtension}";
    }
}

/// <summary>
/// What a successful write produced: both keys and both sizes.
/// </summary>
/// <param name="OriginalBytes">
/// Bytes ACTUALLY written, counted while writing. Never a client-declared <c>Content-Length</c> — a
/// wrong or hostile value there would corrupt the storage totals BE-35 reports.
/// </param>
public sealed record StoredImage(
    StorageBucket Bucket,
    string OriginalKey,
    long OriginalBytes,
    string ThumbnailKey,
    long ThumbnailBytes)
{
    /// <summary>What this image costs in the bucket, for the BE-35 capacity report.</summary>
    public long TotalBytes => OriginalBytes + ThumbnailBytes;
}

/// <summary>
/// Object storage for the two image streams. The ONLY storage contract BE-15 and BE-24 see.
/// </summary>
/// <remarks>
/// ⚠️ Deliberately says nothing about MinIO, S3, buckets-as-URLs or presigned links. BE-15 lands in
/// W5 and BE-24 in W11, eight weeks apart, and both must compile against this shape without it
/// changing. Anything server-specific belongs in the adapter.
/// <para>
/// <b>No presigned URL, by decision.</b> Every byte is served through the API so that Contract
/// section 7 commune scoping applies to images exactly as it applies to rows. A signed URL would be
/// checked by the object store, which knows nothing about <c>commune_id</c> and cannot be told.
/// </para>
/// <para>
/// <b>Write the object first, commit the row second.</b> Both orders can fail halfway; this one fails
/// into an orphaned object — wasted bytes that BE-35 can reconcile — rather than an orphaned row,
/// whose <c>thumbnail_url</c> would 404 in front of a user.
/// </para>
/// </remarks>
public interface IObjectStore
{
    /// <summary>
    /// Validates the image, writes the original byte-for-byte, derives and writes a thumbnail, and
    /// reports what was actually written.
    /// </summary>
    /// <param name="id">The caller's prefixed id, e.g. <c>FRM-000001</c>. The key is derived from it.</param>
    /// <exception cref="Http.LuxMapException">
    /// <c>UNSUPPORTED_IMAGE_FORMAT</c> when the bytes are not a JPEG. Decided by the magic bytes, never
    /// by the file name or a declared content type.
    /// </exception>
    Task<StoredImage> StoreImageAsync(
        StorageBucket bucket, string id, Stream content, CancellationToken cancellationToken = default);

    /// <summary>Opens an object for reading. The caller disposes the stream.</summary>
    Task<Stream> OpenAsync(
        StorageBucket bucket, string key, CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(
        StorageBucket bucket, string key, CancellationToken cancellationToken = default);
}
