using LuxMap.Modules.WorkOrders;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace LuxMap.Api.OpenApi;

/// <summary>Wire types for BE-23's presence-aware fields; runtime retains missing versus null.</summary>
public sealed class WorkOrderSchemaFilter : ISchemaFilter
{
    public void Apply(IOpenApiSchema schema, SchemaFilterContext context)
    {
        if (schema is not OpenApiSchema concrete || concrete.Properties is null) return;
        if (context.Type == typeof(CreateWorkOrderRequest))
        {
            foreach (var name in new[] { "work_order_id", "commune_id", "wo_status", "cluster_id", "priority_score" }) concrete.Properties.Remove(name);
            concrete.Description = "WO-5 (provisional): repair requires fault_ids; inspection requires fault_ids OR segment_id. Server-owned fields are rejected.";
        }
        if (context.Type == typeof(PatchWorkOrderRequest))
        {
            foreach (var name in new[] { "commune_id", "wo_status", "assigned_to", "fault_ids", "task_kind", "segment_id" }) concrete.Properties.Remove(name);
            concrete.Properties["title"] = new OpenApiSchema { Type = JsonSchemaType.String, MinLength = 1, MaxLength = 200 };
            foreach (var name in new[] { "due_date", "scheduled_date" }) concrete.Properties[name] = new OpenApiSchema
            {
                Type = JsonSchemaType.String | JsonSchemaType.Null, Format = "date", Description = "Absent preserves; null clears.",
            };
        }
        if (context.Type == typeof(AssignWorkOrderRequest))
        {
            concrete.Properties["assigned_to"] = new OpenApiSchema { Type = JsonSchemaType.String | JsonSchemaType.Null };
            concrete.Required ??= new HashSet<string>();
            concrete.Required.Add("assigned_to");
        }
        if (context.Type == typeof(CompleteWorkOrderRequest))
        {
            concrete.Properties["fault_outcomes"] = new OpenApiSchema
            {
                Type = JsonSchemaType.Array,
                Items = context.SchemaGenerator.GenerateSchema(typeof(FaultOutcomeRequest), context.SchemaRepository),
                Description = "Required exactly once per linked fault for inspection; forbidden for repair or segment-only inspection.",
            };
            concrete.Properties["report_note"] = new OpenApiSchema { Type = JsonSchemaType.String, MinLength = 10 };
        }
    }
}
