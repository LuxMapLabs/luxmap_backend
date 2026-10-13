using System.Globalization;
using System.Net;
using System.Threading.RateLimiting;
using LuxMap.Shared.Contracts.Errors;
using LuxMap.Shared.Http;
using Microsoft.AspNetCore.RateLimiting;

namespace LuxMap.Api.Http;

/// <summary>
/// ASP.NET Core's built-in rate limiter (no package), applied per endpoint with <c>[EnableRateLimiting]</c>.
/// </summary>
/// <remarks>
/// Partitioned by the connection's remote address. Behind a reverse proxy that address is the proxy's,
/// so every client would share one bucket: a deployment behind a proxy must configure forwarded headers
/// first. Limits come from configuration so a test can raise them; production keeps the defaults.
/// </remarks>
public static class RateLimitSetup
{
    public const string SectionName = "RateLimits:AccountMail";

    public static IServiceCollection AddLuxMapRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        var permits = configuration.GetValue($"{SectionName}:PermitLimit", 5);
        var window = TimeSpan.FromMinutes(configuration.GetValue($"{SectionName}:WindowMinutes", 15));

        services.AddRateLimiter(options =>
        {
            options.AddPolicy(LuxMapRateLimits.AccountMail, context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = permits, Window = window, QueueLimit = 0 }));

            // CITIZEN-QR P2b (D-8). Partitioned by the address the forwarded-headers middleware settled on — the proxy's own
            // address unless that proxy is configured as trusted (ForwardedHeadersSetup).
            AddPerAddress(options, configuration, LuxMapRateLimits.PublicLookup, "RateLimits:PublicLookup", 30, 10);
            AddPerAddress(options, configuration, LuxMapRateLimits.PublicReport, "RateLimits:PublicReport", 10, 60);

            // The Contract's error shape, not the middleware's bare 503 default. Written here rather than
            // through ExceptionHandlingMiddleware.WriteAsync: that clears the response, Retry-After included.
            options.OnRejected = async (rejected, ct) =>
            {
                var http = rejected.HttpContext;
                http.Response.StatusCode = (int)HttpStatusCode.TooManyRequests;
                // The lease knows when the window reopens; failing that, a whole window is never too early.
                var wait = rejected.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter) ? retryAfter : window;
                http.Response.Headers.RetryAfter = ((int)Math.Ceiling(wait.TotalSeconds)).ToString(CultureInfo.InvariantCulture);

                await http.Response.WriteAsJsonAsync(ApiErrorResponse.Create(
                    ErrorCodes.RateLimited,
                    "Too many requests from this address. Try again later.",
                    new Dictionary<string, object?>
                    {
                        ["correlation_id"] = http.RequestServices.GetRequiredService<CorrelationIdHolder>().CorrelationId,
                    }), ct);
            };
        });

        return services;
    }

    private static void AddPerAddress(
        RateLimiterOptions options, IConfiguration configuration, string policy, string section, int permits, int windowMinutes)
    {
        var limit = configuration.GetValue($"{section}:PermitLimit", permits);
        var window = TimeSpan.FromMinutes(configuration.GetValue($"{section}:WindowMinutes", windowMinutes));
        options.AddPolicy(policy, context => RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = limit, Window = window, QueueLimit = 0 }));
    }
}
