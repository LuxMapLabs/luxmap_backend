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
            context.MethodInfo.Name != nameof(IotDiagnosticsController.FeederHistoryAsync)) return;

        operation.Extensions ??= new Dictionary<string, IOpenApiExtension>();
        operation.Extensions["x-luxmap-contract"] = new JsonNodeExtension(JsonValue.Create("drift IOT-HISTORY-RAW-1 (25/11/2026)"));
        if (context.SchemaRepository.Schemas.TryGetValue(nameof(IotFeederHistoryResult), out var result) &&
            result.Properties is { } fields && fields.TryGetValue("sort_by", out var field) && field is OpenApiSchema basis)
            basis.Enum = [JsonValue.Create("measured_at")!, JsonValue.Create("received_at")!];

        if (context.SchemaRepository.Schemas.TryGetValue(nameof(IotFeederHistorySample), out var sample) &&
            sample.Properties is { } sampleFields && sampleFields.TryGetValue("reception_type", out var reception) &&
            reception is OpenApiSchema receptionSchema)
        {
            receptionSchema.Enum = [JsonValue.Create("live")!, JsonValue.Create("backfill")!, null!];
            receptionSchema.Description = "live: mẫu gửi trực tiếp; backfill: gửi bù; null: firmware cũ chưa đánh dấu. Giữ giá trị lần nhận đầu.";
        }

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
            if (parameter.Name == "sort_by")
            {
                parameter.Schema = new OpenApiSchema { Type = JsonSchemaType.String,
                    Enum = [JsonValue.Create("measured_at")!, JsonValue.Create("received_at")!],
                    Default = JsonValue.Create("measured_at") };
                parameter.Description = "Cơ sở sắp xếp và lọc from/to; mới nhất trước.";
            }
            if (parameter.Name is "from" or "to")
                parameter.Description = parameter.Name == "from"
                    ? "ISO 8601 UTC, bao gồm thời điểm này; lọc từng mẫu theo sort_by."
                    : "ISO 8601 UTC, loại trừ thời điểm này; phải lớn hơn from.";
        }
    }
}
