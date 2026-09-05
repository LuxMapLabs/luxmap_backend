using System.Text;
using LuxMap.Modules.Identity.Auth;
using LuxMap.Persistence.Conventions;
using LuxMap.Shared.Authorization;
using LuxMap.Shared.Contracts.Enums;
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
        services.AddSingleton<IAuthorizationHandler, CommuneScopeConsistencyHandler>();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer();

        // Resolve JwtOptions through DI so validation is configured from the SAME object BE-07 signs
        // with — there is no opportunity for the issuer, audience or key to drift apart.
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<JwtOptions>(ConfigureJwtBearer);

        services.AddAuthorizationBuilder()
            // Fail CLOSED: the whole application requires authentication by default; opening an
            // endpoint requires an explicit [AllowAnonymous].
            .SetDefaultPolicy(new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .AddRequirements(new CommuneScopeConsistencyRequirement())
                .Build())
            .SetFallbackPolicy(new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .AddRequirements(new CommuneScopeConsistencyRequirement())
                .Build())
            .AddPolicy(LuxMapPolicies.ManagementAgency, RolePolicy(UserRole.ManagementAgency))
            .AddPolicy(LuxMapPolicies.MaintenanceEngineer, RolePolicy(UserRole.MaintenanceEngineer))
            .AddPolicy(LuxMapPolicies.FieldCrew, RolePolicy(UserRole.FieldCrew))
            .AddPolicy(LuxMapPolicies.Administrator, RolePolicy(UserRole.Administrator));

        return services;
    }

    private static Action<AuthorizationPolicyBuilder> RolePolicy(UserRole role)
        => builder => builder
            .RequireAuthenticatedUser()
            .AddRequirements(new CommuneScopeConsistencyRequirement())
            .RequireClaim(AuthClaims.Role, ContractEnum.ToDbValue(role));

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
