using System.Net;
using Asp.Versioning;
using LuxMap.Shared.Contracts;
using LuxMap.Shared.Contracts.Errors;
using LuxMap.Shared.Http;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace LuxMap.Api.Http;

public static class ApiConventionsSetup
{
    public static IServiceCollection AddLuxMapApiConventions(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<CorrelationIdHolder>();
        services.AddScoped<ICorrelationIdAccessor>(sp => sp.GetRequiredService<CorrelationIdHolder>());

        services.AddApiVersioning(options =>
        {
            options.DefaultApiVersion = new ApiVersion(1, 0);
            options.AssumeDefaultVersionWhenUnspecified = true;
            options.ReportApiVersions = true;
            // Contract mục 0: version nằm trong URL (/api/v1), không phải header hay query.
            options.ApiVersionReader = new UrlSegmentApiVersionReader();
        })
        .AddMvc();

        services.AddControllers();

        services.Configure<ApiBehaviorOptions>(options =>
        {
            // ĐÂY LÀ CÁI BẪY: [ApiController] mặc định trả RFC 7807 ProblemDetails cho lỗi
            // validation, KHÔNG khớp hình dạng error của Contract. Không thay factory này
            // thì FE nhận hai hình dạng lỗi khác nhau tuỳ tình huống.
            options.InvalidModelStateResponseFactory = BuildValidationErrorResponse;
        });

        return services;
    }

    private static IActionResult BuildValidationErrorResponse(ActionContext context)
    {
        var fields = context.ModelState
            .Where(entry => entry.Value?.Errors.Count > 0)
            .ToDictionary(
                entry => entry.Key.ToSnakeCaseLower(),
                entry => (object?)entry.Value!.Errors
                    .Select(error => string.IsNullOrWhiteSpace(error.ErrorMessage)
                        ? "Giá trị không hợp lệ."
                        : error.ErrorMessage)
                    .ToArray());

        var correlation = context.HttpContext.RequestServices
            .GetRequiredService<CorrelationIdHolder>().CorrelationId;

        fields["correlation_id"] = correlation;

        return new ObjectResult(ApiErrorResponse.Create(
            ErrorCodes.ValidationFailed,
            "Dữ liệu gửi lên không hợp lệ.",
            fields))
        {
            StatusCode = (int)HttpStatusCode.BadRequest,
        };
    }

    /// <summary>
    /// Tên field trong <c>details</c> phải snake_case cho khớp với body mà client gửi lên;
    /// ModelState dùng tên property C# (PascalCase).
    /// </summary>
    private static string ToSnakeCaseLower(this string key)
        => string.IsNullOrEmpty(key)
            ? key
            : string.Join('.', key.Split('.').Select(System.Text.Json.JsonNamingPolicy.SnakeCaseLower.ConvertName));
}

public static class ApiPipelineSetup
{
    /// <summary>
    /// Thứ tự bắt buộc: correlation id phải chạy TRƯỚC exception handler, vì handler cần
    /// id để đưa vào body lỗi.
    /// </summary>
    public static WebApplication UseLuxMapApiConventions(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.UseMiddleware<CorrelationIdMiddleware>();
        app.UseMiddleware<ExceptionHandlingMiddleware>();
        app.UseStatusCodePages(HandleBareStatusCodeAsync);

        return app;
    }

    /// <summary>
    /// Route không khớp, method sai... vốn trả body rỗng. Contract yêu cầu mọi API cùng một
    /// hình dạng, nên dựng luôn <c>{ error: {...} }</c> cho các status trần này.
    /// </summary>
    private static async Task HandleBareStatusCodeAsync(StatusCodeContext context)
    {
        var response = context.HttpContext.Response;
        var correlation = context.HttpContext.RequestServices.GetRequiredService<CorrelationIdHolder>();

        var (code, message) = response.StatusCode switch
        {
            404 => ("NOT_FOUND", "Không tìm thấy tài nguyên."),
            405 => ("METHOD_NOT_ALLOWED", "Phương thức không được hỗ trợ trên đường dẫn này."),
            415 => ("UNSUPPORTED_MEDIA_TYPE", "Content-Type không được hỗ trợ."),
            _ => ("REQUEST_FAILED", "Yêu cầu không thực hiện được."),
        };

        await ExceptionHandlingMiddleware.WriteAsync(
            context.HttpContext,
            (HttpStatusCode)response.StatusCode,
            code,
            message,
            ApiError.NoDetails,
            correlation);
    }
}
