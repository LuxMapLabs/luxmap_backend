using System.Net;
using LuxMap.Api.Http;
using LuxMap.Shared.Contracts.Errors;
using LuxMap.Shared.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LuxMap.Api.Tests;

/// <summary>
/// Lỗi nghiệp vụ đã lường trước KHÔNG được kèm stack trace. Xác thực thất bại là chuyện bình
/// thường; mỗi lần gõ sai mật khẩu sinh ~2.800 ký tự stack trace thì log thật bị che mất.
/// </summary>
public class ExpectedErrorLoggingTests
{
    private sealed record Entry(LogLevel Level, Exception? Exception, string Message);

    private sealed class CapturingLogger : ILogger<ExceptionHandlingMiddleware>
    {
        public List<Entry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
            => Entries.Add(new Entry(logLevel, exception, formatter(state, exception)));
    }

    private sealed class StubEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "test";
        public string ContentRootPath { get; set; } = ".";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }

    private static async Task<CapturingLogger> RunAsync(Exception thrown)
    {
        var logger = new CapturingLogger();
        var middleware = new ExceptionHandlingMiddleware(
            _ => throw thrown, logger, new StubEnvironment());

        var context = new DefaultHttpContext { Response = { Body = new MemoryStream() } };
        context.Request.Method = "POST";
        context.Request.Path = "/api/v1/auth/login";
        context.RequestServices = new EmptyServiceProvider();

        await middleware.InvokeAsync(context, new CorrelationIdHolder { CorrelationId = "test-corr" });
        return logger;
    }

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, ErrorCodes.InvalidCredentials)]
    [InlineData(HttpStatusCode.Unauthorized, ErrorCodes.InvalidRefreshToken)]
    [InlineData(HttpStatusCode.Forbidden, ErrorCodes.AccountLocked)]
    [InlineData(HttpStatusCode.RequestEntityTooLarge, ErrorCodes.BboxTooLarge)]
    public async Task Expected_business_errors_are_logged_without_a_stack_trace(
        HttpStatusCode statusCode, string code)
    {
        var logger = await RunAsync(new LuxMapException(code, statusCode, "lý do từ chối"));

        var entry = Assert.Single(logger.Entries);
        Assert.Null(entry.Exception);                       // ← không stack trace
        Assert.Equal(LogLevel.Warning, entry.Level);

        // Vẫn phải điều tra được: mã lỗi, lý do, method và path đều còn.
        Assert.Contains(code, entry.Message, StringComparison.Ordinal);
        Assert.Contains("lý do từ chối", entry.Message, StringComparison.Ordinal);
        Assert.Contains("/api/v1/auth/login", entry.Message, StringComparison.Ordinal);
        Assert.Contains("POST", entry.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Server_side_business_errors_are_logged_at_error_level()
    {
        var logger = await RunAsync(new LuxMapException(
            "SOMETHING_BROKE", HttpStatusCode.InternalServerError, "hỏng phía server"));

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.Null(entry.Exception);
    }

    [Fact]
    public async Task Unexpected_exceptions_keep_their_stack_trace()
    {
        var boom = new InvalidOperationException("lỗi thật ngoài dự kiến");
        var logger = await RunAsync(boom);

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.Same(boom, entry.Exception);                 // ← vẫn có stack trace
    }
}
