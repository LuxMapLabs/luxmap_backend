using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LuxMap.Api.Tests;

public sealed class LuxMapApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Production để chứng minh lỗi 500 KHÔNG lộ chi tiết ngoại lệ ra ngoài.
        builder.UseEnvironment("Production");

        builder.ConfigureServices(services =>
            services.AddControllers().AddApplicationPart(typeof(TestEndpointsController).Assembly));
    }
}

/// <summary>
/// Bản bật Swagger để kiểm chứng spec. Tách khỏi <see cref="LuxMapApiFactory"/> vì factory kia
/// cố tình chạy ở Production để chứng minh lỗi 500 không lộ chi tiết ngoại lệ.
/// </summary>
public sealed class LuxMapSwaggerFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("Swagger:Enabled", "true");

        builder.ConfigureServices(services =>
            services.AddControllers().AddApplicationPart(typeof(TestEndpointsController).Assembly));
    }
}
