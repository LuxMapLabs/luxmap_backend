namespace LuxMap.Infrastructure.Storage;

/// <summary>
/// An upload that has passed validation and has its thumbnail ready, but has not been written yet.
/// </summary>
/// <remarks>
/// <see cref="Original"/> is positioned at 0 and holds the bytes EXACTLY as uploaded — no decode, no
/// re-encode, no EXIF touched, no orientation applied.
/// </remarks>
public sealed class PreparedImage(MemoryStream original, byte[] thumbnail) : IDisposable
{
    public MemoryStream Original { get; } = original;

    public byte[] Thumbnail { get; } = thumbnail;

    /// <summary>Bytes actually read, not a declared <c>Content-Length</c> (BE-11, A6).</summary>
    public long OriginalBytes => Original.Length;

    public long ThumbnailBytes => Thumbnail.LongLength;

    public void Dispose() => Original.Dispose();
}

/// <summary>
/// Validate, then derive the thumbnail — everything that happens to an upload BEFORE any byte
/// reaches the object store.
/// </summary>
/// <remarks>
/// Separated from the adapter on purpose: this is where every rule of BE-11 that can be wrong lives,
/// and none of it needs MinIO. The in-memory store used by the tests runs this same code, so the
/// tests exercise the real validation and the real thumbnail path rather than a stand-in for them.
/// </remarks>
public static class ImagePipeline
{
    /// <summary>
    /// Buffers the upload, rejects anything that is not a JPEG, and renders the thumbnail.
    /// </summary>
    /// <remarks>
    /// <b>Why it buffers.</b> One upload has to be read four times — magic bytes, decode for the
    /// thumbnail, the write itself, and the byte count — and an HTTP request body is a forward-only
    /// stream that can be read exactly once. Buffering makes those passes possible and, just as
    /// importantly, makes the byte count a measurement rather than a claim.
    /// <para>
    /// <b>What it costs.</b> Peak memory is one original plus its thumbnail, PER CONCURRENT UPLOAD —
    /// roughly 5–15 MB for a survey photograph, against a thumbnail of a few tens of KB. Sequential
    /// uploads therefore stay flat no matter how long the sweep is. Hundreds of frames arriving at
    /// once would not: that is a batching and concurrency decision, and it belongs to BE-15, which
    /// owns the sweep upload endpoint. Recorded in CLAUDE.md so it is not discovered under load.
    /// </para>
    /// </remarks>
    public static async Task<PreparedImage> PrepareAsync(
        Stream content, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        var buffered = new MemoryStream();
        try
        {
            await content.CopyToAsync(buffered, cancellationToken);
            buffered.Position = 0;

            // Order matters: reject non-JPEG before handing anything to a decoder.
            await JpegMagicBytes.EnsureJpegAsync(buffered, cancellationToken);

            var thumbnail = await ThumbnailFactory.CreateAsync(buffered, cancellationToken);

            // CreateAsync rewinds, but say so here too — the next thing to touch this stream is the
            // upload, and a wrong position there loses the first bytes of the stored file.
            buffered.Position = 0;

            return new PreparedImage(buffered, thumbnail);
        }
        catch
        {
            await buffered.DisposeAsync();
            throw;
        }
    }
}
