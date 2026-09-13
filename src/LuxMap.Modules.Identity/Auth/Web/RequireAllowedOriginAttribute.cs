using LuxMap.Shared.Contracts.Errors;
using LuxMap.Shared.Http;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;

namespace LuxMap.Modules.Identity.Auth.Web;

/// <summary>
/// <c>403 ORIGIN_NOT_ALLOWED</c> unless the request carries an <c>Origin</c> header on the allowlist
/// (Contract section 2.10.2). Runs as an authorization filter, before model binding, so a request
/// from anywhere else is refused before its body is even read.
/// </summary>
/// <remarks>
/// SEPARATE from CORS, and both are needed: CORS decides whether a browser may READ the response;
/// this decides whether the request is PROCESSED at all. CORS alone would still run a sign-in and set
/// a cookie for a page it then merely hides the answer from.
/// <para>
/// The allowlist is read from the host's DEFAULT CORS policy rather than from configuration a second
/// time, so there is one list and the two checks cannot disagree. A host with no default policy is
/// misconfigured: that fails closed with a 500, never open.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class RequireAllowedOriginAttribute : Attribute, IAsyncAuthorizationFilter
{
    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var httpContext = context.HttpContext;
        var origin = httpContext.Request.Headers.Origin.ToString();

        var policy = await httpContext.RequestServices.GetRequiredService<ICorsPolicyProvider>()
                .GetPolicyAsync(httpContext, policyName: null)
            ?? throw new InvalidOperationException(
                "No default CORS policy is registered, so the web auth endpoints cannot check Origin.");

        // "null" is what a sandboxed iframe or a file:// page sends: it names no origin at all.
        if (origin.Length == 0
            || string.Equals(origin, "null", StringComparison.Ordinal)
            || !policy.IsOriginAllowed(origin))
        {
            throw new LuxMapException(
                KnownErrors.OriginNotAllowed.Code,
                KnownErrors.OriginNotAllowed.StatusCode,
                "This origin may not call the web sign-in endpoints.");
        }
    }
}
