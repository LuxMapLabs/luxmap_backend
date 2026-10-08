using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LuxMap.Api.Tests;

/// <summary>
/// The production host plus <see cref="DeviceProbeController"/> — kept apart from <see cref="AssetImportFixture"/> so the shared asset
/// host never carries a test controller. Data is still written through that fixture; both hosts share one database.
/// </summary>
public sealed class DeviceProbeFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
        => builder.UseEnvironment("Production").UseTestCorsOrigin()
            .ConfigureServices(services => services.AddControllers().AddApplicationPart(typeof(DeviceProbeController).Assembly));
}
