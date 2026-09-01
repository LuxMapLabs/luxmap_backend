using LuxMap.Shared.Contracts;
using LuxMap.Shared.Http;

namespace LuxMap.Api.Http;

/// <summary>
/// Accepts the client's correlation id when present, generates one otherwise. Always echoes it back
/// on EVERY response — 2xx and errors alike — so the front end and the logs can be tied together.
/// </summary>
public sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    /// <summary>Stops the client header being used to push junk into the logs.</summary>
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

        // Only characters that are safe in a header and in a log line; anything else is discarded
        // and replaced with a fresh id.
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

/// <summary>Holds the correlation id for the lifetime of one request.</summary>
public sealed class CorrelationIdHolder : ICorrelationIdAccessor
{
    public string CorrelationId { get; set; } = string.Empty;
}
