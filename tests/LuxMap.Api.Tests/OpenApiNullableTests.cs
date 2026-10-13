using System.Text.Json;

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
}
