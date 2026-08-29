using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LuxMap.Shared.Modularity;

public static class ModuleRegistrationExtensions
{
    public static IServiceCollection AddLuxMapModules(
        this IServiceCollection services,
        IConfiguration configuration,
        IEnumerable<ILuxMapModule> modules)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(modules);

        var mvc = services.AddControllers();

        foreach (var module in modules)
        {
            module.RegisterServices(services, configuration);

            // Controller nằm trong assembly của module, không phải của host — phải nạp
            // tường minh, nếu không MVC sẽ không thấy và route trả 404.
            mvc.AddApplicationPart(module.GetType().Assembly);
        }

        return services;
    }

    public static IEndpointRouteBuilder MapLuxMapModules(
        this IEndpointRouteBuilder endpoints,
        IEnumerable<ILuxMapModule> modules)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(modules);

        foreach (var module in modules)
        {
            module.MapEndpoints(endpoints);
        }

        return endpoints;
    }
}
