namespace LuxMap.Shared.Contracts;

public static class ApiHeaders
{
    /// <summary>
    /// Correlation id đi trên MỌI response (2xx lẫn lỗi), không nhét vào body —
    /// hình dạng <c>{ error: { code, message, details } }</c> đã publish, không thêm khoá.
    /// Middleware gắn header là việc của BE-04.
    /// </summary>
    public const string CorrelationId = "X-Correlation-Id";
}
