using System.Text.Json.Nodes;
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
        if (context.MethodInfo.DeclaringType != typeof(AiController) || context.MethodInfo.Name != nameof(AiController.DetectAsync)) return;
        context.SchemaGenerator.GenerateSchema(typeof(AiClassificationResponse), context.SchemaRepository);
        context.SchemaGenerator.GenerateSchema(typeof(AiDetectResponse), context.SchemaRepository);
        Restrict(nameof(AiClassificationResponse), ["day", "unknown"]);
        Restrict(nameof(AiDetectResponse), ["night"]);
        void Restrict(string name, string[] labels)
        {
            var schema = (OpenApiSchema)context.SchemaRepository.Schemas[name];
            // The union is disjoint by classification, not by closing objects to future fields.
            schema.AdditionalPropertiesAllowed = true;
            schema.Properties!["classification"] = new OpenApiSchema
            {
                Type = JsonSchemaType.String,
                Enum = [.. labels.Select(value => (JsonNode)JsonValue.Create(value)!)],
            };
            schema.Properties["confidence_percent"] = new OpenApiSchema
            {
                Type = JsonSchemaType.Number, Minimum = "0", Maximum = "100",
            };
        }
        operation.Responses!["200"] = new OpenApiResponse
        {
            Description = "Day/unknown object with confidence_percent, or the night classification with confidence_percent and YOLO detection fields.",
            Content = new Dictionary<string, OpenApiMediaType>
            {
                ["application/json"] = new()
                {
                    Schema = new OpenApiSchema
                    {
                        OneOf =
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
