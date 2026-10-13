namespace LuxMap.Shared.Storage;

/// <summary>The limits a sanitized upload must fit (CITIZEN-QR P2b, D-8).</summary>
public sealed record ImageLimits(long MaxBytes, long MaxPixels);

/// <summary>
/// Re-encodes an untrusted JPEG into one that carries nothing but pixels (CITIZEN-QR P2b, D-2 / D-10).
/// </summary>
/// <remarks>
/// <para>
/// The exception to BE-11 rule 4 (originals byte-for-byte), for citizen photos ONLY: a survey frame's EXIF is measurement data,
/// a citizen photo's EXIF is the sender's phone and position. The output is what gets stored — original and thumbnail alike.
/// </para>
/// <para>
/// Order, each step before the next can cost anything: bounded read (bytes), JPEG magic bytes, <c>Identify</c> (header only) and
/// the pixel cap, THEN decode — inside a concurrency gate — orientation applied, every metadata profile dropped, re-encoded.
/// A small file that declares 40 000 × 40 000 pixels is refused before a single row is decoded.
/// </para>
/// </remarks>
public interface IImageSanitizer
{
    /// <exception cref="Http.LuxMapException">
    /// <c>UNSUPPORTED_IMAGE_FORMAT</c> (not a JPEG, or undecodable), <c>VALIDATION_FAILED</c> (over <see cref="ImageLimits"/>).
    /// </exception>
    Task<byte[]> SanitizeJpegAsync(Stream content, ImageLimits limits, CancellationToken cancellationToken = default);
}
