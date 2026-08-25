namespace LuxMap.Shared.Contracts.Errors;

/// <summary>
/// Contract v1.1 mục 0 — hình dạng lỗi đã chốt: <c>{ "error": { "code", "message", "details" } }</c>.
/// Correlation id đi ở header <see cref="ApiHeaders.CorrelationId"/>, không nằm trong body:
/// body giữ đúng ba khoá đã publish.
/// </summary>
public sealed record ApiErrorResponse(ApiError Error)
{
    public static ApiErrorResponse Create(
        string code,
        string message,
        IReadOnlyDictionary<string, object?>? details = null)
        => new(new ApiError(code, message, details ?? ApiError.NoDetails));
}

/// <param name="Code">Mã ổn định để client rẽ nhánh, ví dụ <c>BBOX_TOO_LARGE</c>. Xem <see cref="ErrorCodes"/>.</param>
/// <param name="Message">Câu mô tả cho người đọc. Client không được parse.</param>
/// <param name="Details">Bag tuỳ ngữ cảnh; luôn có mặt, rỗng thì là <c>{}</c>.</param>
public sealed record ApiError(
    string Code,
    string Message,
    IReadOnlyDictionary<string, object?> Details)
{
    public static readonly IReadOnlyDictionary<string, object?> NoDetails =
        new Dictionary<string, object?>();
}
