using LuxMap.Modules.Identity.Seeding;
using LuxMap.Shared.Modularity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LuxMap.Modules.Identity;

/// <summary>
/// Module Identity — AppUser, AdministrativeUnit, RefreshToken, JWT, phân quyền theo vai trò
/// và địa bàn (BE-06..BE-08).
/// BE-06 mới có entity, migration và seed; chưa có endpoint và chưa có logic auth.
/// </summary>
public sealed class IdentityModule : ILuxMapModule
{
    public string Name => "Identity";

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IdentitySeeder>();
    }
}
