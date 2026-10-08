using LuxMap.Shared.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace LuxMap.Api.OpenApi;

/// <summary>
/// LIGHT-CTRL 2b: an endpoint naming <see cref="DeviceAuth.Policy"/> is called by a DEVICE, not a person. Its operation
/// replaces the document-wide Bearer requirement with the <c>Device</c> scheme, and carries no capability — no role is a device.
/// </summary>
public sealed class DeviceSecurityOperationFilter : IOperationFilter
{
    public const string SchemeId = "Device";

    public static void AddDefinition(SwaggerGenOptions options)
        => options.AddSecurityDefinition(SchemeId, new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.ApiKey,
            In = ParameterLocation.Header,
            Name = "Authorization",
            Description = "IoT devices only (LIGHT-CTRL, SELF-SIGNED): `Device <node_id>.<secret>`. The secret is issued once by "
                + "POST /api/v1/assets/iot-nodes/{nodeId}/credential. A user's Bearer token is a 401 on these operations; "
                + "this header is a 401 on every other one.",
        });

    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(context);

        if (context.ApiDescription.ActionDescriptor.EndpointMetadata.OfType<IAuthorizeData>().Any(data => data.Policy == DeviceAuth.Policy))
        {
            operation.Security =
            [
                new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference(SchemeId, context.Document)] = [] },
            ];
        }
    }
}
