using System.Text.Json.Nodes;
using LuxMap.Modules.IOT;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace LuxMap.Api.OpenApi;

public sealed class IotTelemetryHistoryOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        if (context.MethodInfo.DeclaringType != typeof(IotDiagnosticsController) ||
            context.MethodInfo.Name is not (nameof(IotDiagnosticsController.FeederMeasuredHistoryAsync)
                or nameof(IotDiagnosticsController.FeederReceivedHistoryAsync))) return;

        operation.Extensions ??= new Dictionary<string, IOpenApiExtension>();
        operation.Extensions["x-luxmap-contract"] = new JsonNodeExtension(JsonValue.Create("drift IOT-HISTORY-30S (18/11/2026)"));
        if (context.SchemaRepository.Schemas.TryGetValue(nameof(IotFeederHistoryResult), out var result) &&
            result.Properties is { } fields && fields.TryGetValue("time_basis", out var field) && field is OpenApiSchema basis)
            basis.Enum = [JsonValue.Create("measured_at")!, JsonValue.Create("received_at")!];

        foreach (var parameter in operation.Parameters?.OfType<OpenApiParameter>() ?? [])
        {
            if (parameter.Name is "page" or "page_size")
            {
                parameter.Schema = new OpenApiSchema
                {
                    Type = JsonSchemaType.Integer, Format = "int32", Minimum = "1",
                    Maximum = parameter.Name == "page_size" ? "200" : null,
                    Default = JsonValue.Create(parameter.Name == "page_size" ? 20 : 1),
                };
                parameter.Description = "Giá trị ngoài giới hạn được chuẩn hóa theo quy ước phân trang.";
            }
            if (parameter.Name is "from" or "to")
                parameter.Description = parameter.Name == "from"
                    ? "ISO 8601 UTC, bao gồm thời điểm này; lọc mẫu trước khi tính trung bình."
                    : "ISO 8601 UTC, loại trừ thời điểm này; phải lớn hơn from.";
        }
    }
}
