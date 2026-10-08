using System.Text.Json;
using LuxMap.Modules.Assets.Crud;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace LuxMap.Api.OpenApi;

/// <summary>
/// Gives the <see cref="JsonElement"/> request fields a real type in the spec: <c>SetPoleFeederRequest.feeder_id</c>,
/// and the pole note of <c>SetPoleNoteRequest</c> and <c>UpdatePoleRequest</c> (POLE-NOTE).
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
    /// <summary>(request type, JSON property) → what the field means. Each is a nullable string on the wire.</summary>
    private static readonly Dictionary<(Type Type, string Property), string> Fields = new()
    {
        [(typeof(SetPoleFeederRequest), "feeder_id")] =
            "The feeder this pole hangs off, or null when it is on no circuit at all. "
            + "The key is REQUIRED: omitting it is a 400, so an empty body cannot silently clear it.",
        [(typeof(SetPoleNoteRequest), "note")] =
            "The engineer's note on this pole, at most 1000 characters; null or blank clears it. "
            + "The key is REQUIRED: omitting it is a 400, so an empty body cannot silently clear it.",
        [(typeof(UpdatePoleRequest), "note")] =
            "The engineer's note. OPTIONAL and the one field this full replacement keeps when absent: "
            + "leave the key out to keep the note, send null or blank to clear it, text (at most 1000 characters) to overwrite it.",
        [(typeof(UpdateFeederRequest), "cabinet_id")] =
            "The cabinet this circuit leaves from (CAB-6). OPTIONAL and KEPT when absent: leave the key out to keep the cabinet, "
            + "send null to detach the feeder, an id to move it. A feeder a device switches cannot move or be detached (409).",
        [(typeof(CreatePoleRequest), "feeder_source")] = FeederSource,
        [(typeof(UpdatePoleRequest), "feeder_source")] = FeederSource,
        [(typeof(SetPoleFeederRequest), "feeder_source")] = FeederSource,
        [(typeof(CreateFeederRequest), "cabinet_source")] = CabinetSource,
        [(typeof(UpdateFeederRequest), "cabinet_source")] = CabinetSource,
    };

    private const string FeederSource =
        "Provenance of the pole → feeder relation (TOPO-INFER): verified | inferred. OPTIONAL: absent = inferred for a new or moved "
            + "feeder, KEPT when the feeder stays. null while a feeder is set, or a label with no feeder, is 400.";

    private const string CabinetSource =
        "Provenance of the feeder → cabinet relation (TOPO-INFER D-10): verified | inferred. OPTIONAL: absent = inferred for a new or "
            + "moved cabinet, KEPT when the cabinet stays. null while a cabinet is set, or a label with no cabinet, is 400.";

    public void Apply(IOpenApiSchema schema, SchemaFilterContext context)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(context);

        if (schema is not OpenApiSchema concrete || concrete.Properties is null)
        {
            return;
        }

        foreach (var ((type, name), description) in Fields)
        {
            if (type == context.Type && concrete.Properties.TryGetValue(name, out var property) && property is OpenApiSchema field)
            {
                field.Type = JsonSchemaType.String | JsonSchemaType.Null;
                field.Description = description;
            }
        }
    }
}
