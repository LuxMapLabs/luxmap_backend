using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LuxMap.Api.Tests;

/// <summary>
/// SPEC-NULLABLE (D-1…D-3): every object / enum property a RESPONSE may send as <c>null</c> says so — <c>type</c> + <c>nullable</c> +
/// <c>allOf: [$ref]</c> — and nothing else changed shape. Expectations are LITERAL (the matrix-test rule): a test that read them from
/// the filter would agree with any filter.
/// </summary>
public class OpenApiNullableTests(LuxMapSwaggerFactory factory) : IClassFixture<LuxMapSwaggerFactory>
{
    private static JsonElement? cachedSpec;
    private static readonly Lock SpecLock = new();

    /// <summary>The 25 response properties of Phase 1 (<c>.ai/results/SPEC-NULLABLE-p1.md</c> §1): schema, property, target, wire type.</summary>
    public static TheoryData<string, string, string, string> ResponseProperties => new()
    {
        { "AgendaWorkOrder", "location", "WorkOrderLocation", "object" },
        { "WorkOrderAgendaGroup", "location", "WorkOrderLocation", "object" },
        { "CitizenPhotoItem", "ai_hint", "CitizenPhotoAiHint", "object" },
        { "CoverageGap", "geometry", "Geometry", "object" },
        { "FeederListItem", "cabinet", "FeederCabinet", "object" },
        { "LuxReadingWithLuminanceResponse", "nearest_luminance", "NearestLuminance", "object" },
        { "PoleListItem", "active_fixture", "ActiveFixture", "object" },
        { "PoleMapDetail", "fixture", "PoleMapFixture", "object" },
        { "PoleMapDetail", "iot_node", "PoleMapIotNode", "object" },
        { "PoleMapDetail", "luminance_baseline", "PoleMapBaseline", "object" },
        { "SweepDetailResponse", "latest_run", "SweepRunSummary", "object" },
        { "CitizenSubmissionItem", "photos_dropped", "PhotosDroppedReason", "string" },
        { "IotNodeRelay", "control_mode", "FeederControlMode", "string" },
        { "LightingCommandItem", "reported_mode", "FeederControlMode", "string" },
        { "PoleListItem", "feeder_source", "TopologySource", "string" },
        { "PoleListItem", "cabinet_source", "TopologySource", "string" },
        { "PoleMapStatus", "source_channel", "SourceChannel", "string" },
        { "PoleProperties", "power_source", "PowerSource", "string" },
        { "PoleProperties", "fixture_type", "FixtureType", "string" },
        { "SyncPoleProperties", "power_source", "PowerSource", "string" },
        { "SyncPoleProperties", "fixture_type", "FixtureType", "string" },
        { "TopologyPole", "feeder_source", "TopologySource", "string" },
        { "WarrantyItem", "fixture_type", "FixtureType", "string" },
        { "WorkOrderFaultDetail", "inspection_outcome", "InspectionOutcome", "string" },
        { "WorkOrderPole", "fixture_type", "FixtureType", "string" },
    };

    /// <summary>
    /// Request properties C# declares nullable only so the server can answer "missing" with a 400 that names the field (D-1). A client
    /// must never read that it may SEND null.
    /// </summary>
    public static TheoryData<string, string> RequestProperties => new()
    {
        { "AcceptCitizenReportRequest", "fault_type" },
        { "AcceptCitizenReportRequest", "severity" },
        { "ReportFaultRequest", "location" },
        { "ReportFaultRequest", "fault_type" },
        { "ReportFaultRequest", "severity" },
        { "CreateSweepRequest", "data_source" },
        { "DeviceAckRequest", "reported_mode" },
        { "FaultOutcomeRequest", "outcome" },
        { "UpdateUserRequest", "role" },
    };

