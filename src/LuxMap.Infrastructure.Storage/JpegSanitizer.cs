using System.Net;
using LuxMap.Shared.Contracts.Errors;
using LuxMap.Shared.Http;
using LuxMap.Shared.Storage;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;

namespace LuxMap.Infrastructure.Storage;

/// <summary>
/// <see cref="IImageSanitizer"/> on ImageSharp (CITIZEN-QR P2b). Decodes ONLY through <see cref="ThumbnailFactory.JpegOnly"/> —
/// the premise under which the ImageSharp advisories are suppressed (<c>JpegOnlyDecodeTests</c>).
/// </summary>
/// <param name="maxConcurrency">Decodes at once; the gate covers decode and re-encode, the expensive part of a large upload.</param>
public sealed class JpegSanitizer(int maxConcurrency) : IImageSanitizer, IDisposable
{
    /// <summary>The quality the cleaned photo is re-encoded at: a citizen's photo is evidence for a person, not a measurement.</summary>
    public const int Quality = 90;

    private readonly SemaphoreSlim gate = new(maxConcurrency, maxConcurrency);

    public async Task<byte[]> SanitizeJpegAsync(Stream content, ImageLimits limits, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(limits);

        using var buffered = await ReadBoundedAsync(content, limits.MaxBytes, cancellationToken);
        await JpegMagicBytes.EnsureJpegAsync(buffered, cancellationToken);
        buffered.Position = 0;

        var decoder = new DecoderOptions { Configuration = ThumbnailFactory.JpegOnly };
        await gate.WaitAsync(cancellationToken);
        try
        {
            ImageInfo info;
            try
            {
                info = await Image.IdentifyAsync(decoder, buffered, cancellationToken);
            }
            catch (Exception error) when (error is ImageFormatException or UnknownImageFormatException)
            {
                throw Unsupported(error);
            }

            if ((long)info.Width * info.Height > limits.MaxPixels)
            {
                throw new LuxMapException(ErrorCodes.ValidationFailed, HttpStatusCode.BadRequest,
                    $"The photo has more than {limits.MaxPixels} pixels.",
                    new Dictionary<string, object?> { ["field"] = "photos", ["width"] = info.Width, ["height"] = info.Height, ["max_pixels"] = limits.MaxPixels });
            }

            buffered.Position = 0;
            Image image;
            try
            {
                image = await Image.LoadAsync(decoder, buffered, cancellationToken);
            }
            catch (Exception error) when (error is ImageFormatException or UnknownImageFormatException)
            {
                throw Unsupported(error);
            }

            using (image)
            {
                // Orientation first: once EXIF is gone, a sideways photo would stay sideways.
                image.Mutate(context => context.AutoOrient());
                image.Metadata.ExifProfile = null;
                image.Metadata.XmpProfile = null;
                image.Metadata.IptcProfile = null;
                image.Metadata.IccProfile = null;

                using var output = new MemoryStream();
                await image.SaveAsync(output, new JpegEncoder { Quality = Quality }, cancellationToken);
                return output.ToArray();
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public void Dispose() => gate.Dispose();

    /// <summary>Reads at most <paramref name="maxBytes"/>; one byte more is a 413, counted on the bytes, not a declared length.</summary>
    private static async Task<MemoryStream> ReadBoundedAsync(Stream content, long maxBytes, CancellationToken ct)
    {
        var buffered = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await content.ReadAsync(chunk, ct)) > 0)
        {
            if (buffered.Length + read > maxBytes)
            {
                buffered.Dispose();
                throw new LuxMapException(ErrorCodes.ValidationFailed, HttpStatusCode.RequestEntityTooLarge,
                    $"A photo may be at most {maxBytes} bytes.",
                    new Dictionary<string, object?> { ["field"] = "photos", ["max_bytes"] = maxBytes });
            }

            buffered.Write(chunk, 0, read);
        }

        buffered.Position = 0;
        return buffered;
    }

    private static LuxMapException Unsupported(Exception error)
        => new(ErrorCodes.UnsupportedImageFormat, HttpStatusCode.UnsupportedMediaType,
            "The photo starts like a JPEG but cannot be decoded as one: " + error.Message,
            new Dictionary<string, object?> { ["field"] = "photos" });
}
