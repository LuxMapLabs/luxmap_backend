using System.Globalization;
using System.Text.Json.Nodes;
using LuxMap.Modules.WorkOrders;
using LuxMap.Shared.Http;
using LuxMap.Shared.Contracts.Paging;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace LuxMap.Api.OpenApi;

/// <summary>Describe the custom PageQuery binder and the string-valued wire filters for suggestion clients.</summary>
public sealed class SchedulingSuggestionOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        if (context.MethodInfo.DeclaringType != typeof(SchedulingSuggestionsController)) return;
        operation.Extensions ??= new Dictionary<string, IOpenApiExtension>();
        operation.Extensions["x-luxmap-contract"] = new JsonNodeExtension(JsonValue.Create("drift SS-1…SS-5 (19/11/2026, SELF-SIGNED)"));
        foreach (var parameter in operation.Parameters?.OfType<OpenApiParameter>() ?? [])
        {
            if (parameter.Name == "data_source")
            {
                parameter.Description = "CSV data sources; defaults to field. Other sources require an explicit selection.";
                parameter.Schema = new OpenApiSchema { Type = JsonSchemaType.String, Default = JsonValue.Create("field") };
            }
            if (parameter.Name == "kind")
                parameter.Schema = new OpenApiSchema { Type = JsonSchemaType.String,
                    Enum = [.. Enum.GetValues<SchedulingSuggestionKind>().Select(x => JsonValue.Create(WireEnum.Name(x))!)] };
            if (parameter.Name == "state")
                parameter.Schema = new OpenApiSchema { Type = JsonSchemaType.String,
                    Enum = [.. Enum.GetValues<SchedulingSuggestionState>().Select(x => JsonValue.Create(WireEnum.Name(x))!)] };
        }
        if (context.MethodInfo.Name != nameof(SchedulingSuggestionsController.SuggestionDetails)) return;
        operation.Parameters ??= [];
        foreach (var parameter in operation.Parameters.Where(p => p.Name is "page" or "page_size").ToArray())
            operation.Parameters.Remove(parameter);
        foreach (var (name, value) in new[] { ("page", PageRequest.FirstPage), ("page_size", PageRequest.DefaultPageSize) })
            operation.Parameters.Add(new OpenApiParameter
            {
                Name = name, In = ParameterLocation.Query,
                Description = "Out-of-range values are clamped; non-numeric values use the default.",
                Schema = new OpenApiSchema { Type = JsonSchemaType.Integer, Format = "int32", Minimum = "1",
                    Maximum = name == "page_size" ? PageRequest.MaxPageSize.ToString(CultureInfo.InvariantCulture) : null, Default = JsonValue.Create(value) },
            });
    }
}