    /// <summary><c>Feature&lt;TProperties&gt;.properties</c>: nullable to the annotations (a bare type parameter), never null on the wire.</summary>
    public static TheoryData<string> FeatureSchemas => new()
    {
        "PolePropertiesFeature", "SyncPolePropertiesFeature", "SegmentPropertiesFeature", "IotNodePropertiesFeature",
        "CabinetPropertiesFeature", "CabinetTopologyEdgePropertiesFeature",
    };

    private JsonElement Spec
    {
        get
        {
            lock (SpecLock)
            {
                cachedSpec ??= JsonDocument.Parse(
                    factory.CreateClient().GetStringAsync("/swagger/v1/swagger.json").GetAwaiter().GetResult()).RootElement.Clone();
                return cachedSpec.Value;
            }
        }
    }

    private JsonElement Property(string schema, string property) => Spec.GetProperty("components").GetProperty("schemas")
        .GetProperty(schema).GetProperty("properties").GetProperty(property);

    [Theory]
    [MemberData(nameof(ResponseProperties))]
    public void A_response_property_that_may_be_null_says_so_next_to_its_type(string schema, string property, string target, string type)
    {
        var node = Property(schema, property);

        Assert.False(node.TryGetProperty("$ref", out _), $"{schema}.{property} is still a bare $ref — the spec says it is never null");
        Assert.Equal(type, node.GetProperty("type").GetString());
        Assert.True(node.GetProperty("nullable").GetBoolean());
        var reference = Assert.Single(node.GetProperty("allOf").EnumerateArray());
        Assert.Equal($"#/components/schemas/{target}", reference.GetProperty("$ref").GetString());
    }

    [Theory]
    [MemberData(nameof(RequestProperties))]
    public void A_request_property_keeps_its_bare_reference(string schema, string property)
    {
        var node = Property(schema, property);

        Assert.True(node.TryGetProperty("$ref", out _), $"{schema}.{property} must not tell a client it may send null");
        Assert.False(node.TryGetProperty("nullable", out _));
    }

    [Theory]
    [MemberData(nameof(FeatureSchemas))]
    public void A_feature_always_carries_its_properties(string schema)
    {
        var node = Property(schema, "properties");

        Assert.True(node.TryGetProperty("$ref", out _));
        Assert.False(node.TryGetProperty("nullable", out _));
    }

    /// <summary>Whatever the filter wraps, anywhere in the document: one reference, a type that matches what it points at, and null allowed.</summary>
    [Fact]
    public void Every_wrapped_reference_points_at_one_schema_of_the_declared_type()
    {
        var schemas = Spec.GetProperty("components").GetProperty("schemas");
        var wrapped = schemas.EnumerateObject()
            .SelectMany(s => s.Value.TryGetProperty("properties", out var properties) ? properties.EnumerateObject() : [])
            .Where(p => p.Value.TryGetProperty("allOf", out _))
            .ToArray();

        Assert.Equal(25, wrapped.Length);
        Assert.All(wrapped, p =>
        {
            var target = Assert.Single(p.Value.GetProperty("allOf").EnumerateArray()).GetProperty("$ref").GetString()!.Split('/')[^1];
            var isEnum = schemas.GetProperty(target).TryGetProperty("enum", out _);
            Assert.Equal(isEnum ? "string" : "object", p.Value.GetProperty("type").GetString());
            Assert.True(p.Value.GetProperty("nullable").GetBoolean());
        });
    }

    /// <summary>
    /// Independent of the filter and of the literal list: every response-only schema in the document is matched to its C# type, and no
    /// property the nullable annotations call nullable may still be a bare <c>$ref</c>. A new response DTO is checked the day it exists.
    /// </summary>
    [Fact]
    public void No_nullable_property_of_a_response_type_is_left_a_bare_reference()
    {
        var schemas = Spec.GetProperty("components").GetProperty("schemas");
        var responseOnly = Reach(Spec, "responses").Except(Reach(Spec, "requestBody").Concat(Reach(Spec, "parameters"))).ToHashSet();
        var types = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => a.GetName().Name!.StartsWith("LuxMap.", StringComparison.Ordinal))
            .SelectMany(a => a.GetTypes())
            .Where(t => t is { IsClass: true, IsGenericType: false, IsPublic: true } || t.IsNestedPublic)
            .GroupBy(t => t.Name).Where(g => g.Count() == 1).ToDictionary(g => g.Key, g => g.Single());

