using System.Security.Cryptography;
using LuxMap.Shared.Contracts.Errors;
using LuxMap.Shared.Http;
using LuxMap.Shared.Storage;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;

namespace LuxMap.Infrastructure.Storage.Tests;

/// <summary>
/// BE-11 — the rules that make image storage safe to build BE-15 and BE-24 on top of.
/// </summary>
/// <remarks>
/// No MinIO and no PostgreSQL. Every rule under test is a property of the bytes, not of the
/// transport, so the assembly runs wherever the compiler does.
/// </remarks>
public class ObjectStorageTests
{
    private const string FrameId = "FRM-000001";

    private static InMemoryObjectStore Store() => new();

    // ── (i) magic bytes ──────────────────────────────────────────────────────

    [Fact]
    public async Task A_real_jpeg_is_accepted()
    {
        var store = Store();
        using var content = new MemoryStream(TestImages.JpegWithExif());

        var stored = await store.StoreImageAsync(StorageBucket.Survey, FrameId, content);

        Assert.True(stored.OriginalBytes > 0);
    }

    public static TheoryData<string, byte[]> Rejected => new()
    {
        { "PNG", TestImages.PngHeader },
        { "GIF", TestImages.GifHeader },
        { "PDF", TestImages.PdfHeader },
        { "empty file", TestImages.Empty },
        { "two bytes", TestImages.TruncatedToTwoBytes },
    };

    [Theory]
    [MemberData(nameof(Rejected))]
    public async Task Anything_that_is_not_a_jpeg_is_rejected(string label, byte[] bytes)
    {
        var store = Store();
        using var content = new MemoryStream(bytes);

        var error = await Assert.ThrowsAsync<LuxMapException>(
            () => store.StoreImageAsync(StorageBucket.Survey, FrameId, content));

        Assert.Equal(ErrorCodes.UnsupportedImageFormat, error.Code);
        Assert.Equal(System.Net.HttpStatusCode.UnsupportedMediaType, error.StatusCode);

        // Nothing was written — a rejected upload must not leave a half-object behind.
        Assert.Empty(store.Objects);
        Assert.NotNull(label);
    }

    [Fact]
    public async Task A_png_renamed_to_jpg_is_still_rejected()
    {
        // The id carries the .jpg extension into the key, and the caller may well have sent
        // Content-Type: image/jpeg. Neither is consulted; only the first three bytes are.
        var store = Store();
        using var content = new MemoryStream(TestImages.PngHeader);

        var error = await Assert.ThrowsAsync<LuxMapException>(
            () => store.StoreImageAsync(StorageBucket.Survey, FrameId, content));

        Assert.Equal(ErrorCodes.UnsupportedImageFormat, error.Code);
    }

    [Fact]
    public async Task A_jpeg_announced_under_the_wrong_content_type_is_still_accepted()
    {
        // The mirror image of the test above, and the reason the rule is "magic bytes" rather than
        // "magic bytes AND a matching header": a correct file wrongly labelled is a real, common
        // case, and rejecting it would lose survey data for a header nobody controls.
        var store = Store();
        using var content = new MemoryStream(TestImages.JpegWithExif());

        var stored = await store.StoreImageAsync(StorageBucket.Survey, FrameId, content);

        Assert.Equal(StorageKeys.KeyFor(ObjectVariant.Original, FrameId), stored.OriginalKey);
    }

    // ── (ii) A1: the original is not touched ─────────────────────────────────

    [Fact]
    public async Task The_original_is_stored_byte_for_byte()
    {
        var source = TestImages.JpegWithExif();
        var store = Store();
        using var content = new MemoryStream(source);

        var stored = await store.StoreImageAsync(StorageBucket.Survey, FrameId, content);
        var written = store[StorageBucket.Survey, stored.OriginalKey];

        // SHA-256 rather than a length check: reading the magic bytes without rewinding would produce
        // a file three bytes short, and re-encoding would produce one of a similar size but different
        // content. Only a hash catches both.
        Assert.Equal(
            Convert.ToHexString(SHA256.HashData(source)),
            Convert.ToHexString(SHA256.HashData(written)));
    }

    // ── (iii) A1: EXIF survives, which is what BE-16 depends on ──────────────

