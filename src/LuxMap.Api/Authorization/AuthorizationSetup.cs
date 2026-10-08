using System.Text;
using LuxMap.Modules.Identity.Auth;
using LuxMap.Persistence.Conventions;
using LuxMap.Shared.Authorization;
using LuxMap.Shared.Contracts.Enums;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace LuxMap.Api.Authorization;

public static class AuthorizationSetup
{
    public static IServiceCollection AddLuxMapAuthorization(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddHttpContextAccessor();

        // Singleton — mandatory. See the note on CommuneScopeAccessor.
        services.AddSingleton<ICommuneScopeAccessor, CommuneScopeAccessor>();
        services.AddSingleton<ICurrentActorAccessor, CurrentActorAccessor>();
        services.AddSingleton<IAuthorizationHandler, CommuneScopeConsistencyHandler>();

        // Records WHY a request was forbidden, so the BE-04 status-code page can tell a refused role
        // (ROLE_FORBIDDEN) from a territorial refusal (COMMUNE_FORBIDDEN).
        services.AddSingleton<IAuthorizationMiddlewareResultHandler, ForbiddenCodeResultHandler>();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer()
            // LIGHT-CTRL LC-2: an IoT device's own scheme. NOT the default — only endpoints naming DeviceAuth.Policy use it,
            // so a Device header sent to a user endpoint is never read, and a Bearer token never satisfies a device endpoint.
            .AddScheme<AuthenticationSchemeOptions, DeviceAuthenticationHandler>(DeviceAuth.Scheme, null)
            // LC-12: the MQTT broker calling back to authenticate a client. Same shape — not the default, own policy only.
            .AddScheme<AuthenticationSchemeOptions, BrokerAuthenticationHandler>(BrokerAuth.Scheme, null);

        // Resolve JwtOptions through DI so validation is configured from the SAME object BE-07 signs
        // with — there is no opportunity for the issuer, audience or key to drift apart.
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<JwtOptions>(ConfigureJwtBearer);

        var authorization = services.AddAuthorizationBuilder()
            // Fail CLOSED: the whole application requires authentication by default; opening an
            // endpoint requires an explicit [AllowAnonymous].
            .SetDefaultPolicy(new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .AddRequirements(new CommuneScopeConsistencyRequirement())
                .Build())
            .SetFallbackPolicy(new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .AddRequirements(new CommuneScopeConsistencyRequirement())
                .Build());

        // The device policy: the Device scheme ONLY, authenticated, carrying a node id. It is deliberately outside the
        // capability matrix — a device has no role, so no capability admits it, and this policy admits nothing else.
        authorization.AddPolicy(DeviceAuth.Policy, builder => builder
            .AddAuthenticationSchemes(DeviceAuth.Scheme)
            .RequireAuthenticatedUser()
            .RequireClaim(DeviceAuth.NodeIdClaim));

        // The broker policy: the Broker scheme only — outside the matrix for the same reason.
        authorization.AddPolicy(BrokerAuth.Policy, builder => builder
            .AddAuthenticationSchemes(BrokerAuth.Scheme)
            .RequireAuthenticatedUser()
            .RequireClaim(BrokerAuth.Claim));

        // One policy per capability, straight from the matrix — no policy exists that is not in it.
        foreach (var (policy, roles) in LuxMapPolicies.Matrix)
        {
            // 🔴 An EMPTY list does not mean "nobody". RequireClaim with no allowed values only asks
            // that a role claim EXISTS, so it admits every signed-in role — found by removing the only
            // role of ManageAssets and watching the superior create a segment. Refuse to start instead.
            if (roles.Count == 0)
            {
                throw new InvalidOperationException(
                    $"Capability '{policy}' names no role. An empty RequireClaim admits EVERY role; "
                    + "remove the capability instead of emptying it.");
            }

            authorization.AddPolicy(policy, CapabilityPolicy(roles));
        }

        return services;
    }

    /// <summary>
    /// Admits exactly the listed roles. <c>RequireClaim</c> with several values is an OR over those
    /// values and nothing else — there is no ordering between roles, so no "and above".
    /// </summary>
    private static Action<AuthorizationPolicyBuilder> CapabilityPolicy(IReadOnlyList<UserRole> roles)
        => builder => builder
            .RequireAuthenticatedUser()
            .AddRequirements(new CommuneScopeConsistencyRequirement())
            .RequireClaim(AuthClaims.Role, roles.Select(ContractEnum.ToDbValue));

    private static void ConfigureJwtBearer(JwtBearerOptions options, JwtOptions jwt)
    {
        // ⚠️ The default handler RENAMES inbound claims: sub becomes
        // http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier.
        // Leave this on and User.FindFirst("sub") ALWAYS returns null.
        options.MapInboundClaims = false;

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt.Issuer,

            ValidateAudience = true,
            ValidAudience = jwt.Audience,

            ValidateLifetime = true,

            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),

            // Accept only the algorithm BE-07 actually signs with.
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],

            // .NET defaults to 5 MINUTES. With a 60-minute access token, 5 minutes of slack is far
            // too generous.
            ClockSkew = TimeSpan.FromSeconds(30),

            // Claim names exactly as BE-07 issues them, not the WS-Federation URI schema.
            NameClaimType = AuthClaims.Subject,
            RoleClaimType = AuthClaims.Role,
        };
    }
}
