using System.Text.Json.Nodes;
using LuxMap.Modules.IOT;
using LuxMap.Shared.Contracts.Paging;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace LuxMap.Api.OpenApi;

public sealed class IotTelemetryHistoryOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        if (context.MethodInfo.DeclaringType != typeof(IotDiagnosticsController) ||
            context.MethodInfo.Name != nameof(IotDiagnosticsController.TelemetryHistoryAsync)) return;

        if (operation.Parameters?.SingleOrDefault(parameter => parameter.Name == "sort_by") is OpenApiParameter sortBy)
        {
            sortBy.Schema = new OpenApiSchema
            {
                Type = JsonSchemaType.String,
                Enum = [JsonValue.Create("received_at")!, JsonValue.Create("measured_at")!],
                Description = "Mặc định received_at khi đọc theo trang.",
            };
        }
        operation.Responses!["200"] = new OpenApiResponse
        {
            Description = "Trang kết quả mặc định. Khi truyền after hoặc limit, trả lịch sử theo cursor.",
            Content = new Dictionary<string, OpenApiMediaType>
            {
                ["application/json"] = new()
                {
                    Schema = new OpenApiSchema
                    {
                        OneOf =
                        [
                            context.SchemaGenerator.GenerateSchema(typeof(PagedResult<IotArchivedSample>), context.SchemaRepository),
                            context.SchemaGenerator.GenerateSchema(typeof(IotTelemetryPage), context.SchemaRepository),
                        ],
                    },
                },
            },
        };
    }
}
