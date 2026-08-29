using LuxMap.Modules.Identity.Auth;
using LuxMap.Modules.Identity.Seeding;
using LuxMap.Shared.Modularity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LuxMap.Modules.Identity;

/// <summary>
/// Module Identity — AppUser, AdministrativeUnit, RefreshToken, JWT, phân quyền theo vai trò
/// và địa bàn (BE-06..BE-08).
/// BE-07 phát access token và refresh token; việc KIỂM token là BE-08.
/// </summary>
public sealed class IdentityModule : ILuxMapModule
{
    public string Name => "Identity";

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddScoped<IdentitySeeder>();

        // Giá trị không bí mật lấy từ appsettings; khoá ký CHỈ lấy từ biến môi trường.
        var options = configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
            ?? new JwtOptions { SigningKey = string.Empty };

        options = options with
        {
            SigningKey = Environment.GetEnvironmentVariable(JwtOptions.SigningKeyEnvironmentVariable)
                ?? configuration[$"{JwtOptions.SectionName}:SigningKey"]
                ?? string.Empty,
        };

        // Thiếu khoá thì DỪNG ngay lúc khởi động, không lặng lẽ chạy với giá trị mặc định.
        options.Validate();

        services.AddSingleton(options);
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<AccessTokenIssuer>();
        services.AddScoped<AuthService>();
    }
}
