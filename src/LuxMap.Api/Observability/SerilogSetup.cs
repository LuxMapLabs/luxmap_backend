using LuxMap.Api.Http;
using Serilog;
using Serilog.Events;

namespace LuxMap.Api.Observability;

public static class SerilogSetup
{
    private static int bootstrapped;

    /// <summary>
    /// Logger tối thiểu dùng trong lúc khởi động — nếu host chết trước khi đọc xong cấu hình
    /// thì vẫn có chỗ ghi lại lý do.
    /// </summary>
    /// <remarks>
    /// Dùng <c>CreateLogger</c> chứ KHÔNG phải <c>CreateBootstrapLogger</c>: bootstrap logger
    /// là loại reloadable, và <c>UseSerilog</c> sẽ gọi <c>Freeze()</c> lên nó mỗi lần dựng host.
    /// Dựng host lần thứ hai trong cùng process — đúng điều WebApplicationFactory làm khi chạy
    /// test — sẽ ném <c>The logger is already frozen</c>. Logger thường bị thay thế êm ru.
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
            // Bắt buộc cho correlation id: CorrelationIdMiddleware đẩy property vào LogContext,
            // enricher này mới là thứ đưa nó vào từng log entry.
            .Enrich.FromLogContext()
            .Enrich.With<SensitivePropertyScrubber>()
            .Enrich.WithProperty("Application", "LuxMap.Api"));
    }

    /// <summary>
    /// Một dòng tổng kết cho mỗi request: method, path, status code, thời gian xử lý.
    /// KHÔNG đưa header vào — Authorization sẽ lộ token.
    /// </summary>
    public static WebApplication UseLuxMapRequestLogging(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.UseSerilogRequestLogging(options =>
        {
            options.MessageTemplate =
                "{RequestMethod} {RequestPath} trả {StatusCode} trong {Elapsed:0.0000} ms";

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

                // Correlation id đã có sẵn qua LogContext; set thêm ở đây để dòng tổng kết
                // vẫn mang nó kể cả khi ai đó đổi thứ tự middleware.
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
