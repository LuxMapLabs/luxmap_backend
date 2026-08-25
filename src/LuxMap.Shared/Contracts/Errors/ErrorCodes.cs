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
}
