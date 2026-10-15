using System.Net;
using LuxMap.Shared.Http;
using LuxMap.Shared.Storage;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;

namespace LuxMap.Infrastructure.Storage;

public sealed class OriginalJpegReader : IOriginalJpegReader
{
    public async Task<ValidatedJpeg> ReadAsync(Stream content, long maxBytes, long maxPixels, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await content.ReadAsync(chunk, ct)) != 0)
        {
            if (buffer.Length + read > maxBytes)
                throw new LuxMapException("UPLOAD_TOO_LARGE", HttpStatusCode.RequestEntityTooLarge, "Photo exceeds the byte limit.");
            buffer.Write(chunk, 0, read);
        }
        buffer.Position = 0;
        await JpegMagicBytes.EnsureJpegAsync(buffer, ct);
        buffer.Position = 0;
        try
        {
            var decoder = new DecoderOptions { Configuration = ThumbnailFactory.JpegOnly };
            var info = await Image.IdentifyAsync(decoder, buffer, ct);
            if ((long)info.Width * info.Height > maxPixels)
                throw new LuxMapException("VALIDATION_FAILED", HttpStatusCode.BadRequest, "Photo exceeds the pixel limit.");
            buffer.Position = 0;
            var thumbnail = await ThumbnailFactory.CreateAsync(buffer, ct);
            return new(buffer.ToArray(), thumbnail, info.Width, info.Height);
        }
        catch (ImageFormatException)
        {
            throw new LuxMapException("UNSUPPORTED_IMAGE_FORMAT", HttpStatusCode.UnsupportedMediaType, "The JPEG cannot be decoded.");
        }
    }
}
