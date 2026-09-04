using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace LuxMap.Infrastructure.Storage.Tests;

/// <summary>
/// Builds the test images in memory.
/// </summary>
/// <remarks>
/// Nothing binary is committed. A checked-in .jpg would be a fixture nobody can read the contents of
/// in a diff, and the EXIF values this suite asserts on are exactly the thing that would silently
/// differ between a committed file and what the test claims about it. Generated here, the expected
/// values and the file agree by construction.
/// </remarks>
public static class TestImages
{
    /// <summary>The five tags BE-16 rejects a frame for missing.</summary>
    public const ushort Iso = 800;

    public static readonly Rational ExposureTime = new(1, 60);
    public static readonly Rational FNumber = new(18, 10);
    public static readonly Rational Heading = new(25, 1);

    /// <summary>10° 58' 12" — inside the study site.</summary>
    public static readonly Rational[] Latitude = [new(10, 1), new(58, 1), new(12, 1)];

    /// <summary>A JPEG carrying every exposure tag BE-16 validates.</summary>
    public static byte[] JpegWithExif(int width = 800, int height = 600)
    {
        using var image = new Image<Rgb24>(width, height);

        // A little variation so the encoder cannot collapse the whole frame into one flat block.
        image.Mutate(context => context.BackgroundColor(Color.DarkSlateGray));
        image[width / 2, height / 2] = new Rgb24(255, 240, 180);

        var exif = new ExifProfile();
        exif.SetValue(ExifTag.ISOSpeedRatings, [Iso]);
        exif.SetValue(ExifTag.ExposureTime, ExposureTime);
        exif.SetValue(ExifTag.FNumber, FNumber);
        exif.SetValue(ExifTag.GPSImgDirection, Heading);
        exif.SetValue(ExifTag.GPSLatitude, Latitude);
        image.Metadata.ExifProfile = exif;

        var buffer = new MemoryStream();
        image.Save(buffer, new JpegEncoder { Quality = 92 });
        return buffer.ToArray();
    }

    /// <summary>
    /// Headers only. The magic-byte check runs before any decoder, so these never need to be valid
    /// files — and keeping them minimal makes it obvious that the FIRST BYTES are what decides.
    /// </summary>
    public static byte[] PngHeader => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00];

    public static byte[] GifHeader => [0x47, 0x49, 0x46, 0x38, 0x39, 0x61, 0x00, 0x00];

    public static byte[] PdfHeader => [0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x37];

    public static byte[] Empty => [];

    /// <summary>Two bytes: the SOI marker, but the stream ends before the third signature byte.</summary>
    public static byte[] TruncatedToTwoBytes => [0xFF, 0xD8];
}
