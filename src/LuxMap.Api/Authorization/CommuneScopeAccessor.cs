using System.Security.Claims;
using LuxMap.Modules.Identity.Auth;
using LuxMap.Persistence.Conventions;
using LuxMap.Shared.Authorization;
using LuxMap.Shared.Contracts.Enums;

namespace LuxMap.Api.Authorization;

/// <summary>
/// Derives the commune scope from the current request's <see cref="ClaimsPrincipal"/>.
/// </summary>
/// <remarks>
/// ⚠️ MUST be registered as a <b>singleton</b>, not scoped. EF Core builds the model ONCE and caches
/// it, and the query-filter expression holds a reference to whichever accessor instance existed at
/// model-build time. A scoped accessor would leave every later request reusing the first request's
/// scope — a silent leak, exactly the failure BE-08 exists to prevent.
/// A singleton reading <see cref="IHttpContextAccessor"/> still resolves the correct user per request.
/// </remarks>
public sealed class CommuneScopeAccessor(IHttpContextAccessor httpContextAccessor) : ICommuneScopeAccessor
{
    public CommuneScope Scope => FromPrincipal(httpContextAccessor.HttpContext?.User);

    /// <summary>
    /// Fails closed on every branch: unauthenticated, claim missing entirely, or claim present but
    /// empty all produce <see cref="CommuneScope.Empty"/> — none of them ever means "unrestricted".
    /// </summary>
    public static CommuneScope FromPrincipal(ClaimsPrincipal? principal)
    {
        if (principal?.Identity?.IsAuthenticated != true)
        {
            return CommuneScope.Empty;
        }

        // commune_ids is an ARRAY in the JWT, so it becomes SEVERAL claims of the same name in the
        // principal. FindAll is required; FindFirst would only ever see the first element.
        var communeIds = principal.FindAll(AuthClaims.CommuneIds).Select(claim => claim.Value).ToArray();

        if (communeIds.Length == 0)
        {
            return CommuneScope.Empty;
        }

        if (!communeIds.Contains(AuthClaims.AllCommunes, StringComparer.Ordinal))
        {
            return CommuneScope.ForCommunes(communeIds);
        }

        // Defence in depth: '*' only means anything when the role really is Administrator. The
        // mismatch case is already rejected by CommuneScopeConsistencyHandler; failing closed a
        // second time here covers anyone using the accessor outside the authorization pipeline.
        return IsAdministrator(principal) ? CommuneScope.SystemWide : CommuneScope.Empty;
    }

    public static bool IsAdministrator(ClaimsPrincipal principal)
        => string.Equals(
            principal.FindFirst(AuthClaims.Role)?.Value,
            ContractEnum.ToDbValue(UserRole.Administrator),
            StringComparison.Ordinal);

    public static bool HasWildcardClaim(ClaimsPrincipal principal)
        => principal.FindAll(AuthClaims.CommuneIds)
            .Any(claim => string.Equals(claim.Value, AuthClaims.AllCommunes, StringComparison.Ordinal));
}
