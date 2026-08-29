namespace LuxMap.Shared.Contracts.Errors;

/// <summary>
/// Các mã lỗi đã được đặc tả đích danh trong Contract v1.1. Chỉ chép lại những mã contract đã nêu —
/// mã mới phải vào contract trước, không tự thêm ở đây.
/// </summary>
public static class ErrorCodes
{
    /// <summary>413 — bbox phủ quá 2000 cột (mục 2.1).</summary>
    public const string BboxTooLarge = "BBOX_TOO_LARGE";

    /// <summary>404 — <c>pole_id</c> không tồn tại (mục 2.8).</summary>
    public const string PoleNotFound = "POLE_NOT_FOUND";

    /// <summary>400 — không có <c>pole_id</c> mà cũng không có <c>location</c> (mục 2.8).</summary>
    public const string LocationRequired = "LOCATION_REQUIRED";

    /// <summary>400 — <c>fault_type</c> thuộc nhóm chỉ engine sinh (mục 2.8).</summary>
    public const string FaultTypeNotReportable = "FAULT_TYPE_NOT_REPORTABLE";

    /// <summary>200 (KHÔNG phải lỗi) — <c>client_op_id</c> đã xử lý, trả lại bản ghi đã tạo (mục 2.8, 5.8).</summary>
    public const string DuplicateOp = "DUPLICATE_OP";

    /// <summary>403 — yêu cầu <c>commune_id</c> ngoài phạm vi claim (mục 7).</summary>
    public const string CommuneForbidden = "COMMUNE_FORBIDDEN";

    // ── Dưới đây KHÔNG có trong Contract v1.1 ────────────────────────────────
    // Hai mã hạ tầng, thêm ở BE-04 vì mọi API phải cùng một hình dạng lỗi.
    // Cần đưa vào Contract ở FW-00 rồi tăng version, đừng để FE tự đoán.

    /// <summary>400 — request không qua được validation. Chi tiết từng field nằm trong <c>details</c>.</summary>
    public const string ValidationFailed = "VALIDATION_FAILED";

    /// <summary>500 — lỗi chưa xử lý. Thông điệp cố ý chung chung, chi tiết chỉ có trong log theo correlation id.</summary>
    public const string InternalError = "INTERNAL_ERROR";

    // ── Xác thực (BE-07) — cũng KHÔNG có trong Contract v1.1 ────────────────
    // Contract chưa đặc tả nhóm endpoint /auth. Cần bổ sung ở FW-00.

    /// <summary>
    /// 401 — sai tài khoản HOẶC sai mật khẩu. Cố ý dùng CHUNG một mã cho cả hai:
    /// tách ra là nói cho kẻ tấn công biết tài khoản nào tồn tại.
    /// </summary>
    public const string InvalidCredentials = "INVALID_CREDENTIALS";

    /// <summary>403 — đúng mật khẩu nhưng tài khoản đang bị khoá.</summary>
    public const string AccountLocked = "ACCOUNT_LOCKED";

    /// <summary>
    /// 401 — refresh token sai, hết hạn, đã thu hồi, hoặc bị dùng lại. Cũng cố ý dùng CHUNG
    /// một mã: phân biệt ra là giúp kẻ tấn công dò xem token nào từng tồn tại.
    /// </summary>
    public const string InvalidRefreshToken = "INVALID_REFRESH_TOKEN";
}
