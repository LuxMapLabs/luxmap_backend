using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace LuxMap.Api.OpenApi;

/// <summary>
/// SPEC-NULLABLE (D-1…D-3) — an object or enum property that may be <c>null</c> on the wire must SAY so in the spec.
/// </summary>
/// <remarks>
/// <para>
/// Swashbuckle writes such a property as a bare <c>$ref</c>, and in OpenAPI 3.0 nothing can sit beside a <c>$ref</c>: the spec then
/// claims the field is never null (<c>PoleMapDetail.fixture</c> on a pole without a lamp, <c>SweepDetailResponse.latest_run</c> before the
/// first run…). The fix is the 3.0.4 form, where <c>nullable</c> only counts next to a <c>type</c>:
/// <c>{ "type": "object" | "string", "nullable": true, "allOf": [ { "$ref": … } ] }</c>.
/// </para>
/// <para>
/// Not Swashbuckle's <c>UseAllOfToExtendReferenceSchemas</c>: it wraps EVERY reference, never-null ones too (88 schemas changed when
/// measured). Only what C# declares nullable is touched, read from the nullable annotations — no hand-kept list, so a new DTO is covered
/// the day it is written. Not covered (none exists in a response today): nullable elements INSIDE a list or map, and a nullable property
/// whose declared type is a generic parameter (<c>Feature&lt;T&gt;.Properties</c> and any future <c>Wrapper&lt;T&gt;.Value</c>).
/// </para>
/// </remarks>
public sealed class NullableReferenceSchemaFilter : ISchemaFilter
{
    public void Apply(IOpenApiSchema schema, SchemaFilterContext context)
    {
        if (schema is not OpenApiSchema concrete || concrete.Properties is null || !context.Type.IsClass) return;

        // One per call: NullabilityInfoContext caches internally and is NOT thread-safe, and several documents can be generated at once.
        var nullability = new NullabilityInfoContext();

        foreach (var property in context.Type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var name = property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name
                ?? JsonNamingPolicy.SnakeCaseLower.ConvertName(property.Name);
            if (!concrete.Properties.TryGetValue(name, out var current) || current is not OpenApiSchemaReference reference) continue;
            if (IsGenericParameter(property) || nullability.Create(property).ReadState != NullabilityState.Nullable) continue;

            var target = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
            concrete.Properties[name] = new OpenApiSchema
            {
                Type = (target.IsEnum ? JsonSchemaType.String : JsonSchemaType.Object) | JsonSchemaType.Null,
                AllOf = [reference],
                Description = reference.Description,
            };
        }
    }

    /// <summary>
    /// <c>Feature&lt;TProperties&gt;.Properties</c>: the annotations call an unconstrained type parameter nullable, but a feature always
    /// carries its properties. Decided by the generic DEFINITION, so it holds for every feature type, present and future.
    /// </summary>
    private static bool IsGenericParameter(PropertyInfo property)
        => property.DeclaringType is { IsConstructedGenericType: true } declaring
           && declaring.GetGenericTypeDefinition().GetProperty(property.Name)?.PropertyType.IsGenericParameter == true;
}

/// <summary>
/// Undoes <see cref="NullableReferenceSchemaFilter"/> on schemas that reach a REQUEST body (D-1): only the response side was decided.
/// Of the nine request properties this leaves bare today, four reject <c>null</c> with a 400 (<c>fault_type</c> ×2, <c>data_source</c>,
/// <c>outcome</c>); five DO give <c>null</c> a meaning — keep / default / not applicable (<c>UpdateUserRequest.role</c>,
/// <c>ReportFaultRequest.location</c> / <c>.severity</c>, <c>AcceptCitizenReportRequest.severity</c>, <c>DeviceAckRequest.reported_mode</c>).
/// That is spec debt that predates this ticket, recorded for a follow-up (Codex review SPEC-NULLABLE P2 #5), not something decided here.
/// </summary>
/// <remarks>
/// A schema reachable from a request AND a response stays bare (conservative: the request reading wins); none exists today. The only
/// <c>allOf</c> of one reference with <c>null</c> in its type in this document is the filter's own, which is what is undone.
/// </remarks>
public sealed class NullableReferenceDocumentFilter : IDocumentFilter
{
    public void Apply(OpenApiDocument swaggerDoc, DocumentFilterContext context)
    {
        ArgumentNullException.ThrowIfNull(swaggerDoc);
        var schemas = swaggerDoc.Components?.Schemas;
        if (schemas is null) return;

        var requestRoots = new List<IOpenApiSchema>();
        var paths = swaggerDoc.Paths?.Values ?? Enumerable.Empty<IOpenApiPathItem>();
        foreach (var operation in paths.SelectMany(path => path.Operations?.Values ?? Enumerable.Empty<OpenApiOperation>()))
        {
            requestRoots.AddRange((operation.RequestBody?.Content?.Values ?? []).Select(media => media.Schema).OfType<IOpenApiSchema>());
            requestRoots.AddRange((operation.Parameters ?? []).Select(parameter => parameter.Schema).OfType<IOpenApiSchema>());
        }

        foreach (var id in Reachable(requestRoots, schemas))
        {
            if (schemas[id] is not OpenApiSchema request || request.Properties is null) continue;
            foreach (var (name, property) in request.Properties.ToArray())
            {
                if (property is OpenApiSchema { AllOf: [OpenApiSchemaReference reference] } wrapped
                    && wrapped.Type is { } type && type.HasFlag(JsonSchemaType.Null))
                {
                    request.Properties[name] = reference;
                }
            }
        }
    }

    /// <summary>Every component schema reachable from <paramref name="roots"/>, through properties, items, compositions and maps.</summary>
    public static HashSet<string> Reachable(IEnumerable<IOpenApiSchema> roots, IDictionary<string, IOpenApiSchema> schemas)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var stack = new Stack<IOpenApiSchema>(roots);
        while (stack.Count > 0)
        {
            var schema = stack.Pop();
            if (schema is OpenApiSchemaReference { Reference.Id: { } id })
            {
                if (seen.Add(id) && schemas.TryGetValue(id, out var target)) stack.Push(target);
                continue;
            }

            foreach (var child in Children(schema)) stack.Push(child);
        }

        return seen;
    }

    private static IEnumerable<IOpenApiSchema> Children(IOpenApiSchema schema)
    {
        foreach (var property in schema.Properties?.Values ?? []) yield return property;
        foreach (var part in (schema.AllOf ?? []).Concat(schema.OneOf ?? []).Concat(schema.AnyOf ?? [])) yield return part;
        if (schema.Items is { } items) yield return items;
        if (schema.AdditionalProperties is { } map) yield return map;
    }
}
