using LuxMap.Shared.Contracts.Enums;

namespace LuxMap.Modules.Assets.Entities;

public interface IAssetPhoto
{
    string PhotoId { get; set; }
    string CommuneId { get; set; }
    DataSource DataSource { get; set; }
    DateTime CreatedAt { get; set; }
    string UploadedBy { get; set; }
    Guid ClientOpId { get; set; }
    string RequestHash { get; set; }
    string ObjectKey { get; set; }
    string ThumbnailKey { get; set; }
    long ByteCount { get; set; }
    long ThumbnailBytes { get; set; }
    int Width { get; set; }
    int Height { get; set; }
    DateTime? DeletedAt { get; set; }
    string? DeletedBy { get; set; }
}
