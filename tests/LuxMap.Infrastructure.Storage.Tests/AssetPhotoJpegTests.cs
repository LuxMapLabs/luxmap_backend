using System.Net;
using System.Text;
using LuxMap.Infrastructure.Storage;
using LuxMap.Shared.Http;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.Metadata.Profiles.Iptc;
using SixLabors.ImageSharp.Metadata.Profiles.Xmp;
using SixLabors.ImageSharp.PixelFormats;

namespace LuxMap.Infrastructure.Storage.Tests;

public sealed class AssetPhotoJpegTests
{
    [Fact]
    public async Task Magic_bytes_are_checked_independently_of_the_decoder()
    {
        using var source = new MemoryStream(new byte[] { 0x89, 0x50, 0x4e, 0x47 });
        var error = await Assert.ThrowsAsync<LuxMapException>(() => JpegMagicBytes.EnsureJpegAsync(source));
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, error.StatusCode);
    }

    private static byte[] ImageBytes()
    {
        using var image = new Image<Rgb24>(500, 250, new Rgb24(90, 90, 90));
        image.Metadata.ExifProfile = new ExifProfile(); image.Metadata.ExifProfile.SetValue(ExifTag.Artist, "private");
        image.Metadata.XmpProfile = new XmpProfile(Encoding.UTF8.GetBytes("<x:xmpmeta xmlns:x='adobe:ns:meta/'><private>location</private></x:xmpmeta>"));
        image.Metadata.IptcProfile = new IptcProfile(); image.Metadata.IptcProfile.SetValue(IptcTag.Caption, "private");
        using var output = new MemoryStream(); image.SaveAsJpeg(output); return output.ToArray();
    }

    [Fact]
    public async Task Bounded_reader_rejects_bytes_before_decode_and_checks_pixels_before_thumbnail()
    {
        using var bytes = new MemoryStream(ImageBytes());
        var error = await Assert.ThrowsAsync<LuxMapException>(() => new OriginalJpegReader().ReadAsync(bytes, 32, 40_000_000, default));
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, error.StatusCode);
        bytes.Position = 0;
        var pixels = await Assert.ThrowsAsync<LuxMapException>(() => new OriginalJpegReader().ReadAsync(bytes, 16 * 1024 * 1024, 100, default));
        Assert.Equal(HttpStatusCode.BadRequest, pixels.StatusCode);
    }

    [Fact]
    public async Task Thumbnail_strips_each_profile_while_original_preserves_all_three()
    {
        var source = ImageBytes(); using var input = new MemoryStream(source);
        var result = await new OriginalJpegReader().ReadAsync(input, 16 * 1024 * 1024, 40_000_000, default);
        Assert.Equal(source, result.Original);
        var options = new DecoderOptions { Configuration = ThumbnailFactory.JpegOnly };
        using var original = Image.Load(options, source);
        Assert.NotNull(original.Metadata.ExifProfile); Assert.NotNull(original.Metadata.XmpProfile); Assert.NotNull(original.Metadata.IptcProfile);
        using var thumb = Image.Load(options, result.Thumbnail);
        Assert.Equal(320, thumb.Width);
        Assert.Null(thumb.Metadata.ExifProfile); Assert.Null(thumb.Metadata.XmpProfile); Assert.Null(thumb.Metadata.IptcProfile);
    }
}
