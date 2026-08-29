namespace LuxMap.Modules.Identity.Entities;

/// <summary>
/// Vì sao một refresh token bị thu hồi. Không phải enum của Contract mục 1 — đây là chi tiết
/// nội bộ, không bao giờ ra API.
/// </summary>
/// <remarks>
/// Phân biệt được lý do là điều kiện BẮT BUỘC để xử lý đúng việc dùng lại token:
/// retry lành tính sau khi xoay vòng phải im lặng, còn logout thì không bao giờ được coi là
/// tấn công.
/// </remarks>
public enum RefreshTokenRevocationReason
{
    /// <summary>Bị thay bằng token mới khi refresh. Trong cửa sổ ân hạn thì dùng lại là retry.</summary>
    Rotation,

    /// <summary>Người dùng chủ động đăng xuất. Dùng lại KHÔNG bao giờ kích hoạt thu hồi chuỗi.</summary>
    Logout,

    /// <summary>Thu hồi vì phát hiện dùng lại token đã xoay quá cửa sổ ân hạn.</summary>
    ReuseDetected,
}
