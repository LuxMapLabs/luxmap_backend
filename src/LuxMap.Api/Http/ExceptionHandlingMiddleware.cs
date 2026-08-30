using System.Net;
using LuxMap.Shared.Contracts.Errors;
using LuxMap.Shared.Http;

namespace LuxMap.Api.Http;

/// <summary>
/// Bắt mọi lỗi chưa xử lý và dựng đúng hình dạng
/// <c>{ error: { code, message, details } }</c> của Contract mục 0.
/// </summary>
public sealed class ExceptionHandlingMiddleware(
    RequestDelegate next,
    ILogger<ExceptionHandlingMiddleware> logger,
    IHostEnvironment environment)
{
    public async Task InvokeAsync(HttpContext context, CorrelationIdHolder correlation)
    {
        try
        {
            await next(context);
        }
        catch (LuxMapException known)
        {
            // KHÔNG truyền exception vào logger. Đây là lỗi nghiệp vụ ĐÃ LƯỜNG TRƯỚC — sai mật
            // khẩu, token hết hạn, bbox quá lớn — không phải sự cố ứng dụng. Kèm stack trace thì
            // mỗi lần người dùng gõ sai mật khẩu sinh ra gần 3.000 ký tự rác, che mất lỗi thật.
            // Mã lỗi, đường dẫn, phương thức và correlation id là đủ để điều tra.
            logger.Log(
                LevelFor(known.StatusCode),
                "Từ chối {Method} {Path}: {Code} — {Reason}",
                context.Request.Method, context.Request.Path, known.Code, known.Message);

            await WriteAsync(context, known.StatusCode, known.Code, known.Message, known.Details, correlation);
        }
        catch (Exception unexpected)
        {
            logger.LogError(
                unexpected,
                "Lỗi chưa xử lý trên {Path} (correlation {CorrelationId})",
                context.Request.Path, correlation.CorrelationId);

            // Thông điệp cố ý chung chung: chi tiết ngoại lệ chỉ lộ ở Development.
            // Tra bằng correlation id trong log, không đẩy stack trace ra cho client.
            var details = new Dictionary<string, object?>();
            if (environment.IsDevelopment())
            {
                details["exception"] = unexpected.GetType().FullName;
                details["exception_message"] = unexpected.Message;
            }

            await WriteAsync(
                context,
                HttpStatusCode.InternalServerError,
                ErrorCodes.InternalError,
                "Đã xảy ra lỗi không mong muốn. Gửi correlation id cho quản trị để tra log.",
                details,
                correlation);
        }
    }

    /// <summary>
    /// 4xx là hành vi bình thường của client nên chỉ Warning; 5xx mới là lỗi phía ta.
    /// </summary>
    private static LogLevel LevelFor(HttpStatusCode statusCode)
        => (int)statusCode >= 500 ? LogLevel.Error : LogLevel.Warning;

    internal static async Task WriteAsync(
        HttpContext context,
        HttpStatusCode statusCode,
        string code,
        string message,
        IReadOnlyDictionary<string, object?> details,
        CorrelationIdHolder correlation)
    {
        if (context.Response.HasStarted)
        {
            // Response đã gửi đi rồi thì không ghi đè được — chỉ còn cách cắt kết nối.
            context.Abort();
            return;
        }

        var payload = new Dictionary<string, object?>(details)
        {
            ["correlation_id"] = correlation.CorrelationId,
        };

        context.Response.Clear();
        context.Response.StatusCode = (int)statusCode;
        await context.Response.WriteAsJsonAsync(ApiErrorResponse.Create(code, message, payload));
    }
}
