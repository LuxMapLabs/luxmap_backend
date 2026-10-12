using System.Reflection;
using LuxMap.Shared.Csv;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace LuxMap.Api.OpenApi;

/// <summary>
/// Adds <c>text/csv</c> to the 200 of every action marked <see cref="CsvExportAttribute"/> (BE-31, drift W-5), beside the
/// JSON body the action's <c>ProducesResponseType</c> already declares.
/// </summary>
public sealed class CsvExportOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(context);

        if (context.MethodInfo.GetCustomAttribute<CsvExportAttribute>() is null
            || operation.Responses?.TryGetValue("200", out var ok) != true || ok!.Content is null)
        {
            return;
        }

        ok.Content["text/csv"] = new OpenApiMediaType
        {
            Schema = new OpenApiSchema
            {
                Type = JsonSchemaType.String,
                Description = "format=csv: UTF-8 with BOM, CRLF, one row per item, columns spelled as in the JSON body; at most 10 000 rows (else 413 EXPORT_TOO_LARGE).",
            },
        };
    }
}
