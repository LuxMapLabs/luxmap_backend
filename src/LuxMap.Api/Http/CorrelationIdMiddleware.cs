using LuxMap.Shared.Contracts;
using LuxMap.Shared.Http;

namespace LuxMap.Api.Http;

/// <summary>
/// Nhận correlation id từ client nếu có, không thì tự sinh. Luôn trả lại ở response header
/// cho MỌI response — 2xx lẫn lỗi — để FE và log nối được cùng một request.
/// </summary>
public sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    /// <summary>Chặn header của client dùng làm vector chèn dữ liệu rác vào log.</summary>
    private const int MaxLength = 128;

    public async Task InvokeAsync(HttpContext context, CorrelationIdHolder holder)
    {
        holder.CorrelationId = Sanitize(context.Request.Headers[ApiHeaders.CorrelationId].ToString());

        context.Response.OnStarting(() =>
        {
            context.Response.Headers[ApiHeaders.CorrelationId] = holder.CorrelationId;
            return Task.CompletedTask;
        });

        using (var scope = BeginLogScope(context, holder.CorrelationId))
        {
            await next(context);
        }
    }

    private static IDisposable? BeginLogScope(HttpContext context, string correlationId)
        => context.RequestServices
            .GetService<ILoggerFactory>()?
            .CreateLogger<CorrelationIdMiddleware>()
            .BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId });

    private static string Sanitize(string? incoming)
    {
        if (string.IsNullOrWhiteSpace(incoming))
        {
            return Guid.NewGuid().ToString("D");
        }

        var trimmed = incoming.Trim();
        if (trimmed.Length > MaxLength)
        {
            trimmed = trimmed[..MaxLength];
        }

        // Chỉ giữ ký tự an toàn cho header và log; chuỗi lạ thì bỏ, sinh id mới.
        foreach (var c in trimmed)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_' or '.' or ':'))
            {
                return Guid.NewGuid().ToString("D");
            }
        }

        return trimmed;
    }
}

/// <summary>Giữ correlation id trong phạm vi một request.</summary>
public sealed class CorrelationIdHolder : ICorrelationIdAccessor
{
    public string CorrelationId { get; set; } = string.Empty;
}