    [Fact]
    public async Task Every_exposure_tag_BE16_validates_survives_the_pipeline()
    {
        var store = Store();
        using var content = new MemoryStream(TestImages.JpegWithExif());

        var stored = await store.StoreImageAsync(StorageBucket.Survey, FrameId, content);

        using var readBack = new MemoryStream(store[StorageBucket.Survey, stored.OriginalKey]);
        using var image = Image.Load(readBack);
        var exif = image.Metadata.ExifProfile;

        Assert.NotNull(exif);
        Assert.True(exif!.TryGetValue(ExifTag.ISOSpeedRatings, out var iso));
        var isoValue = Assert.IsType<ushort[]>(iso!.Value);
        Assert.Equal(TestImages.Iso, isoValue[0]);

        Assert.True(exif.TryGetValue(ExifTag.ExposureTime, out var shutter));
        Assert.Equal(TestImages.ExposureTime, shutter!.Value);

        Assert.True(exif.TryGetValue(ExifTag.FNumber, out var aperture));
        Assert.Equal(TestImages.FNumber, aperture!.Value);

        Assert.True(exif.TryGetValue(ExifTag.GPSImgDirection, out var heading));
        Assert.Equal(TestImages.Heading, heading!.Value);

        Assert.True(exif.TryGetValue(ExifTag.GPSLatitude, out var latitude));
        Assert.Equal(TestImages.Latitude, latitude!.Value);
    }

    [Fact]
    public async Task The_thumbnail_is_a_separate_object_and_the_original_keeps_its_exif()
    {
        var store = Store();
        using var content = new MemoryStream(TestImages.JpegWithExif());

        var stored = await store.StoreImageAsync(StorageBucket.Survey, FrameId, content);

        Assert.NotEqual(stored.OriginalKey, stored.ThumbnailKey);
        Assert.Equal(2, store.Objects.Count);

        // The encode path drops EXIF — correct for the thumbnail, fatal if it ever reached the
        // original. Asserting both sides pins which object went through which path.
        using var thumbnail = new MemoryStream(store[StorageBucket.Survey, stored.ThumbnailKey]);
        using var thumbnailImage = Image.Load(thumbnail);
        var thumbnailExif = thumbnailImage.Metadata.ExifProfile;
        Assert.True(
            thumbnailExif is null || !thumbnailExif.TryGetValue(ExifTag.ISOSpeedRatings, out _),
            "The thumbnail must not carry the original's exposure metadata.");
    }

    [Fact]
    public async Task The_thumbnail_carries_no_gps_so_a_widely_served_object_cannot_leak_capture_locations()
    {
        // NOT metadata housekeeping — this is the location-leak guard, and the name says so because
        // the line it protects (ExifProfile = null in ThumbnailFactory) looks like tidying and would
        // be a natural thing for someone to delete during a refactor.
        //
        // ImageSharp does NOT drop EXIF on resize; decode-resize-encode carries the profiles straight
        // through. Left alone, every thumbnail would embed the exact coordinates where the photo was
        // taken. The thumbnail is the object served WIDELY — list views, map popups, caches — while
        // the original sits behind an authorised read, so that is the most sensitive field landing in
        // the least protected place, inside a system whose whole authorization model is territorial.
        var store = Store();
        using var content = new MemoryStream(TestImages.JpegWithExif());

        var stored = await store.StoreImageAsync(StorageBucket.Survey, FrameId, content);

        using var thumbnail = Image.Load(new MemoryStream(store[StorageBucket.Survey, stored.ThumbnailKey]));
        var exif = thumbnail.Metadata.ExifProfile;

        Assert.True(
            exif is null || !exif.TryGetValue(ExifTag.GPSLatitude, out _),
            "The thumbnail carries GPSLatitude. ThumbnailFactory must null ExifProfile before encoding.");
        Assert.True(
            exif is null || !exif.TryGetValue(ExifTag.GPSLongitude, out _),
            "The thumbnail carries GPSLongitude. ThumbnailFactory must null ExifProfile before encoding.");
        Assert.True(
            exif is null || !exif.TryGetValue(ExifTag.GPSImgDirection, out _),
            "The thumbnail carries GPSImgDirection. ThumbnailFactory must null ExifProfile before encoding.");

        // The counterpart: the ORIGINAL must still have every one of them, or BE-16 has nothing to
        // validate. Asserting both sides in one test pins which object went through which path.
        using var original = Image.Load(new MemoryStream(store[StorageBucket.Survey, stored.OriginalKey]));
        Assert.True(original.Metadata.ExifProfile!.TryGetValue(ExifTag.GPSLatitude, out _));
        Assert.True(original.Metadata.ExifProfile!.TryGetValue(ExifTag.GPSImgDirection, out _));
    }

    // ── (iv) thumbnail geometry ──────────────────────────────────────────────

