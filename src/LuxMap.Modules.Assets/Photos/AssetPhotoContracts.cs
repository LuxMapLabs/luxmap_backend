namespace LuxMap.Modules.Assets.Photos;

public sealed record AssetPhotoCover(string PhotoId, string ThumbnailUrl);

public abstract record AssetPhotoItem(
    string PhotoId, DateTime CreatedAt, string UploadedBy, Guid ClientOpId,
    int Width, int Height, long ByteCount, long ThumbnailBytes, string OriginalUrl, string ThumbnailUrl);

public sealed record PoleAssetPhotoItem(
    string PhotoId, string PoleId, DateTime CreatedAt, string UploadedBy, Guid ClientOpId,
    int Width, int Height, long ByteCount, long ThumbnailBytes, string OriginalUrl, string ThumbnailUrl)
    : AssetPhotoItem(PhotoId, CreatedAt, UploadedBy, ClientOpId, Width, Height, ByteCount, ThumbnailBytes, OriginalUrl, ThumbnailUrl);

public sealed record CabinetAssetPhotoItem(
    string PhotoId, string CabinetId, DateTime CreatedAt, string UploadedBy, Guid ClientOpId,
    int Width, int Height, long ByteCount, long ThumbnailBytes, string OriginalUrl, string ThumbnailUrl)
    : AssetPhotoItem(PhotoId, CreatedAt, UploadedBy, ClientOpId, Width, Height, ByteCount, ThumbnailBytes, OriginalUrl, ThumbnailUrl);
