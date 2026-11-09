using System.Globalization;
using System.Net;
using LuxMap.Modules.Identity.Entities;
using LuxMap.Persistence;
using LuxMap.Shared.Authorization;
using LuxMap.Shared.Contracts.Errors;
using LuxMap.Shared.Http;
using LuxMap.Shared.Serialization;
using LuxMap.Shared.Storage;
using Microsoft.EntityFrameworkCore;

namespace LuxMap.Modules.Identity.Profiles;

public static class AvatarLimits
{
    public const long MaxBytes = 1_048_576;
    public const long MaxPixels = 16_000_000;
    public const int MaxLongEdge = 512;
    public const long MaxRequestBytes = MaxBytes + 65_536;
    public static readonly ImageLimits Image = new(MaxBytes, MaxPixels, MaxLongEdge);
}

public sealed record AvatarResponse(string? AvatarUrl);

/// <summary>Shared by current-user and admin account responses; version is UTC Unix milliseconds, so two uploads in one second still change the URL (ACCT-5).</summary>
internal static class AvatarUrls
{
    public static string? For(AppUser user, bool admin = false)
    {
        if (user.AvatarUpdatedAt is not { } updated) return null;
        var path = admin ? $"admin/users/{user.UserId}" : "auth/me";
        return $"/api/v1/{path}/avatar?v={new DateTimeOffset(updated).ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture)}";
    }
}

public sealed class AvatarService(
    LuxMapDbContext db, ICurrentActorAccessor actor, IObjectStore store, IImageSanitizer sanitizer, TimeProvider clock)
{
    public string OwnUserId => actor.UserId
        ?? throw new LuxMapException(ErrorCodes.Unauthenticated, HttpStatusCode.Unauthorized, "Sign in to manage your avatar.");

    public async Task<AvatarResponse> UploadAsync(Stream content, CancellationToken ct)
    {
        var user = await db.Set<AppUser>().SingleOrDefaultAsync(u => u.UserId == OwnUserId, ct)
            ?? throw Missing();
        var clean = await sanitizer.SanitizeJpegAsync(content, AvatarLimits.Image, ct);
        using var image = new MemoryStream(clean, writable: false);
        // No transaction or row lock spans sanitizing or object storage. Fixed keys are overwritten on each upload.
        var stored = await store.StoreImageAsync(StorageBucket.Avatar, user.UserId, image, ct);
        user.AvatarObjectKey = stored.OriginalKey;
        user.AvatarByteCount = stored.OriginalBytes;
        user.AvatarThumbnailKey = stored.ThumbnailKey;
        user.AvatarThumbnailBytes = stored.ThumbnailBytes;
        user.AvatarUpdatedAt = UtcMicrosecondClock.UtcNow(clock);
        MarkAvatarModified(user);
        await db.SaveChangesAsync(ct);
        return new(AvatarUrls.For(user));
    }

    public async Task<AppUser> FindAsync(string id, CancellationToken ct)
        => await db.Set<AppUser>().AsNoTracking().SingleOrDefaultAsync(u => u.UserId == id && u.AvatarUpdatedAt != null, ct)
            ?? throw Missing();

    public Task<Stream> OpenAsync(AppUser user, CancellationToken ct)
        => store.OpenAsync(StorageBucket.Avatar, user.AvatarThumbnailKey!, ct);

    public async Task RemoveAsync(CancellationToken ct)
    {
        var user = await db.Set<AppUser>().SingleOrDefaultAsync(u => u.UserId == OwnUserId, ct);
        if (user is null) return;
        // Unlink only. IObjectStore has no deletion API; the next upload overwrites the same two keys.
        user.AvatarObjectKey = null;
        user.AvatarByteCount = null;
        user.AvatarThumbnailKey = null;
        user.AvatarThumbnailBytes = null;
        user.AvatarUpdatedAt = null;
        MarkAvatarModified(user);
        await db.SaveChangesAsync(ct);
    }

    private void MarkAvatarModified(AppUser user)
    {
        // Write the whole set even when fixed keys equal their loaded values (e.g. an overlapping unlink).
        var entry = db.Entry(user);
        entry.Property(u => u.AvatarObjectKey).IsModified = true;
        entry.Property(u => u.AvatarByteCount).IsModified = true;
        entry.Property(u => u.AvatarThumbnailKey).IsModified = true;
        entry.Property(u => u.AvatarThumbnailBytes).IsModified = true;
        entry.Property(u => u.AvatarUpdatedAt).IsModified = true;
    }

    private static LuxMapException Missing()
        => new(ErrorCodes.AvatarNotFound, HttpStatusCode.NotFound, "No avatar is available.");
}
