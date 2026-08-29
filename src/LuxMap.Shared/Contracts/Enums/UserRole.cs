namespace LuxMap.Shared.Contracts.Enums;

/// <summary>
/// Bốn vai trò ở CLAUDE.md và Contract mục 7.
/// </summary>
/// <remarks>
/// ⚠️ KHÔNG có trong Contract v1.1 mục 1 — Contract chỉ liệt kê vai trò bằng tiếng Việt, không
/// chốt giá trị enum trên dây. Bốn chuỗi dưới đây do BE-06 đặt và sẽ nằm trong claim của JWT,
/// nên FE và mobile sẽ hardcode chúng. <b>Cần đưa vào Contract ở FW-00 rồi tăng version</b>
/// trước khi WP5/WP6 code theo.
/// <para>
/// Không có vai trò Người dân — Contract và CLAUDE.md đều nêu đích danh điều này.
/// </para>
/// </remarks>
public enum UserRole
{
    /// <summary>Cơ quan quản lý — có thể phụ trách nhiều xã.</summary>
    ManagementAgency,

    /// <summary>Kỹ sư bảo trì — duyệt sự cố, đúng các xã trong claim.</summary>
    MaintenanceEngineer,

    /// <summary>Tổ khảo sát / sửa chữa — báo sự cố tại chỗ, đúng các xã trong claim.</summary>
    FieldCrew,

    /// <summary>Quản trị — phạm vi toàn hệ thống, claim mang giá trị đặc biệt '*'.</summary>
    Administrator,
}
