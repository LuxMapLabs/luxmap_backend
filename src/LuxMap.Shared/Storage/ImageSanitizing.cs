namespace LuxMap.Shared.Storage;

/// <summary>The limits a sanitized upload must fit (CITIZEN-QR P2b, D-8).</summary>
/// <param name="MaxLongEdge">
/// The cleaned photo is scaled down to this longest edge. Evidence for a person needs no 40 MP — and the thumbnail step that decodes
/// the cleaned photo again (outside the sanitizer's gate) then only ever decodes a small image (Codex review P2b #2).
/// </param>
public sealed record ImageLimits(long MaxBytes, long MaxPixels, int MaxLongEdge);

/// <summary>
/// Re-encodes an untrusted JPEG into one that carries nothing but pixels (CITIZEN-QR P2b, D-2 / D-10).
/// </summary>
/// <remarks>
/// <para>
/// The exception to BE-11 rule 4 (originals byte-for-byte), for citizen photos ONLY: a survey frame's EXIF is measurement data,
/// a citizen photo's EXIF is the sender's phone and position. The output is what gets stored — original and thumbnail alike.
/// </para>
/// <para>
/// Order, each step before the next can cost anything: a slot in the concurrency gate (a bounded queue — beyond it 503
/// <c>SERVER_BUSY</c>, before a single byte is buffered), bounded read (bytes), JPEG magic bytes, <c>Identify</c> (header only) and
/// the pixel cap, THEN decode, orientation applied, scaled to <see cref="ImageLimits.MaxLongEdge"/>, every metadata profile dropped,
/// re-encoded. A small file that declares 40 000 × 40 000 pixels is refused before a single row is decoded.
/// </para>
/// </remarks>
public interface IImageSanitizer
{
    /// <exception cref="Http.LuxMapException">
    /// <c>UNSUPPORTED_IMAGE_FORMAT</c> (not a JPEG, or undecodable), <c>VALIDATION_FAILED</c> (over <see cref="ImageLimits"/>).
    /// </exception>
    Task<byte[]> SanitizeJpegAsync(Stream content, ImageLimits limits, CancellationToken cancellationToken = default);
}
