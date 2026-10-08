using System.Net;
using Asp.Versioning;
using LuxMap.Shared.Authorization;
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
            // Contract section 0: the version lives in the URL (/api/v1), not in a header or query.
            options.ApiVersionReader = new UrlSegmentApiVersionReader();
        })
        .AddMvc()
        .AddApiExplorer(options =>
        {
            options.GroupNameFormat = "'v'VVV";
            // Substitutes the real number for {version} in the route so the spec reads /api/v1/...
            // rather than /api/v{version}/... — WP6 generates its URLs from that spec.
            options.SubstituteApiVersionInUrl = true;
        });

        // First, so no query-string date reaches the culture-dependent framework parser unchecked.
        services.AddControllers(options => options.ModelBinderProviders.Insert(0, new IsoDateQueryModelBinderProvider()));

        services.Configure<ApiBehaviorOptions>(options =>
        {
            // THIS IS THE TRAP: by default [ApiController] returns RFC 7807 ProblemDetails for
            // validation failures, which does NOT match the Contract's error shape. Without replacing
            // this factory the front end receives two different error shapes depending on the case.
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
                    .Select(error => Describe(error.ErrorMessage))
                    .ToArray());

        var correlation = context.HttpContext.RequestServices
            .GetRequiredService<CorrelationIdHolder>().CorrelationId;

        fields["correlation_id"] = correlation;

        return new ObjectResult(ApiErrorResponse.Create(
            ErrorCodes.ValidationFailed,
            "The submitted payload is invalid.",
            fields))
        {
            StatusCode = (int)HttpStatusCode.BadRequest,
        };
    }

    /// <summary>
    /// What a client is told about one invalid field. The JSON input formatter copies the serializer's own message,
    /// and that message names .NET types — "could not be converted to System.Nullable`1[LuxMap.Modules…TaskKind]".
    /// Internal type names are not the client's business, and they leak the module layout. Messages our converters
    /// write themselves ("'NaN' is not a number.") name no type and are kept.
    /// </summary>
    private static string Describe(string message)
        => string.IsNullOrWhiteSpace(message) ? "Invalid value."
            : message.Contains("System.", StringComparison.Ordinal) || message.Contains("LuxMap.", StringComparison.Ordinal)
                ? "The value is not valid for this field."
                : message;

    /// <summary>
    /// Field names in <c>details</c> must be snake_case to match the body the client sent;
    /// ModelState uses the C# property names (PascalCase).
    /// </summary>
    private static string ToSnakeCaseLower(this string key)
        => string.IsNullOrEmpty(key)
            ? key
            : string.Join('.', key.Split('.').Select(System.Text.Json.JsonNamingPolicy.SnakeCaseLower.ConvertName));
}

public static class ApiPipelineSetup
{
    /// <summary>
    /// Must run BEFORE request logging and before the exception handler: both need the correlation id.
    /// </summary>
    public static WebApplication UseLuxMapCorrelationId(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.UseMiddleware<CorrelationIdMiddleware>();

        return app;
    }

    /// <summary>
    /// Produces the Contract's error shape for thrown exceptions and for bare status codes
    /// (401, 403, 404, 405, 415).
    /// </summary>
    public static WebApplication UseLuxMapErrorHandling(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.UseMiddleware<ExceptionHandlingMiddleware>();
        app.UseStatusCodePages(HandleBareStatusCodeAsync);

        return app;
    }

    /// <summary>
    /// Unmatched routes, wrong methods and so on normally return an empty body. The Contract requires
    /// one shape for every API response, so we build <c>{ error: {...} }</c> for those too.
    /// </summary>
    private static async Task HandleBareStatusCodeAsync(StatusCodeContext context)
    {
        var response = context.HttpContext.Response;
        var correlation = context.HttpContext.RequestServices.GetRequiredService<CorrelationIdHolder>();

        var (code, message) = response.StatusCode switch
        {
            // BE-08 — ASP.NET Core returns an EMPTY BODY for 401/403; the Contract demands one shape
            // for every response, so they are rebuilt here.
            401 => (ErrorCodes.Unauthenticated, "Authentication required."),

            // The authorization middleware recorded which kind of refusal this was (D-4); with no
            // record the territorial code stays, as before.
            403 => (
                context.HttpContext.Items[Authorization.ForbiddenCodeResultHandler.ItemKey] as string
                    ?? ErrorCodes.CommuneForbidden,
                "You do not have access to this resource."),
            404 => ("NOT_FOUND", "Resource not found."),
            405 => ("METHOD_NOT_ALLOWED", "That method is not supported on this path."),
            415 => (ErrorCodes.UnsupportedMediaType, "Unsupported Content-Type."),
            _ => ("REQUEST_FAILED", "The request could not be completed."),
        };

        await ExceptionHandlingMiddleware.WriteAsync(
            context.HttpContext,
            (HttpStatusCode)response.StatusCode,
            code,
            message,
            ApiError.NoDetails,
            correlation,
            DeviceChallenge(response));
    }

    /// <summary>
    /// Keeps the device scheme's bare challenge through the rewrite (LIGHT-CTRL, RFC 9110 §11.6.1). ONLY that one: JwtBearer's
    /// challenge carries <c>error_description</c> ("the token expired", "the signature key was not found"), the very cause that
    /// <see cref="ErrorCodes.Unauthenticated"/> refuses to split — so a user's 401 stays without it, as before.
    /// </summary>
    private static string? DeviceChallenge(HttpResponse response)
        => response.StatusCode == StatusCodes.Status401Unauthorized && response.Headers.WWWAuthenticate == DeviceAuth.Scheme
            ? DeviceAuth.Scheme
            : null;
}
