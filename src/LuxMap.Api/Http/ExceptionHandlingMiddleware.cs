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
            logger.LogWarning(
                known,
                "Lỗi nghiệp vụ {Code} trên {Path} (correlation {CorrelationId})",
                known.Code, context.Request.Path, correlation.CorrelationId);

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
