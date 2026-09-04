using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;

namespace LuxMap.Infrastructure.Storage;

/// <summary>
/// Derives the thumbnail of Contract section 2.7 (<c>GET /frames/{id}/thumbnail</c> → JPEG).
/// </summary>
/// <remarks>
/// ⚠️ <b>The original is never passed through here.</b> Encoding drops the EXIF block, which is
/// exactly right for a thumbnail and fatal for the original: BE-16 rejects a frame whose ISO,
/// shutter, aperture, GPS or heading is missing, and an auto-exposure photo cannot be re-measured
/// after the fact. The original is copied byte-for-byte; this class only ever produces a second,
/// separate object.
/// </remarks>
public static class ThumbnailFactory
{
    /// <summary>Longest edge, in pixels. Aspect ratio is preserved, so the other edge follows.</summary>
    /// <remarks>⚠️ PROVISIONAL. The Contract specifies a JPEG but no dimension; 320 has to be agreed with the front end at FW-00.</remarks>
    public const int LongestEdgePixels = 320;

    /// <summary>JPEG quality. Also provisional, same conversation.</summary>
    public const int JpegQuality = 80;

    /// <summary>
    /// A configuration that knows about JPEG and NOTHING else — the second line of defence behind the
    /// magic-byte check.
    /// </summary>
    /// <remarks>
    /// <c>Configuration.Default</c> registers every bundled codec: PNG, GIF, BMP, TGA, TIFF, WebP,
    /// PBM, QOI. Each is a decoder for attacker-supplied bytes, and none of them is wanted here. A
    /// configuration built from <see cref="JpegConfigurationModule"/> alone means a crafted PNG is not
    /// merely rejected by policy — there is no code path able to parse it in the first place.
    /// <para>
    /// Static and shared on purpose: a <c>Configuration</c> is immutable once built and is the unit
    /// ImageSharp expects to be reused, so rebuilding it per image would only add allocations.
    /// </para>
    /// </remarks>
    public static Configuration JpegOnly { get; } = new(new JpegConfigurationModule());

    /// <summary>
    /// Decodes <paramref name="source"/>, scales its longest edge to
    /// <see cref="LongestEdgePixels"/>, and returns the encoded JPEG.
    /// </summary>
    /// <remarks>
    /// The stream is REWOUND before decoding: the magic-byte check ran first and this method must not
    /// depend on where it left the position.
    /// <para>
    /// <see cref="ResizeMode.Max"/> fits the image inside the box while preserving the ratio, and
    /// never enlarges beyond it — an image already smaller than 320 is returned at its own size rather
    /// than upscaled into blur.
    /// </para>
    /// </remarks>
    public static async Task<byte[]> CreateAsync(Stream source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        source.Position = 0;

        var options = new DecoderOptions { Configuration = JpegOnly };
        using var image = await Image.LoadAsync(options, source, cancellationToken);

        image.Mutate(context => context.Resize(new ResizeOptions
        {
            Size = new Size(LongestEdgePixels, LongestEdgePixels),
            Mode = ResizeMode.Max,
        }));

        // Stripped EXPLICITLY, because ImageSharp does NOT drop it. Decode-resize-encode carries the
        // metadata profiles straight through — verified, not assumed: the test asserting an
        // EXIF-free thumbnail failed until this line existed.
        //
        // It matters most for GPS. The thumbnail is the widely served object — it goes into list
        // views and map popups — while the original stays behind an authorised read. Copying the
        // capture coordinates into the small, cheap, frequently cached object puts the most sensitive
        // field in the least protected place. Dropping the profiles also takes a few KB off every
        // thumbnail, which is the smaller of the two reasons.
        image.Metadata.ExifProfile = null;
        image.Metadata.XmpProfile = null;
        image.Metadata.IptcProfile = null;

        var buffer = new MemoryStream();
        await image.SaveAsync(buffer, new JpegEncoder { Quality = JpegQuality }, cancellationToken);

        source.Position = 0;
        return buffer.ToArray();
    }
}