        var checkedProperties = 0;
        var bare = new List<string>();
        foreach (var id in responseOnly.Where(types.ContainsKey))
        {
            if (!schemas.GetProperty(id).TryGetProperty("properties", out var properties)) continue;
            var context = new NullabilityInfoContext();
            foreach (var property in types[id].GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                var name = property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name
                    ?? JsonNamingPolicy.SnakeCaseLower.ConvertName(property.Name);
                if (!properties.TryGetProperty(name, out var node)) continue;
                checkedProperties++;
                if (node.TryGetProperty("$ref", out _) && context.Create(property).ReadState == NullabilityState.Nullable)
                    bare.Add($"{id}.{name}");
            }
        }

        Assert.True(checkedProperties > 500, $"only {checkedProperties} properties matched a C# type — the mapping broke, not the spec");
        Assert.Empty(bare);
    }

    /// <summary>
    /// The consolidated spec is generated with hand-written schemas that OVERWRITE the exported ones (CLAUDE.md, BE-14b): any property
    /// the code declares nullable must still be nullable there. This is what lost <c>lamp_watt</c> / <c>install_date</c> before.
    /// </summary>
    [Fact]
    public void The_consolidated_spec_keeps_every_nullable_the_code_declares()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "LuxMap.slnx"))) root = root.Parent;
        Assert.NotNull(root);
        JsonElement Load(string file) => JsonDocument.Parse(File.ReadAllText(Path.Combine(root!.FullName, "docs", "openapi", file)))
            .RootElement.GetProperty("components").GetProperty("schemas").Clone();
        var code = Load("luxmap-v1.json");
        var consolidated = Load("luxmap-v1.5.json");

        var lost = new List<string>();
        foreach (var schema in code.EnumerateObject())
        {
            if (!schema.Value.TryGetProperty("properties", out var properties)
                || !consolidated.TryGetProperty(schema.Name, out var other) || !other.TryGetProperty("properties", out var otherProperties)) continue;
            foreach (var property in properties.EnumerateObject())
            {
                if (Nullable(property.Value) && otherProperties.TryGetProperty(property.Name, out var kept) && !Nullable(kept))
                    lost.Add($"{schema.Name}.{property.Name}");
            }
        }

        Assert.Empty(lost);

        static bool Nullable(JsonElement node) => node.TryGetProperty("nullable", out var flag) && flag.GetBoolean();
    }

    /// <summary>Component schemas reachable from every <paramref name="part"/> of every operation (responses / requestBody / parameters).</summary>
    private static HashSet<string> Reach(JsonElement spec, string part)
    {
        var schemas = spec.GetProperty("components").GetProperty("schemas");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var stack = new Stack<JsonElement>();
        foreach (var operation in spec.GetProperty("paths").EnumerateObject().SelectMany(p => p.Value.EnumerateObject()))
        {
            if (operation.Value.ValueKind == JsonValueKind.Object && operation.Value.TryGetProperty(part, out var node)) stack.Push(node);
        }

        while (stack.Count > 0)
        {
            var node = stack.Pop();
            if (node.ValueKind == JsonValueKind.Object)
            {
                foreach (var member in node.EnumerateObject())
                {
                    if (member.Name == "$ref" && member.Value.GetString() is { } reference && reference.StartsWith("#/components/schemas/", StringComparison.Ordinal))
                    {
                        var id = reference["#/components/schemas/".Length..];
                        if (seen.Add(id)) stack.Push(schemas.GetProperty(id));
                    }
                    else
                    {
                        stack.Push(member.Value);
                    }
                }
            }
            else if (node.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in node.EnumerateArray()) stack.Push(item);
            }
        }

        return seen;
    }
}