    [Fact]
    public async Task The_thumbnail_is_320_on_its_longest_edge_and_keeps_the_ratio()
    {
        var store = Store();
        using var content = new MemoryStream(TestImages.JpegWithExif(width: 800, height: 600));

        var stored = await store.StoreImageAsync(StorageBucket.Survey, FrameId, content);

        var bytes = store[StorageBucket.Survey, stored.ThumbnailKey];
        Assert.True(JpegMagicBytes.Matches(bytes));

        using var thumbnail = Image.Load(new MemoryStream(bytes));
        Assert.Equal(ThumbnailFactory.LongestEdgePixels, thumbnail.Width);
        Assert.Equal(240, thumbnail.Height);
    }

    [Fact]
    public async Task A_portrait_image_puts_the_320_on_its_height()
    {
        var store = Store();
        using var content = new MemoryStream(TestImages.JpegWithExif(width: 600, height: 800));

        var stored = await store.StoreImageAsync(StorageBucket.Survey, FrameId, content);

        using var thumbnail = Image.Load(new MemoryStream(store[StorageBucket.Survey, stored.ThumbnailKey]));
        Assert.Equal(ThumbnailFactory.LongestEdgePixels, thumbnail.Height);
        Assert.Equal(240, thumbnail.Width);
    }

    // ── (v) A6: the byte count is measured, not believed ─────────────────────

    [Fact]
    public async Task The_reported_size_is_what_was_written_not_what_the_client_claimed()
    {
        var source = TestImages.JpegWithExif();
        var store = Store();

        // A stream that lies about its length the way a forged Content-Length header would.
        using var content = new LyingLengthStream(source, claimedLength: source.Length * 10);

        var stored = await store.StoreImageAsync(StorageBucket.Survey, FrameId, content);

        Assert.Equal(source.LongLength, stored.OriginalBytes);
        Assert.NotEqual(content.Length, stored.OriginalBytes);

        Assert.Equal(
            store[StorageBucket.Survey, stored.ThumbnailKey].LongLength, stored.ThumbnailBytes);
        Assert.Equal(stored.OriginalBytes + stored.ThumbnailBytes, stored.TotalBytes);
    }

    // ── (vi) key convention ──────────────────────────────────────────────────

    [Fact]
    public void The_four_key_shapes_are_exactly_as_agreed()
    {
        Assert.Equal("luxmap-survey", StorageKeys.NameOf(StorageBucket.Survey));
        Assert.Equal("luxmap-evidence", StorageKeys.NameOf(StorageBucket.Evidence));

        Assert.Equal("original/FRM-000001.jpg", StorageKeys.KeyFor(ObjectVariant.Original, "FRM-000001"));
        Assert.Equal("thumb/FRM-000001.jpg", StorageKeys.KeyFor(ObjectVariant.Thumbnail, "FRM-000001"));
        Assert.Equal("original/EVD-0001.jpg", StorageKeys.KeyFor(ObjectVariant.Original, "EVD-0001"));
        Assert.Equal("thumb/EVD-0001.jpg", StorageKeys.KeyFor(ObjectVariant.Thumbnail, "EVD-0001"));
    }

    [Fact]
    public void A_key_carries_no_commune_id()
    {
        // Authorization has one source of truth: the commune_id column with its foreign key. A copy
        // in the key would be a second, unconstrained answer, and nothing would notice them drifting.
        var key = StorageKeys.KeyFor(ObjectVariant.Original, "FRM-000001");

        Assert.DoesNotContain("COM-", key, StringComparison.Ordinal);
        Assert.Equal(2, key.Split('/').Length);
    }

    [Fact]
    public async Task Evidence_and_survey_images_land_in_different_buckets()
    {
        var store = Store();
        using var frame = new MemoryStream(TestImages.JpegWithExif());
        using var evidence = new MemoryStream(TestImages.JpegWithExif());

        await store.StoreImageAsync(StorageBucket.Survey, "FRM-000001", frame);
        await store.StoreImageAsync(StorageBucket.Evidence, "EVD-0001", evidence);

        Assert.True(await store.ExistsAsync(StorageBucket.Survey, "original/FRM-000001.jpg"));
        Assert.False(await store.ExistsAsync(StorageBucket.Evidence, "original/FRM-000001.jpg"));
        Assert.True(await store.ExistsAsync(StorageBucket.Evidence, "original/EVD-0001.jpg"));
    }
}

/// <summary>A stream whose <see cref="Length"/> is a lie, standing in for a forged Content-Length.</summary>
internal sealed class LyingLengthStream(byte[] content, long claimedLength) : MemoryStream(content, writable: false)
{
    public override long Length => claimedLength;
}
