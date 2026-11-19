using System.Net;
using LuxMap.Shared.Contracts.Errors;
using LuxMap.Shared.Http;
using LuxMap.Shared.Storage;

namespace LuxMap.Infrastructure.Storage;

/// <summary>
/// Decides whether a stream holds a JPEG by looking at its first three bytes (BE-11, A3).
/// </summary>
/// <remarks>
/// Neither the file name nor the declared <c>Content-Type</c> is consulted. Both are attacker- or
/// accident-controlled: a PNG renamed <c>.jpg</c> and a JPEG announced as <c>application/octet-stream</c>
/// are equally common, and only one of them should be rejected.
/// <para>
/// A pure function over a stream, with no dependency on the object store — which is what lets the
/// whole rule be tested without MinIO running.
/// </para>
/// </remarks>
public static class JpegMagicBytes
{
    /// <summary>SOI marker <c>FF D8</c> followed by the first marker's <c>FF</c>.</summary>
    public static ReadOnlySpan<byte> Signature => [0xFF, 0xD8, 0xFF];

    public static ReadOnlySpan<byte> PngSignature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    public static bool MatchesPng(ReadOnlySpan<byte> header)
        => header.Length >= PngSignature.Length && header[..PngSignature.Length].SequenceEqual(PngSignature);

    public static bool Matches(ReadOnlySpan<byte> header)
        => header.Length >= Signature.Length && header[..Signature.Length].SequenceEqual(Signature);

    /// <summary>
    /// Throws unless <paramref name="content"/> starts with the JPEG signature, then REWINDS the
    /// stream to position 0.
    /// </summary>
    /// <remarks>
    /// ⚠️ The rewind is the whole point of this method existing rather than being three inline lines.
    /// Reading a header advances the position; writing straight afterwards would silently store an
    /// image missing its first bytes — a corrupt file that no exception announces.
    /// </remarks>
    /// <exception cref="LuxMapException">415 <c>UNSUPPORTED_IMAGE_FORMAT</c>.</exception>
    public static async Task EnsureJpegAsync(Stream content, CancellationToken cancellationToken = default)
    {
        await EnsureAllowedAsync(content, AllowedImageFormats.JpegOnly, cancellationToken);
    }

    public static async Task EnsureAllowedAsync(Stream content, AllowedImageFormats allowedFormats, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (allowedFormats is not (AllowedImageFormats.JpegOnly or AllowedImageFormats.JpegOrPng))
        {
            throw new ArgumentOutOfRangeException(nameof(allowedFormats));
        }

        var header = new byte[allowedFormats == AllowedImageFormats.JpegOrPng ? PngSignature.Length : Signature.Length];
        var read = await content.ReadAtLeastAsync(
            header, header.Length, throwOnEndOfStream: false, cancellationToken);

        content.Position = 0;

        // A file shorter than the signature cannot be a JPEG, and this is also the empty-file case.
        if (!Matches(header.AsSpan(0, read))
            && !(allowedFormats == AllowedImageFormats.JpegOrPng && MatchesPng(header.AsSpan(0, read))))
        {
            throw new LuxMapException(
                ErrorCodes.UnsupportedImageFormat,
                HttpStatusCode.UnsupportedMediaType,
                allowedFormats == AllowedImageFormats.JpegOrPng
                    ? "Only JPEG or PNG images are accepted, identified by magic bytes."
                    : "Only JPEG images are accepted, identified by magic bytes.");
        }
    }
}
