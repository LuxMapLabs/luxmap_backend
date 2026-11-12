using LuxMap.Persistence.Audit;
using LuxMap.Shared.Authorization;
using LuxMap.Shared.Contracts.Enums;

namespace LuxMap.Modules.Assets.Entities;

public sealed class PoleAssetPhoto : ICommuneScoped, IAudited, IAssetPhoto
{
    public string PhotoId { get; set; } = null!;
    public string PoleId { get; set; } = null!;
    public string CommuneId { get; set; } = null!;
    public DataSource DataSource { get; set; }
    public DateTime CreatedAt { get; set; }
    public string UploadedBy { get; set; } = null!;
    public Guid ClientOpId { get; set; }
    public string RequestHash { get; set; } = null!;
    public string ObjectKey { get; set; } = null!;
    public string ThumbnailKey { get; set; } = null!;
    public long ByteCount { get; set; }
    public long ThumbnailBytes { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public DateTime? DeletedAt { get; set; }
    public string? DeletedBy { get; set; }
}
