using System.Text.Json;
using System.Text.Json.Nodes;
using LuxMap.Shared.Contracts.Enums;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace LuxMap.Api.OpenApi;

/// <summary>
/// Injects all 12 Contract section 1 enums into <c>components/schemas</c> even when no DTO
/// references them yet. OpenAPI only emits schemas for types that are actually used, and at this
/// stage there are no domain endpoints — without this filter the spec is empty and FM-04 has nothing
/// to generate from.
/// <para>
/// The enums are the frozen part of the Contract, so publishing them early is safe. From BE-09
/// onward, when real DTOs reference them, these are the very schemas that get reused.
/// </para>
/// </summary>
public sealed class ContractEnumDocumentFilter : IDocumentFilter
{
    /// <summary>In the order given by Contract section 1.</summary>
    private static readonly Type[] ContractEnums =
    [
        typeof(FixtureStatus), typeof(PowerSource), typeof(FixtureType),
        typeof(FaultType), typeof(FaultStatus), typeof(Severity),
        typeof(SourceChannel), typeof(DataSource), typeof(WorkOrderStatus),
        typeof(NodeRole), typeof(NodeStatus), typeof(RoadClass),
    ];

    public void Apply(OpenApiDocument swaggerDoc, DocumentFilterContext context)
    {
        ArgumentNullException.ThrowIfNull(swaggerDoc);
        ArgumentNullException.ThrowIfNull(context);

        swaggerDoc.Components ??= new OpenApiComponents();
        swaggerDoc.Components.Schemas ??= new Dictionary<string, IOpenApiSchema>();

        foreach (var enumType in ContractEnums)
        {
            // If a real DTO already referenced it, keep Swashbuckle's schema and do not overwrite.
            if (swaggerDoc.Components.Schemas.ContainsKey(enumType.Name))
            {
                continue;
            }

            swaggerDoc.Components.Schemas[enumType.Name] = BuildStringEnumSchema(enumType);
        }
    }

    private static OpenApiSchema BuildStringEnumSchema(Type enumType)
    {
        var values = Enum.GetNames(enumType)
            .Select(name => JsonNamingPolicy.SnakeCaseLower.ConvertName(name))
            .ToArray();

        return new OpenApiSchema
        {
            Type = JsonSchemaType.String,
            Enum = [.. values.Select(value => (JsonNode)JsonValue.Create(value)!)],
            Description =
                $"Contract v1.1 section 1 — {JsonNamingPolicy.SnakeCaseLower.ConvertName(enumType.Name)}. "
                + "Frozen: do not add values, do not rename, do not use int.",
        };
    }
}
