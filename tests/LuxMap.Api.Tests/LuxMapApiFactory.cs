using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LuxMap.Api.Tests;

public sealed class LuxMapApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Production, to prove a 500 does NOT leak exception detail to the client.
        builder.UseEnvironment("Production");

        builder.ConfigureServices(services =>
            services.AddControllers().AddApplicationPart(typeof(TestEndpointsController).Assembly));
    }
}

/// <summary>
/// A Swagger-enabled variant for verifying the spec. Kept separate from <see cref="LuxMapApiFactory"/>
/// because that one deliberately runs in Production to prove a 500 leaks no exception detail.
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
