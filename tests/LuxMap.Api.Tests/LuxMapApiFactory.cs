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
