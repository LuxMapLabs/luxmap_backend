using LuxMap.Modules.Identity.Auth;
using Microsoft.AspNetCore.Authorization;

namespace LuxMap.Api.Authorization;

/// <summary>
/// If the <c>commune_ids</c> claim carries <c>"*"</c>, the role must be Administrator.
/// </summary>
/// <remarks>
/// This is NOT protection against a forged client — <c>commune_ids</c> lives inside a signed JWT and
/// cannot be altered without the signing key. It guards against a BUG ON THE ISSUING SIDE: BE-06 has
/// no database constraint tying <c>has_system_wide_scope</c> to <c>role = 'administrator'</c>, so a
/// single manual UPDATE or a defect in BE-33 is enough to make BE-07 hand <c>["*"]</c> to an ordinary
/// account.
/// </remarks>
public sealed class CommuneScopeConsistencyRequirement : IAuthorizationRequirement;

public sealed class CommuneScopeConsistencyHandler(ILogger<CommuneScopeConsistencyHandler> logger)
    : AuthorizationHandler<CommuneScopeConsistencyRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        CommuneScopeConsistencyRequirement requirement)
    {
        var principal = context.User;

        if (principal.Identity?.IsAuthenticated != true)
        {
            // Not authenticated is RequireAuthenticatedUser's business; draw no conclusion here.
            return Task.CompletedTask;
        }

        if (CommuneScopeAccessor.HasWildcardClaim(principal) && !CommuneScopeAccessor.IsAdministrator(principal))
        {
            // Error rather than Warning: this signals a BUG on the token-issuing side, not an attack.
            logger.LogError(
                "Token for {Subject} carries commune_ids '*' but the role is {Role}, not Administrator. "
                + "Check has_system_wide_scope on that account in BE-06/BE-33.",
                principal.FindFirst(AuthClaims.Subject)?.Value ?? "(unknown)",
                principal.FindFirst(AuthClaims.Role)?.Value ?? "(unknown)");

            context.Fail(new AuthorizationFailureReason(this, "commune_ids '*' does not match the role."));
            return Task.CompletedTask;
        }

        context.Succeed(requirement);
        return Task.CompletedTask;
    }
}
