using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.PixelFormats;

namespace LuxMap.Infrastructure.Storage;

/// <summary>Mean luma of a JPEG thumbnail, decoded only through the JPEG-only configuration (one definition for photos, clips and the AI gate).</summary>
public static class JpegLuma
{
    /// <summary>Mean luma (0..1) of a JPEG thumbnail.</summary>
    public static async Task<double> MeanAsync(Stream thumbnail, CancellationToken ct)
    {
        thumbnail.Position = 0;
        await JpegMagicBytes.EnsureJpegAsync(thumbnail, ct);
        thumbnail.Position = 0;
        using var image = await Image.LoadAsync<L8>(new DecoderOptions { Configuration = ThumbnailFactory.JpegOnly }, thumbnail, ct);
        long sum = 0;
        image.ProcessPixelRows(rows =>
        {
            for (var y = 0; y < rows.Height; y++)
            {
                foreach (var pixel in rows.GetRowSpan(y)) sum += pixel.PackedValue;
            }
        });
        return sum / (255.0 * image.Width * image.Height);
    }
}
