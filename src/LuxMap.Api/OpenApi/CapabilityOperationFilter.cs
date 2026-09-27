using System.Text.Json.Nodes;
using LuxMap.Persistence.Conventions;
using LuxMap.Shared.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace LuxMap.Api.OpenApi;

/// <summary>
/// Publishes, on every operation, the capability it requires and the exact roles that capability
/// admits (Contract v1.7 section 2): <c>x-luxmap-capability</c> and <c>x-luxmap-roles</c>.
/// </summary>
/// <remarks>
/// Read from the endpoint's own <see cref="IAuthorizeData"/> and from <see cref="LuxMapPolicies.Matrix"/>,
/// so the spec cannot say something the authorization pipeline does not do. Web and mobile generate
/// from this file; before it they had to read the Contract by eye to know which buttons to hide.
/// </remarks>
public sealed class CapabilityOperationFilter : IOperationFilter
{
    public const string CapabilityExtension = "x-luxmap-capability";
    public const string RolesExtension = "x-luxmap-roles";

    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(context);

        var capability = context.ApiDescription.ActionDescriptor.EndpointMetadata
            .OfType<IAuthorizeData>()
            .Select(data => data.Policy)
            .FirstOrDefault(policy => policy is not null && LuxMapPolicies.Matrix.ContainsKey(policy));

        if (capability is null)
        {
            return;
        }

        var roles = new JsonArray([.. LuxMapPolicies.Matrix[capability]
            .Select(role => JsonValue.Create(ContractEnum.ToDbValue(role)))]);

        operation.Extensions ??= new Dictionary<string, IOpenApiExtension>();
        operation.Extensions[CapabilityExtension] = new JsonNodeExtension(JsonValue.Create(capability));
        operation.Extensions[RolesExtension] = new JsonNodeExtension(roles);
    }
}
