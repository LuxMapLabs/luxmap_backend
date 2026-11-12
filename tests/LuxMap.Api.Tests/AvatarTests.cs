using System.Net;
using System.Text.Json;
using LuxMap.Modules.Identity.Auth;
using LuxMap.Modules.Identity.Entities;
using LuxMap.Modules.Identity.Profiles;
using LuxMap.Modules.Identity.Seeding;
using LuxMap.Shared.Contracts.Enums;
using LuxMap.Shared.Contracts.Errors;
using LuxMap.Shared.Storage;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.PixelFormats;

namespace LuxMap.Api.Tests;

public sealed class AvatarTestFactory : AccountTestFactory
{
    internal PhotoStore Photos { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(services => services.Replace(ServiceDescriptor.Singleton<IObjectStore>(Photos)));
    }
}

/// <summary>ACCT-5: sanitized self-service avatars, with a separate admin capability.</summary>
public sealed class AvatarTests(AvatarTestFactory factory) : IClassFixture<AvatarTestFactory>, IAsyncLifetime
{
    private const string Own = "/api/v1/auth/me/avatar";
    private readonly List<string> users = [];

    public Task InitializeAsync() => Task.CompletedTask;

    [Fact]
    public void Avatar_routes_have_no_anonymous_metadata()
    {
        var endpoints = factory.Services.GetRequiredService<Microsoft.AspNetCore.Routing.EndpointDataSource>().Endpoints
            .Where(e => e.Metadata.GetMetadata<Microsoft.AspNetCore.Mvc.Controllers.ControllerActionDescriptor>()?
                .ControllerTypeInfo.AsType() == typeof(AvatarController)).ToArray();
        Assert.Equal(4, endpoints.Length);
        Assert.All(endpoints, e => Assert.Null(e.Metadata.GetMetadata<Microsoft.AspNetCore.Authorization.IAllowAnonymous>()));
    }

    public async Task DisposeAsync()
    {
        factory.Photos.DuringWrite = null;
        await factory.QueryAsync(async db =>
        {
            db.RemoveRange(await db.Set<AppUser>().Where(u => users.Contains(u.UserId)).ToListAsync());
            return await db.SaveChangesAsync(); // Only our accounts; tokens and assignments cascade.
        });
    }

    [Fact]
    public async Task Upload_serves_clean_rotated_thumbnail_and_both_account_views_link_to_it()
    {
        var (client, id) = await Account(UserRole.FieldEngineer);
        var uploaded = await Upload(client, ExifJpeg(), "image/png"); // Magic bytes win.
        Assert.Equal(HttpStatusCode.OK, uploaded.StatusCode);
        var row = await Row(id);
        var version = new DateTimeOffset(row.AvatarUpdatedAt!.Value).ToUnixTimeMilliseconds();
        var url = $"{Own}?v={version}";
        Assert.Equal(url, (await Json(uploaded)).GetProperty("avatar_url").GetString());
        Assert.Equal(url, (await Json(await client.GetAsync("/api/v1/auth/me"))).GetProperty("avatar_url").GetString());
        var served = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, served.StatusCode);
        Assert.Equal("image/jpeg", served.Content.Headers.ContentType!.MediaType);
        Assert.True(served.Headers.CacheControl!.Private);
        Assert.Equal(TimeSpan.FromDays(1), served.Headers.CacheControl.MaxAge);
        Assert.Equal("nosniff", Assert.Single(served.Headers.GetValues("X-Content-Type-Options")));
        using var thumbnail = Decode(await served.Content.ReadAsByteArrayAsync());
        Assert.Equal(160, thumbnail.Width);
        Assert.Equal(320, thumbnail.Height); // Orientation 6 applied to the 800 × 400 source.
        Assert.Null(thumbnail.Metadata.ExifProfile);
        using var original = Decode(factory.Photos.Objects[(StorageBucket.Avatar, row.AvatarObjectKey!)]);
        Assert.Equal(256, original.Width);
        Assert.Equal(512, original.Height); // Sanitizer edge cap, separately from the 320px thumbnail.
        Assert.Null(original.Metadata.ExifProfile);
        Assert.Null(original.Metadata.IccProfile);
        Assert.Null(original.Metadata.XmpProfile);
        Assert.Null(original.Metadata.IptcProfile);
        AssertStored(row);

