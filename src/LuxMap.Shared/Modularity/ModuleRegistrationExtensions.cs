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

            // Controllers live in the module's assembly, not the host's — they must be registered
            // explicitly or MVC never discovers the routes and every call returns 404.
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
