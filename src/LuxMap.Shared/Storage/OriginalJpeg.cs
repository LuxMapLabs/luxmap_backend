namespace LuxMap.Shared.Storage;

/// <param name="MeanLuma">Mean luma (0..1) of the thumbnail — the number the single-lamp daylight gate looks at.</param>
public sealed record ValidatedJpeg(byte[] Original, byte[] Thumbnail, int Width, int Height, double MeanLuma);

/// <summary>Bounded read, header pixel cap BEFORE decode; original bytes are preserved.</summary>
public interface IOriginalJpegReader
{
    Task<ValidatedJpeg> ReadAsync(Stream content, long maxBytes, long maxPixels, CancellationToken ct);
}
