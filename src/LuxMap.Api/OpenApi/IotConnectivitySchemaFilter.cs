using System.Text.Json.Nodes;
using LuxMap.Modules.IOT;
using LuxMap.Shared.Contracts.Enums;
using LuxMap.Shared.Http;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace LuxMap.Api.OpenApi;

public sealed class IotConnectivitySchemaFilter : ISchemaFilter
{
    public void Apply(IOpenApiSchema schema, SchemaFilterContext context)
    {
        if (context.Type == typeof(IotDiagnosticHistoryItem) && schema is OpenApiSchema history && history.Properties is { } properties)
        {
            properties["fault_status"] = new OpenApiSchema {
                Type = JsonSchemaType.String | JsonSchemaType.Null,
                Enum = [.. Enum.GetValues<FaultStatus>().Select(value => (JsonNode)JsonValue.Create(WireEnum.Name(value))!), null!],
                Description = "Trạng thái nghiệp vụ hiện tại của Fault được liên kết; null khi chưa có Fault. Độc lập status của episode.",
            };
            return;
        }
        if (context.Type != typeof(IotNodeStatus) && context.Type != typeof(IotElectricalResult) &&
            context.Type != typeof(IotDiagnosticResult) && context.Type != typeof(IotDiagnosticSummaryItem)) return;

        void Describe(string name, string description, params string[] values)
        {
            if (schema.Properties is not { } fields || !fields.TryGetValue(name, out var fieldSchema) ||
                fieldSchema is not OpenApiSchema property) return;
            property.Description = description;
            if (values.Length > 0) property.Enum = values.Select(value => (JsonNode)JsonValue.Create(value)!).ToList();
        }
        var unavailable = context.Type == typeof(IotNodeStatus) || context.Type == typeof(IotDiagnosticSummaryItem);
        Describe("electrical_status", "Độ mới mẫu điện độc lập với kết nối node. ACK không làm mới mẫu đo.",
            unavailable ? ["fresh", "stale", "no_data", "unconfigured", "source_unavailable"] : ["fresh", "stale", "no_data"]);
        Describe("last_seen_at", "UTC lần liên lạc node đã xác thực gần nhất: mẫu đo, ACK mới hoặc báo cáo phần cứng.");
        Describe("last_command_ack_at", "UTC ACK lệnh hợp lệ gần nhất. ACK lặp giữ thời điểm nhận lần đầu.");
        Describe("last_received_at", "UTC nhận mẫu điện latestTelemetry, giữ nghĩa cũ; không phải thời điểm ACK.");
        if (context.Type == typeof(IotNodeStatus) || context.Type == typeof(IotElectricalResult))
            Describe("status", "Trạng thái liên lạc node. online không đảm bảo mẫu điện còn mới.",
                unavailable ? ["online", "offline", "no_data", "unconfigured", "source_unavailable"] : ["online", "offline", "no_data"]);
        else
            Describe("connection_status", "Trạng thái liên lạc node, độc lập diagnostic status và electrical_status.",
                unavailable ? ["online", "offline", "no_data", "unconfigured", "source_unavailable"] : ["online", "offline", "no_data"]);
    }
}
