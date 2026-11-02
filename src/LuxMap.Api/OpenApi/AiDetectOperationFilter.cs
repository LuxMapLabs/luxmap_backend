using LuxMap.Modules.AI.Controllers;
using LuxMap.Modules.AI.DTOs;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace LuxMap.Api.OpenApi;

/// <summary>The one 200 response is a classification object for day/unknown or the classification plus YOLO object for night.</summary>
public sealed class AiDetectOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        if (context.MethodInfo.DeclaringType != typeof(AiController)) return;
        operation.Responses!["200"] = new OpenApiResponse
        {
            Description = "Day/unknown object with confidence_percent, or the night classification with confidence_percent and YOLO detection fields.",
            Content = new Dictionary<string, OpenApiMediaType>
            {
                ["application/json"] = new()
                {
                    Schema = new OpenApiSchema
                    {
                        AnyOf =
                        [
                            context.SchemaGenerator.GenerateSchema(typeof(AiDetectResponse), context.SchemaRepository),
                            context.SchemaGenerator.GenerateSchema(typeof(AiClassificationResponse), context.SchemaRepository),
                        ],
                    },
                },
            },
        };
    }
}
