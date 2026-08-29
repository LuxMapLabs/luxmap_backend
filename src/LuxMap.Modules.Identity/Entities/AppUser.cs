using LuxMap.Shared.Contracts.Enums;

namespace LuxMap.Modules.Identity.Entities;

/// <summary>
/// Tài khoản người dùng. ID dạng <c>USR-001</c> (Contract mục 0.2).
/// Không có vai trò Người dân — xem <see cref="UserRole"/>.
/// </summary>
public class AppUser
{
    /// <summary>Do DB sinh qua sequence — không tự đặt khi insert.</summary>
    public string UserId { get; set; } = null!;

    public required string Username { get; set; }

    public required string Email { get; set; }

    public required string FullName { get; set; }

    /// <summary>
    /// Chuỗi đã mã hoá của <c>PasswordHasher</c>, salt nằm SẴN bên trong nên không có cột salt
    /// riêng. BE-07 hiện thực phần băm và kiểm; BE-06 chỉ dựng schema.
    /// </summary>
    public required string PasswordHash { get; set; }

    /// <summary>
    /// Thuật toán đã dùng để băm, ví dụ <c>pbkdf2-aspnetcore-v3</c>. Có cột này thì đổi thuật
    /// toán về sau không phải đụng schema, và biết được bản ghi nào cần băm lại.
    /// </summary>
    public required string PasswordAlgorithm { get; set; }

    public UserRole Role { get; set; }

    /// <summary>Khoá tài khoản: chặn đăng nhập mà vẫn giữ nguyên lịch sử thao tác của user.</summary>
    public bool IsLocked { get; set; }

    /// <summary>
    /// Contract mục 7: Quản trị có phạm vi toàn hệ thống, claim mang giá trị đặc biệt <c>*</c>.
    /// Dùng cờ này thay vì nhét mọi xã vào bảng nối — thêm xã mới sẽ không phải sửa gì.
    /// </summary>
    public bool HasSystemWideScope { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public ICollection<AppUserCommune> CommuneAssignments { get; set; } = [];

    public ICollection<RefreshToken> RefreshTokens { get; set; } = [];
}

/// <summary>
/// Bảng nối user ↔ xã, phục vụ claim <c>commune_ids</c> ở Contract mục 7.
/// Nhiều-nhiều vì Cơ quan quản lý có thể phụ trách nhiều xã.
/// </summary>
public class AppUserCommune
{
    public required string UserId { get; set; }

    public required string CommuneId { get; set; }

    public DateTime AssignedAt { get; set; }

    public AppUser User { get; set; } = null!;

    public AdministrativeUnit Commune { get; set; } = null!;
}
