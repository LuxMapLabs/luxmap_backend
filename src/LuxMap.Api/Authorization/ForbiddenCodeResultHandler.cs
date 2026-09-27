using LuxMap.Shared.Contracts.Errors;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Authorization.Policy;

namespace LuxMap.Api.Authorization;

/// <summary>
/// Decides WHICH 403 the status-code page renders (BE-REVIEW-02, D-4): <c>ROLE_FORBIDDEN</c> when
/// a role policy refused the caller, <c>COMMUNE_FORBIDDEN</c> for everything else.
/// </summary>
/// <remarks>
/// ASP.NET Core answers a policy failure with an EMPTY 403 and the BE-04 status-code page rebuilds
/// the Contract shape from the status alone — which is how every refused role came out as
/// <c>COMMUNE_FORBIDDEN</c>, a code Contract section 7 reserves for a <c>commune_id</c> outside the
/// caller's scope. The authorization middleware is the only place that still knows WHY the request
/// was refused, so the reason is recorded here on <see cref="HttpContext.Items"/> and read back by
/// <c>ApiPipelineSetup.HandleBareStatusCodeAsync</c>. The default handler still writes the response;
/// nothing about the pipeline order changes.
/// <para>
/// A capability policy carries one <see cref="ClaimsAuthorizationRequirement"/>
/// (<c>RequireClaim(role, …)</c> over the roles of <c>LuxMapPolicies.Matrix</c>). The other
/// requirement on every policy, <see cref="CommuneScopeConsistencyRequirement"/>, fails only when a
/// token carries <c>["*"]</c> without the system_admin role — that is an issuing-side
/// bug about territory, so it keeps <c>COMMUNE_FORBIDDEN</c>.
/// </para>
/// </remarks>
public sealed class ForbiddenCodeResultHandler : IAuthorizationMiddlewareResultHandler
{
    /// <summary>The <see cref="HttpContext.Items"/> key the status-code page reads.</summary>
    public const string ItemKey = "LuxMap:ForbiddenCode";

    private readonly AuthorizationMiddlewareResultHandler defaultHandler = new();

    public Task HandleAsync(
        RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult authorizeResult)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(authorizeResult);

        if (authorizeResult.Forbidden)
        {
            context.Items[ItemKey] = CodeFor(authorizeResult.AuthorizationFailure);
        }

        return defaultHandler.HandleAsync(next, context, policy, authorizeResult);
    }

    /// <summary>
    /// <c>ROLE_FORBIDDEN</c> if a claim requirement is among the failed ones; the territorial code
    /// otherwise, including when the failure carries no requirement detail at all.
    /// </summary>
    public static string CodeFor(AuthorizationFailure? failure)
        => failure?.FailedRequirements.Any(requirement => requirement is ClaimsAuthorizationRequirement) == true
            ? ErrorCodes.RoleForbidden
            : ErrorCodes.CommuneForbidden;
}