        var (admin, _) = await Account(UserRole.SystemAdmin);
        var adminUrl = $"/api/v1/admin/users/{id}/avatar?v={version}";
        Assert.Equal(adminUrl, (await Json(await admin.GetAsync($"/api/v1/admin/users/{id}"))).GetProperty("avatar_url").GetString());
        var list = await Json(await admin.GetAsync("/api/v1/admin/users?page_size=200"));
        Assert.Equal(adminUrl, list.GetProperty("items").EnumerateArray().Single(u => u.GetProperty("user_id").GetString() == id)
            .GetProperty("avatar_url").GetString());
        var adminRead = await admin.GetAsync(adminUrl);
        Assert.Equal(HttpStatusCode.OK, adminRead.StatusCode);
        Assert.Equal(await served.Content.ReadAsByteArrayAsync(), await adminRead.Content.ReadAsByteArrayAsync());
        using var conditional = new HttpRequestMessage(HttpMethod.Get, adminUrl);
        conditional.Headers.IfNoneMatch.Add(adminRead.Headers.ETag!);
        Assert.Equal(HttpStatusCode.NotModified, (await admin.SendAsync(conditional)).StatusCode);
    }

    [Theory]
    [InlineData("png", "image/jpeg")]
    [InlineData("png", "image/png")]
    [InlineData("webp", "image/webp")]
    [InlineData("garbage", "image/jpeg")]
    [InlineData("broken-jpeg", "image/jpeg")]
    public async Task Non_jpeg_and_undecodable_files_are_415_even_with_a_jpg_name(string kind, string type)
    {
        var (client, id) = await Account(UserRole.Manager);
        byte[] bytes = kind switch
        {
            "png" => [0x89, 0x50, 0x4e, 0x47, 13, 10, 26, 10],
            "webp" => "RIFF0000WEBP"u8.ToArray(),
            "broken-jpeg" => [0xff, 0xd8, 0xff, 0],
            _ => "not an image"u8.ToArray(),
        };
        await Error(await Upload(client, bytes, type), HttpStatusCode.UnsupportedMediaType, ErrorCodes.UnsupportedImageFormat);
        Assert.Null((await Row(id)).AvatarUpdatedAt);
        Assert.Empty(Objects(id));
    }

    [Fact]
    public async Task Oversized_bytes_and_small_header_declaring_huge_pixels_are_refused()
    {
        var (client, id) = await Account(UserRole.Manager);
        await Error(await Upload(client, new byte[AvatarLimits.MaxBytes + 1]), HttpStatusCode.RequestEntityTooLarge, ErrorCodes.ValidationFailed);
        // Edit a real JPEG's SOF header without allocating the declared huge bitmap.
        var huge = PhotoStore.Jpeg();
        var found = false;
        for (var i = 0; i < huge.Length - 8; i++)
        {
            if (huge[i] != 0xff || huge[i + 1] is not (0xc0 or 0xc1 or 0xc2)) continue;
            huge[i + 5] = 0x9c; huge[i + 6] = 0x40; // Height 40000
            huge[i + 7] = 0x9c; huge[i + 8] = 0x40; // Width 40000
            found = true;
            break;
        }
        Assert.True(found);
        var response = await Upload(client, huge);
        await Error(response, HttpStatusCode.BadRequest, ErrorCodes.ValidationFailed);
        Assert.Equal(16_000_000, (await Json(response)).GetProperty("error").GetProperty("details").GetProperty("max_pixels").GetInt64());
        Assert.Empty(Objects(id));
    }

    [Fact]
    public async Task Missing_file_has_a_specific_field_error()
    {
        var (client, _) = await Account(UserRole.Manager);
        using var form = new MultipartFormDataContent { { new StringContent("x"), "note" } };
        var response = await client.PostAsync(Own, form);
        await Error(response, HttpStatusCode.BadRequest, ErrorCodes.ValidationFailed);
        Assert.Equal("file", (await Json(response)).GetProperty("error").GetProperty("details").GetProperty("field").GetString());
    }

    [Fact]
    public async Task Replacement_reuses_keys_changes_bytes_url_etag_then_unlink_is_idempotent()
    {
        var (client, id) = await Account(UserRole.Superior);
        Assert.Equal(HttpStatusCode.OK, (await Upload(client, PhotoStore.Jpeg(10))).StatusCode);
        var first = await Row(id);
        var oldBytes = factory.Photos.Objects[(StorageBucket.Avatar, first.AvatarObjectKey!)];
        var oldUrl = (await Json(await client.GetAsync("/api/v1/auth/me"))).GetProperty("avatar_url").GetString();
        var firstRead = await client.GetAsync(Own);
        using (var request = new HttpRequestMessage(HttpMethod.Get, Own))
        {
            request.Headers.TryAddWithoutValidation("If-None-Match", "W/" + firstRead.Headers.ETag);
            var cached = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.NotModified, cached.StatusCode);
            Assert.Empty(await cached.Content.ReadAsByteArrayAsync());
            Assert.Equal(firstRead.Headers.ETag, cached.Headers.ETag);
        }
        factory.Clock.Advance(TimeSpan.FromSeconds(1));
        var replacement = await Upload(client, PhotoStore.Jpeg(240));
        Assert.Equal(HttpStatusCode.OK, replacement.StatusCode);
        var second = await Row(id);
        Assert.True(second.AvatarUpdatedAt > first.AvatarUpdatedAt);
        Assert.NotEqual(oldUrl, (await Json(replacement)).GetProperty("avatar_url").GetString());
        Assert.NotEqual(firstRead.Headers.ETag, (await client.GetAsync(Own)).Headers.ETag);
        Assert.Equal(first.AvatarObjectKey, second.AvatarObjectKey);
        Assert.Equal(first.AvatarThumbnailKey, second.AvatarThumbnailKey);
        Assert.False(oldBytes.SequenceEqual(factory.Photos.Objects[(StorageBucket.Avatar, second.AvatarObjectKey!)]));
        Assert.Equal(2, Objects(id).Count());
        AssertStored(second);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync(Own)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync(Own)).StatusCode);
        var removed = await Row(id);
        Assert.Null(removed.AvatarObjectKey); Assert.Null(removed.AvatarByteCount);
        Assert.Null(removed.AvatarThumbnailKey); Assert.Null(removed.AvatarThumbnailBytes); Assert.Null(removed.AvatarUpdatedAt);
        Assert.Equal(JsonValueKind.Null, (await Json(await client.GetAsync("/api/v1/auth/me"))).GetProperty("avatar_url").ValueKind);
        await Error(await client.GetAsync(Own), HttpStatusCode.NotFound, ErrorCodes.AvatarNotFound);
        Assert.Equal(2, Objects(id).Count()); // Unlink deliberately preserves bytes.
    }

    [Theory]
    [InlineData(UserRole.Manager)]
    [InlineData(UserRole.FieldEngineer)]
    [InlineData(UserRole.Superior)]
    [InlineData(UserRole.SystemAdmin)]
    public async Task Each_role_manages_own_avatar_and_only_admin_reads_another(UserRole role)
    {
        var (client, _) = await Account(role);
        var (other, otherId) = await Account(UserRole.FieldEngineer);
        Assert.Equal(HttpStatusCode.OK, (await Upload(other, PhotoStore.Jpeg())).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Upload(client, PhotoStore.Jpeg())).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(Own)).StatusCode);
        var response = await client.GetAsync($"/api/v1/admin/users/{otherId}/avatar");
        if (role == UserRole.SystemAdmin) Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        else await Error(response, HttpStatusCode.Forbidden, ErrorCodes.RoleForbidden);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync(Own)).StatusCode);
    }

    [Fact]
    public async Task Anonymous_is_401_on_all_routes_and_missing_admin_avatars_are_indistinguishable()
    {
        var anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await Upload(anonymous, PhotoStore.Jpeg())).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(Own)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.DeleteAsync(Own)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/v1/admin/users/USR-0/avatar")).StatusCode);
        var (admin, id) = await Account(UserRole.SystemAdmin);
        foreach (var target in new[] { id, "USR-0" })
            await Error(await admin.GetAsync($"/api/v1/admin/users/{target}/avatar"), HttpStatusCode.NotFound, ErrorCodes.AvatarNotFound);
        Assert.Equal(JsonValueKind.Null, (await Json(await admin.GetAsync($"/api/v1/admin/users/{id}"))).GetProperty("avatar_url").ValueKind);
    }

    [Fact]
    public async Task Both_concurrent_uploads_succeed_and_leave_one_complete_set()
    {
        var (client, id) = await Account(UserRole.Manager);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var count = 0;
        factory.Photos.DuringWrite = async () =>
        {
            if (Interlocked.Increment(ref count) == 2) entered.TrySetResult();
            // Overlap the writes if the service lets them; a service that serializes uploads of one account never lets
            // the second in while the first waits here, so the barrier gives up instead of deadlocking.
            try { await entered.Task.WaitAsync(TimeSpan.FromSeconds(1)); }
            catch (TimeoutException) { }
        };
        var responses = await Task.WhenAll(Upload(client, PhotoStore.Jpeg(10)), Upload(client, PhotoStore.Jpeg(200)));
        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        AssertStored(await Row(id));
        Assert.Equal(2, Objects(id).Count());
    }

    [Fact]
    public async Task Storage_failure_never_publishes_a_row_before_objects()
    {
        var (client, id) = await Account(UserRole.Manager);
        var observed = false;
        factory.Photos.DuringWrite = async () =>
        {
            var pending = await Row(id);
            Assert.Null(pending.AvatarObjectKey);
            Assert.Null(pending.AvatarUpdatedAt);
            observed = true;
            throw new IOException("Injected object write failure.");
        };
        Assert.Equal(HttpStatusCode.InternalServerError, (await Upload(client, PhotoStore.Jpeg())).StatusCode);
        Assert.True(observed);
        Assert.Null((await Row(id)).AvatarUpdatedAt);
        Assert.Empty(Objects(id));
    }

    [Fact]
    public async Task Database_check_refuses_partial_avatar_and_negative_bytes()
    {
        var (client, id) = await Account(UserRole.Manager);
        var partial = await Assert.ThrowsAsync<DbUpdateException>(() => factory.QueryAsync(async db =>
        {
            var user = await db.Set<AppUser>().SingleAsync(u => u.UserId == id);
            user.AvatarObjectKey = $"original/{id}.jpg";
            return await db.SaveChangesAsync();
        }));
        Assert.Equal("ck_app_user_avatar_all_or_none", Assert.IsType<Npgsql.PostgresException>(partial.InnerException).ConstraintName);
        Assert.Equal(HttpStatusCode.OK, (await Upload(client, PhotoStore.Jpeg())).StatusCode);
        var negative = await Assert.ThrowsAsync<DbUpdateException>(() => factory.QueryAsync(async db =>
        {
            var user = await db.Set<AppUser>().SingleAsync(u => u.UserId == id);
            user.AvatarThumbnailBytes = -1;
            return await db.SaveChangesAsync();
        }));
        Assert.Equal("ck_app_user_avatar_all_or_none", Assert.IsType<Npgsql.PostgresException>(negative.InnerException).ConstraintName);
    }

    private async Task<(HttpClient Client, string Id)> Account(UserRole role)
    {
        var commune = await factory.SeededCommuneIdAsync();
        var user = new AppUser
        {
            Username = "avatar-" + Guid.NewGuid().ToString("N"), Email = "avatar-" + Guid.NewGuid().ToString("N") + "@example.invalid",
            FullName = "Avatar test", Role = role, HasSystemWideScope = role == UserRole.SystemAdmin,
            PasswordAlgorithm = IdentitySeeder.PasswordAlgorithm,
        };
        await factory.QueryAsync(async db =>
        {
            db.Add(user);
            await db.SaveChangesAsync();
            if (role != UserRole.SystemAdmin)
            {
                db.Add(new AppUserCommune { UserId = user.UserId, CommuneId = commune, AssignedAt = factory.Clock.GetUtcNow().UtcDateTime });
            }
            return await db.SaveChangesAsync();
        });
        users.Add(user.UserId);
        var token = factory.Services.GetRequiredService<AccessTokenIssuer>().Issue(user, role == UserRole.SystemAdmin ? ["*"] : [commune]);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", token.Token);
        return (client, user.UserId);
    }

    private Task<AppUser> Row(string id) => factory.QueryAsync(db => db.Set<AppUser>().AsNoTracking().SingleAsync(u => u.UserId == id));
    private IEnumerable<(StorageBucket, string)> Objects(string id)
        => factory.Photos.Objects.Keys.Where(k => k.Item1 == StorageBucket.Avatar && (k.Item2 == $"original/{id}.jpg" || k.Item2 == $"thumb/{id}.jpg"));

    private void AssertStored(AppUser row)
    {
        Assert.Equal($"original/{row.UserId}.jpg", row.AvatarObjectKey);
        Assert.Equal($"thumb/{row.UserId}.jpg", row.AvatarThumbnailKey);
        Assert.NotNull(row.AvatarUpdatedAt);
        Assert.Equal(DateTimeKind.Utc, row.AvatarUpdatedAt.Value.Kind);
        Assert.Equal(factory.Photos.Objects[(StorageBucket.Avatar, row.AvatarObjectKey!)].LongLength, row.AvatarByteCount);
        Assert.Equal(factory.Photos.Objects[(StorageBucket.Avatar, row.AvatarThumbnailKey!)].LongLength, row.AvatarThumbnailBytes);
    }

    private static async Task<HttpResponseMessage> Upload(HttpClient client, byte[] bytes, string type = "image/jpeg")
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new(type);
        form.Add(file, "file", "avatar.jpg");
        return await client.PostAsync(Own, form);
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response)
        => JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    private static async Task Error(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal(code, (await Json(response)).GetProperty("error").GetProperty("code").GetString());
    }

    private static Image Decode(byte[] bytes)
        => Image.Load(new DecoderOptions { Configuration = LuxMap.Infrastructure.Storage.ThumbnailFactory.JpegOnly }, bytes);

    private static byte[] ExifJpeg()
    {
        using var image = new Image<Rgb24>(800, 400);
        image.Metadata.ExifProfile = new ExifProfile();
        image.Metadata.ExifProfile.SetValue(ExifTag.Orientation, (ushort)6);
        image.Metadata.ExifProfile.SetValue(ExifTag.GPSLatitude, [new Rational(11, 1), new Rational(24, 1), new Rational(0, 1)]);
        using var output = new MemoryStream();
        image.SaveAsJpeg(output);
        return output.ToArray();
    }
}
