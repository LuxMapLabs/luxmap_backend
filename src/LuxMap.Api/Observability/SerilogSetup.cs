using LuxMap.Api.Http;
using Serilog;
using Serilog.Events;

namespace LuxMap.Api.Observability;

public static class SerilogSetup
{
    private static int bootstrapped;

    /// <summary>
    /// A minimal logger for the startup window — if the host dies before configuration is read, there
    /// is still somewhere to record why.
    /// </summary>
    /// <remarks>
    /// Uses <c>CreateLogger</c> and NOT <c>CreateBootstrapLogger</c>: a bootstrap logger is reloadable
    /// and <c>UseSerilog</c> calls <c>Freeze()</c> on it every time a host is built. Building a second
    /// host in the same process — exactly what WebApplicationFactory does in tests — then throws
    /// <c>The logger is already frozen</c>. A plain logger is replaced silently.
    /// </remarks>
    public static void CreateBootstrapLogger()
    {
        if (Interlocked.Exchange(ref bootstrapped, 1) == 1)
        {
            return;
        }

        Log.Logger = new LoggerConfiguration()
            .WriteTo.Console()
            .CreateLogger();
    }

    public static void UseLuxMapSerilog(this WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Host.UseSerilog((context, _, configuration) => configuration
            .ReadFrom.Configuration(context.Configuration)
            // Required for the correlation id: CorrelationIdMiddleware pushes the property onto
            // LogContext, and this enricher is what copies it into each log entry.
            .Enrich.FromLogContext()
            .Enrich.With<SensitivePropertyScrubber>()
            .Enrich.WithProperty("Application", "LuxMap.Api"));
    }

    /// <summary>
    /// One summary line per request: method, path, status code, elapsed time.
    /// Headers are deliberately excluded — Authorization would leak the token.
    /// </summary>
    public static WebApplication UseLuxMapRequestLogging(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.UseSerilogRequestLogging(options =>
        {
            options.MessageTemplate =
                "{RequestMethod} {RequestPath} returned {StatusCode} in {Elapsed:0.0000} ms";

            options.GetLevel = static (httpContext, _, exception) => exception is not null
                ? LogEventLevel.Error
                : httpContext.Response.StatusCode switch
                {
                    >= 500 => LogEventLevel.Error,
                    >= 400 => LogEventLevel.Warning,
                    _ => LogEventLevel.Information,
                };

            options.EnrichDiagnosticContext = static (diagnostics, httpContext) =>
            {
                diagnostics.Set("RequestHost", httpContext.Request.Host.Value);
                diagnostics.Set("RequestScheme", httpContext.Request.Scheme);

                // The correlation id already arrives via LogContext; setting it here as well keeps
                // the summary line correct even if someone reorders the middleware.
                var correlation = httpContext.RequestServices.GetService<CorrelationIdHolder>();
                if (correlation is not null)
                {
                    diagnostics.Set("CorrelationId", correlation.CorrelationId);
                }
            };
        });

        return app;
    }
}
