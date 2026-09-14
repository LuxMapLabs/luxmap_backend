using LuxMap.Modules.Identity.Auth.Web;
using Microsoft.Net.Http.Headers;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace LuxMap.Api.OpenApi;

/// <summary>
/// Documents the web refresh-token cookie (Contract section 2.10.3) on the operations that use it.
/// Swashbuckle cannot see the cookie on its own: it is neither a parameter nor a body field, so
/// without this filter the spec would show <c>web/refresh</c> as a call that takes nothing.
/// </summary>
public sealed class RefreshTokenCookieOperationFilter : IOperationFilter
{
    public const string SchemeId = "RefreshTokenCookie";

    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(context);

        var method = context.MethodInfo;

        if (method.IsDefined(typeof(ReadsRefreshTokenCookieAttribute), inherit: true))
        {
            // Replaces the document-wide Bearer requirement: these operations are authenticated by
            // the cookie, and never need an access token.
            operation.Security =
            [
                new OpenApiSecurityRequirement
                {
                    [new OpenApiSecuritySchemeReference(SchemeId, context.Document)] = [],
                },
            ];
        }

        if (operation.Responses is null)
        {
            return;
        }

        if (method.IsDefined(typeof(SetsRefreshTokenCookieAttribute), inherit: true))
        {
            foreach (var (status, response) in operation.Responses)
            {
                if (status.StartsWith('2') && response is OpenApiResponse concrete)
                {
                    concrete.Headers ??= new Dictionary<string, IOpenApiHeader>();
                    concrete.Headers[HeaderNames.SetCookie] = new OpenApiHeader
                    {
                        Description = status == "204"
                            ? $"Deletes {RefreshTokenCookie.Name}."
                            : $"Sets {RefreshTokenCookie.Name}: HttpOnly; Secure; SameSite=Lax; "
                              + $"Path={RefreshTokenCookie.Path}. Carries Expires only for remember_me = true.",
                        Schema = new OpenApiSchema { Type = JsonSchemaType.String },
                    };
                }
            }
        }

        if (method.DeclaringType?.IsDefined(typeof(RequireAllowedOriginAttribute), inherit: true) == true
            && operation.Responses.TryGetValue("403", out var forbidden)
            && forbidden is OpenApiResponse forbiddenResponse)
        {
            forbiddenResponse.Description =
                "ORIGIN_NOT_ALLOWED when Origin is missing, not on the allowlist, or null. "
                + "ACCOUNT_LOCKED on login and refresh when the account is locked.";
        }
    }
}
