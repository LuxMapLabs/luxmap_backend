namespace LuxMap.Modules.Identity.Entities;

/// <summary>
/// Token làm mới phiên đăng nhập. Contract mục 0.2 cố ý KHÔNG cấp prefix cho bảng này vì nó
/// không bao giờ lộ ra FE, nên dùng khoá thay thế <c>bigint identity</c> thông thường.
/// </summary>
/// <remarks>
/// KHÔNG lưu token thô: chỉ lưu hash. Rò database thì kẻ tấn công vẫn không mạo danh được phiên.
/// </remarks>
public class RefreshToken
{
    public long Id { get; set; }

    public required string UserId { get; set; }

    /// <summary>Hash của token, có unique index để tra cứu bằng một lần đọc index.</summary>
    public required string TokenHash { get; set; }

    public DateTime ExpiresAt { get; set; }

    /// <summary>Null nghĩa là chưa thu hồi.</summary>
    public DateTime? RevokedAt { get; set; }

    /// <summary>
    /// Token mới thay thế token này khi xoay vòng (BE-07). Có chuỗi thay thế thì phát hiện được
    /// hành vi dùng lại token đã xoay — dấu hiệu token bị đánh cắp.
    /// </summary>
    public long? ReplacedByTokenId { get; set; }

    public DateTime CreatedAt { get; set; }

    public AppUser User { get; set; } = null!;

    public RefreshToken? ReplacedByToken { get; set; }
}
