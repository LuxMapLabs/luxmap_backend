using LuxMap.Shared.Modularity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LuxMap.Modules.Admin;

/// <summary>
/// Module Admin — Quản trị danh mục, ngưỡng, node, model version, dashboard (BE-28..BE-35).
/// Khung rỗng ở BE-01: chưa có entity, chưa có endpoint.
/// </summary>
public sealed class AdminModule : ILuxMapModule
{
    public string Name => "Admin";

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
    }
}
