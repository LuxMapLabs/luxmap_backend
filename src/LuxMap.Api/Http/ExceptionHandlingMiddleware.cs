using System.Net;
using LuxMap.Shared.Contracts.Errors;
using LuxMap.Shared.Http;

namespace LuxMap.Api.Http;

/// <summary>
/// Catches everything that escapes and renders the Contract's error shape
/// <c>{ error: { code, message, details } }</c> (section 0).
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
            // Do NOT pass the exception to the logger. These are ANTICIPATED business failures —
            // wrong password, expired token, bbox too large — not application faults. Attaching a
            // stack trace means every mistyped password writes ~3,000 characters of noise that
            // buries the real errors. Code, path, method and correlation id are enough to investigate.
            logger.Log(
                LevelFor(known.StatusCode),
                "Rejected {Method} {Path}: {Code} — {Reason}",
                context.Request.Method, context.Request.Path, known.Code, known.Message);

            await WriteAsync(context, known.StatusCode, known.Code, known.Message, known.Details, correlation);
        }
        catch (Exception unexpected)
        {
            logger.LogError(
                unexpected,
                "Unhandled failure on {Path} (correlation {CorrelationId})",
                context.Request.Path, correlation.CorrelationId);

            // The message is deliberately generic: exception detail is exposed only in Development.
            // Look it up in the logs by correlation id; never push a stack trace to the client.
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
                "An unexpected error occurred. Send the correlation id to an administrator to trace it.",
                details,
                correlation);
        }
    }

    /// <summary>
    /// 4xx is normal client behaviour, so Warning; only 5xx is our own failure.
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
            // The response is already on the wire and cannot be rewritten — aborting is all that is left.
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
