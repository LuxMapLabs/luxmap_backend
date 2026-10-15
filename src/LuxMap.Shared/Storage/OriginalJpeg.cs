namespace LuxMap.Shared.Storage;

public sealed record ValidatedJpeg(byte[] Original, byte[] Thumbnail, int Width, int Height);

/// <summary>Bounded read, header pixel cap BEFORE decode; original bytes are preserved.</summary>
public interface IOriginalJpegReader
{
    Task<ValidatedJpeg> ReadAsync(Stream content, long maxBytes, long maxPixels, CancellationToken ct);
}
