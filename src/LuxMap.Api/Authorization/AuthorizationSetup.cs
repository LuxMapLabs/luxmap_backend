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

/// <summary>Tên policy theo vai trò. Dùng hằng số, không rải chuỗi ma thuật khắp code.</summary>
public static class LuxMapPolicies
{
    public const string ManagementAgency = "role:management_agency";
    public const string MaintenanceEngineer = "role:maintenance_engineer";
    public const string FieldCrew = "role:field_crew";
    public const string Administrator = "role:administrator";

    public static string For(UserRole role) => role switch
    {
        UserRole.ManagementAgency => ManagementAgency,
        UserRole.MaintenanceEngineer => MaintenanceEngineer,
        UserRole.FieldCrew => FieldCrew,
        UserRole.Administrator => Administrator,
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, "Vai trò chưa có policy."),
    };
}

public static class AuthorizationSetup
{
    public static IServiceCollection AddLuxMapAuthorization(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddHttpContextAccessor();

        // Singleton — bắt buộc. Xem chú thích ở CommuneScopeAccessor.
        services.AddSingleton<ICommuneScopeAccessor, CommuneScopeAccessor>();
        services.AddSingleton<IAuthorizationHandler, CommuneScopeConsistencyHandler>();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer();

        // Lấy JwtOptions qua DI để cấu hình kiểm token TRÙNG KHÍT với cấu hình phát token của
        // BE-07 — cùng một object, không có cơ hội lệch issuer/audience/khoá.
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<JwtOptions>(ConfigureJwtBearer);

        services.AddAuthorizationBuilder()
            // Fail ĐÓNG: mặc định toàn ứng dụng là phải xác thực; mở ra phải khai [AllowAnonymous].
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
        // ⚠️ Handler mặc định ĐỔI TÊN claim khi đọc vào: sub thành
        // http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier.
        // Không tắt thì User.FindFirst("sub") LUÔN trả null.
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

            // Chỉ chấp nhận đúng thuật toán BE-07 dùng để ký.
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],

            // Mặc định của .NET là 5 PHÚT. Access token sống 60 phút mà cho lệch 5 phút là quá rộng.
            ClockSkew = TimeSpan.FromSeconds(30),

            // Tên claim đúng như BE-07 phát ra, không dùng URI schema của WS-Federation.
            NameClaimType = AuthClaims.Subject,
            RoleClaimType = AuthClaims.Role,
        };
    }
}
