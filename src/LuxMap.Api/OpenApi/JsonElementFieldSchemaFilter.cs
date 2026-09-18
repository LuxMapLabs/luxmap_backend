using System.Text.Json;
using LuxMap.Modules.Assets.Crud;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace LuxMap.Api.OpenApi;

/// <summary>
/// Gives <c>SetPoleFeederRequest.feeder_id</c> a real type in the spec.
/// </summary>
/// <remarks>
/// The property is a <see cref="JsonElement"/> at runtime so an ABSENT key and an explicit
/// <c>null</c> stay distinguishable — see <see cref="SetPoleFeederRequest"/>. Swashbuckle has nothing
/// to infer from that and emits <c>"feeder_id": {}</c>, an untyped field. FM-04 generates the Kotlin
/// DTOs from this file, so WP6 would get <c>Any</c> where <c>String?</c> belongs.
/// <para>
/// A <see cref="ISchemaFilter"/> rather than <c>options.MapType&lt;JsonElement&gt;()</c>: MapType is
/// global, and <c>JsonElement</c> genuinely means "arbitrary JSON" anywhere else it might be used. The
/// narrow fix stays narrow.
/// </para>
/// <para>
/// ⚠️ Schema only. The binding is untouched — a missing key is still a 400, and that behaviour is what
/// keeps an empty body from silently clearing a pole's circuit.
/// </para>
/// </remarks>
public sealed class JsonElementFieldSchemaFilter : ISchemaFilter
{
    public void Apply(IOpenApiSchema schema, SchemaFilterContext context)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(context);

        if (context.Type != typeof(SetPoleFeederRequest) || schema is not OpenApiSchema concrete)
        {
            return;
        }

        if (concrete.Properties?.TryGetValue("feeder_id", out var property) != true
            || property is not OpenApiSchema field)
        {
            return;
        }

        field.Type = JsonSchemaType.String | JsonSchemaType.Null;
        field.Description =
            "The feeder this pole hangs off, or null when it is on no circuit at all. "
            + "The key is REQUIRED: omitting it is a 400, so an empty body cannot silently clear it.";
    }
}
