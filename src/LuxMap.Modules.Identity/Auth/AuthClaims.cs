namespace LuxMap.Modules.Identity.Auth;

/// <summary>
/// Tên claim trong access token. BE-08 so chuỗi CHÍNH XÁC — đừng đổi hoa thường,
/// đừng camelCase, đừng dịch.
/// </summary>
public static class AuthClaims
{
    /// <summary>ID người dùng, ví dụ <c>USR-001</c>.</summary>
    public const string Subject = "sub";

    /// <summary>MỘT chuỗi, đúng giá trị của BE-06 (<c>administrator</c>, <c>field_crew</c>...).</summary>
    public const string Role = "role";

    /// <summary>
    /// LUÔN là mảng, kể cả khi chỉ có một xã. Quản trị mang <c>["*"]</c> — mảng một phần tử,
    /// KHÔNG phải chuỗi <c>"*"</c>.
    /// </summary>
    public const string CommuneIds = "commune_ids";

    /// <summary>Giá trị đặc biệt cho phạm vi toàn hệ thống, Contract mục 7.</summary>
    public const string AllCommunes = "*";
}
