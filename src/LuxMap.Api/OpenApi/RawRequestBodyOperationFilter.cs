using System.Reflection;
using LuxMap.Shared.Http;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace LuxMap.Api.OpenApi;

/// <summary>
/// Publishes a required binary body for actions marked <see cref="RawRequestBodyAttribute"/>, which
/// stream <c>Request.Body</c> themselves (BE-15 clip and raw-file uploads). Without it the exported
/// PUT operations have no requestBody, and neither Swagger UI nor a generated client can send one.
/// </summary>
public sealed class RawRequestBodyOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(context);

        var body = context.MethodInfo.GetCustomAttribute<RawRequestBodyAttribute>();
        if (body is null)
        {
            return;
        }

        operation.RequestBody = new OpenApiRequestBody
        {
            Required = true,
            Content = body.ContentTypes.ToDictionary(
                type => type,
                _ => new OpenApiMediaType { Schema = new OpenApiSchema { Type = JsonSchemaType.String, Format = "binary" } }),
        };
    }
}
