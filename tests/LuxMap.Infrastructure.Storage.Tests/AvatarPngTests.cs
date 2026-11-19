using System.Net;
using LuxMap.Shared.Http;
using LuxMap.Shared.Storage;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.PixelFormats;

namespace LuxMap.Infrastructure.Storage.Tests;

public sealed class AvatarPngTests
{
    [Fact]
    public void Signatures_require_all_bytes_and_distinguish_jpeg_from_png()
    {
        var jpeg = new byte[] { 0xff, 0xd8, 0xff, 0 };
        var png = new byte[] { 0x89, 0x50, 0x4e, 0x47, 13, 10, 26, 10, 0 };
        Assert.True(JpegMagicBytes.Matches(jpeg));
        Assert.False(JpegMagicBytes.MatchesPng(jpeg));
        Assert.True(JpegMagicBytes.MatchesPng(png));
        Assert.False(JpegMagicBytes.Matches(png));
        for (var length = 0; length < JpegMagicBytes.PngSignature.Length; length++)
            Assert.False(JpegMagicBytes.MatchesPng(png.AsSpan(0, length)));
        for (var length = 0; length < JpegMagicBytes.Signature.Length; length++)
            Assert.False(JpegMagicBytes.Matches(jpeg.AsSpan(0, length)));
        for (var index = 0; index < JpegMagicBytes.PngSignature.Length; index++)
        {
            var corrupted = (byte[])png.Clone();
            corrupted[index] ^= 1;
            Assert.False(JpegMagicBytes.MatchesPng(corrupted));
        }
    }

    [Fact]
    public async Task Png_requires_explicit_opt_in_and_rewinds_after_magic_check()
    {
        using var png = Png();
        var error = await Assert.ThrowsAsync<LuxMapException>(() => JpegMagicBytes.EnsureJpegAsync(png));
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, error.StatusCode);
        Assert.Equal(0, png.Position);
        await JpegMagicBytes.EnsureAllowedAsync(png, AllowedImageFormats.JpegOrPng);
        Assert.Equal(0, png.Position);
        using var sanitizer = new JpegSanitizer(1, 0);
        await Assert.ThrowsAsync<LuxMapException>(() => sanitizer.SanitizeJpegAsync(png, new(1024, 1024, 32)));
    }

    [Fact]
    public async Task Transparent_png_is_sanitized_to_white_jpeg()
    {
        using var png = Png();
        using var sanitizer = new JpegSanitizer(1, 0);
        var bytes = await sanitizer.SanitizeJpegAsync(png, new(1024, 1024, 32), allowedFormats: AllowedImageFormats.JpegOrPng);
        Assert.True(JpegMagicBytes.Matches(bytes));
        using var image = Image.Load<Rgb24>(new DecoderOptions { Configuration = ThumbnailFactory.JpegOnly }, bytes);
        Assert.Equal(new Rgb24(255, 255, 255), image[8, 8]);
        Assert.Null(image.Metadata.ExifProfile);
        Assert.Null(image.Metadata.IccProfile);
    }

    private static MemoryStream Png()
    {
        using var image = new Image<Rgba32>(16, 16);
        var output = new MemoryStream();
        image.SaveAsPng(output);
        output.Position = 0;
        return output;
    }
}
