using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

// Examples appear in Swagger's request body dropdown and can be executed directly.
sealed class CommandExamples : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var deviceId = operation.Parameters?.FirstOrDefault(parameter => parameter.Name == "deviceId");
        if (deviceId is not null) deviceId.Example = new OpenApiString("LUXMAP-P001");

        if (context.ApiDescription.HttpMethod != "POST" ||
            context.ApiDescription.RelativePath?.EndsWith("/command", StringComparison.Ordinal) != true ||
            operation.RequestBody?.Content.TryGetValue("application/json", out var content) != true ||
            content is null)
            return;

        content.Examples = new Dictionary<string, OpenApiExample>
        {
            ["OFF"] = Example("Tắt đèn", "MANUAL", "OFF"),
            ["ON"] = Example("Bật đèn", "MANUAL", "ON"),
            ["AUTO scheduled"] = Example("Tự động theo lịch và lux", "AUTO", null, "scheduled"),
            ["AUTO full_time"] = Example("Tự động theo lux cả ngày", "AUTO", null, "full_time")
        };
    }

    private static OpenApiExample Example(string summary, string mode, string? command, string? autoType = null)
    {
        var value = new OpenApiObject { ["mode"] = new OpenApiString(mode) };
        if (autoType is not null) value["autoType"] = new OpenApiString(autoType);
        if (autoType == "scheduled") value["autoSchedule"] = new OpenApiObject {
            ["dayStartMinute"] = new OpenApiInteger(360), ["nightStartMinute"] = new OpenApiInteger(1080) };
        if (command is not null) value["command"] = new OpenApiString(command);
        return new OpenApiExample { Summary = summary, Value = value };
    }
}
