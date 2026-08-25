using System.Text.Json;
using System.Text.Json.Nodes;
using LuxMap.Shared.Contracts.Enums;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace LuxMap.Api.OpenApi;

/// <summary>
/// Bơm cả 12 enum của Contract mục 1 vào <c>components/schemas</c> kể cả khi chưa DTO nào
/// tham chiếu tới. OpenAPI chỉ sinh schema cho kiểu được dùng, mà giai đoạn này chưa có
/// endpoint domain nào — không có filter này thì spec rỗng và FM-04 không sinh được gì.
/// <para>
/// Enum là phần Contract đã khoá cứng nên công bố sớm không sợ đổi. Từ BE-09 trở đi, khi
/// DTO thật tham chiếu tới chúng, schema ở đây chính là schema được dùng lại.
/// </para>
/// </summary>
public sealed class ContractEnumDocumentFilter : IDocumentFilter
{
    /// <summary>Đúng thứ tự Contract mục 1.</summary>
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
            // DTO thật đã tham chiếu thì để nguyên schema do Swashbuckle sinh, không ghi đè.
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
                $"Contract v1.1 mục 1 — {JsonNamingPolicy.SnakeCaseLower.ConvertName(enumType.Name)}. "
                + "Khoá cứng: không thêm giá trị, không đổi tên, không dùng int.",
        };
    }
}
