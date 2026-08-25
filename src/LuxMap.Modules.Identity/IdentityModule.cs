using LuxMap.Shared.Modularity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LuxMap.Modules.Identity;

/// <summary>
/// Module Identity — AppUser, AdministrativeUnit, JWT, phân quyền theo vai trò và địa bàn (BE-06..BE-08).
/// Khung rỗng ở BE-01: chưa có entity, chưa có endpoint.
/// </summary>
public sealed class IdentityModule : ILuxMapModule
{
    public string Name => "Identity";

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
    }
}
