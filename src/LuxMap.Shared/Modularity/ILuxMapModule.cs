using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LuxMap.Shared.Modularity;

/// <summary>
/// One business module of the monolith. Each module registers ITS OWN services — the host only
/// lists the modules and never looks inside them.
/// </summary>
public interface ILuxMapModule
{
    /// <summary>Display name used in startup logs, e.g. <c>Assets</c>.</summary>
    string Name { get; }

    void RegisterServices(IServiceCollection services, IConfiguration configuration);

    /// <summary>
    /// Maps the module's endpoints. No-op by default, so a module without endpoints overrides nothing.
    /// </summary>
    void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
    }
}
