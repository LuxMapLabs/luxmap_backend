using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;

namespace LuxMap.Infrastructure.Storage.Tests;

/// <summary>
/// A phone held upright stores landscape pixels plus an EXIF Orientation tag. The original keeps the
/// tag (byte-for-byte), so browsers display it upright; the thumbnail drops EXIF, so the rotation has
/// to be baked into its pixels first or it stays sideways for good.
/// </summary>
public sealed class ThumbnailOrientationTests
{
    [Theory]
    [InlineData((ushort)6)] // rotate 90° clockwise
    [InlineData((ushort)8)] // rotate 90° counter-clockwise
    public async Task A_quarter_turn_orientation_is_baked_into_the_thumbnail_pixels_and_the_tag_is_dropped(ushort orientation)
    {
        using var source = new MemoryStream(TestImages.JpegWithOrientation(orientation, width: 200, height: 100));

        var thumbnail = await ThumbnailFactory.CreateAsync(source);

        var info = await Image.IdentifyAsync(new DecoderOptions { Configuration = ThumbnailFactory.JpegOnly }, new MemoryStream(thumbnail));
        Assert.True(info.Height > info.Width, $"Orientation {orientation} thumbnail is {info.Width}x{info.Height}; expected portrait.");
        Assert.Null(info.Metadata.ExifProfile);
    }

    [Fact]
    public async Task Orientation_one_leaves_a_landscape_thumbnail_landscape()
    {
        using var source = new MemoryStream(TestImages.JpegWithOrientation(1, width: 200, height: 100));

        var thumbnail = await ThumbnailFactory.CreateAsync(source);

        var info = await Image.IdentifyAsync(new DecoderOptions { Configuration = ThumbnailFactory.JpegOnly }, new MemoryStream(thumbnail));
        Assert.True(info.Width > info.Height);
    }

    [Fact]
    public async Task A_long_oriented_image_still_has_its_longest_edge_scaled_to_the_limit()
    {
        using var source = new MemoryStream(TestImages.JpegWithOrientation(6, width: 1600, height: 800));

        var thumbnail = await ThumbnailFactory.CreateAsync(source);

        var info = await Image.IdentifyAsync(new DecoderOptions { Configuration = ThumbnailFactory.JpegOnly }, new MemoryStream(thumbnail));
        Assert.Equal(ThumbnailFactory.LongestEdgePixels, info.Height);
        Assert.Equal(ThumbnailFactory.LongestEdgePixels / 2, info.Width);
    }
}
