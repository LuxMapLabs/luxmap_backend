using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LuxMap.Shared.Modularity;

/// <summary>
/// Một module nghiệp vụ của monolith. Mỗi module TỰ đăng ký service của mình —
/// host chỉ liệt kê module, không biết bên trong module có gì.
/// </summary>
public interface ILuxMapModule
{
    /// <summary>Tên hiển thị trong log khởi động, ví dụ <c>Assets</c>.</summary>
    string Name { get; }

    void RegisterServices(IServiceCollection services, IConfiguration configuration);

    /// <summary>
    /// Gắn endpoint của module. Mặc định không làm gì — module chưa có endpoint thì không cần override.
    /// </summary>
    void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
    }
}
