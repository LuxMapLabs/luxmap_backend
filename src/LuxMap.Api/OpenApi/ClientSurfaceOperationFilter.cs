using System.Text.Json.Nodes;
using LuxMap.Shared.Http;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace LuxMap.Api.OpenApi;

/// <summary>Publishes the client surface without changing tags or operation IDs used by generators.</summary>
public sealed class ClientSurfaceOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(context);

        var surface = context.ApiDescription.ActionDescriptor.EndpointMetadata
            .OfType<ClientSurfaceAttribute>()
            .LastOrDefault()?.Surface;
        var client = surface switch
        {
            ClientSurface.Mobile => "mobile",
            ClientSurface.Web => "web",
            _ => "shared",
        };

        operation.Extensions ??= new Dictionary<string, IOpenApiExtension>();
        operation.Extensions["x-luxmap-client"] = new JsonNodeExtension(JsonValue.Create(client));

        var prefix = client switch
        {
            "mobile" => "[Mobile] ",
            "web" => "[Web] ",
            _ when context.ApiDescription.HttpMethod == "GET"
                && context.ApiDescription.RelativePath == "api/v1/auth/me" => "[Dùng chung] ",
            _ => null,
        };
        if (prefix is not null)
        {
            operation.Summary = prefix + (operation.Summary ?? context.MethodInfo.Name);
        }
    }
}
