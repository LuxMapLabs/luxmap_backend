using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace LuxMap.Api.Tests;

/// <summary>
/// The endpoints of the shipped API, for the tests that assert something about ALL of them at once
/// (<c>AnonymousEndpointTests</c>, <c>CapabilityPolicyCoverageTests</c>).
/// </summary>
internal static class ProductionEndpoints
{
    /// <summary>
    /// Endpoints of the APPLICATION, with the ones this test assembly injects filtered out.
    /// </summary>
    /// <remarks>
    /// <c>ScopeTestController</c> and <c>TestEndpointsController</c> both carry
    /// <c>[AllowAnonymous]</c> on purpose and are loaded into the host through an ApplicationPart, so
    /// counting them would make a test assert something about the test harness rather than about
    /// the shipped API. The filter is on the controller's ASSEMBLY, not on a name pattern.
    /// </remarks>
    public static RouteEndpoint[] Of(IServiceProvider services)
    {
        var thisAssembly = typeof(ProductionEndpoints).Assembly;

        var all = services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.Metadata.GetMetadata<ControllerActionDescriptor>() is not null)
            .ToArray();

        var production = all
            .Where(endpoint => endpoint.Metadata.GetMetadata<ControllerActionDescriptor>()!
                .ControllerTypeInfo.Assembly != thisAssembly)
            .ToArray();

        // The filter must actually be removing something, or its correctness is never exercised.
        Xunit.Assert.NotEqual(all.Length, production.Length);

        return production;
    }

    /// <summary>Renders an endpoint as <c>POST /api/v1/auth/login</c>, with the version token resolved.</summary>
    public static string Describe(RouteEndpoint endpoint)
    {
        var methods = endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? [];
        var route = Regex.Replace(endpoint.RoutePattern.RawText!, @"\{version:apiVersion\}", "1");

        return $"{string.Join('/', methods.Order(StringComparer.Ordinal))} /{route}";
    }
}
