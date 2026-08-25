using System.Net;
using LuxMap.Shared.Contracts.Errors;

namespace LuxMap.Shared.Http;

/// <summary>
/// Lỗi nghiệp vụ đã biết trước, mang sẵn mã và HTTP status của Contract.
/// Ném cái này thay vì trả về lỗi tại chỗ — middleware sẽ dựng đúng hình dạng
/// <see cref="ApiErrorResponse"/> ở một chỗ duy nhất.
/// </summary>
public class LuxMapException(
    string code,
    HttpStatusCode statusCode,
    string message,
    IReadOnlyDictionary<string, object?>? details = null)
    : Exception(message)
{
    public string Code { get; } = code;

    public HttpStatusCode StatusCode { get; } = statusCode;

    public IReadOnlyDictionary<string, object?> Details { get; } = details ?? ApiError.NoDetails;
}
