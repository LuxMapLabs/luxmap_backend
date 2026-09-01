using System.Text.Json;

namespace LuxMap.Api.Tests;

/// <summary>
/// Three things WP6 depends on directly (FM-04 generates Kotlin DTOs). Get any of them wrong and
/// Retrofit/kotlinx generates the wrong types, breaking the whole Android module.
/// </summary>
public class OpenApiSpecTests(LuxMapSwaggerFactory factory) : IClassFixture<LuxMapSwaggerFactory>
{
    private static JsonElement? cachedSpec;
    private static readonly Lock SpecLock = new();

    private JsonElement Spec
    {
        get
        {
            lock (SpecLock)
            {
                cachedSpec ??= JsonDocument.Parse(
                    factory.CreateClient().GetStringAsync("/swagger/v1/swagger.json").GetAwaiter().GetResult())
                    .RootElement.Clone();

                return cachedSpec.Value;
            }
        }
    }

    private JsonElement Schema(string name)
        => Spec.GetProperty("components").GetProperty("schemas").GetProperty(name);

    [Theory]
    [InlineData("FixtureStatus", new[] { "normal", "dim", "out", "unknown" })]
    [InlineData("SourceChannel", new[] { "cv", "iot", "field_report" })]
    [InlineData("DataSource", new[] { "field", "public_imagery", "calibration_rig", "simulated" })]
    [InlineData("FaultType", new[] { "lamp_out", "lamp_dim", "segment_outage", "node_offline", "runtime_decline" })]
    [InlineData("RoadClass", new[] { "inter_commune", "inter_village" })]
    public void Enum_is_a_string_with_the_contract_values(string schemaName, string[] expected)
    {
        var schema = Schema(schemaName);

        Assert.Equal("string", schema.GetProperty("type").GetString());
        Assert.Equal(expected, schema.GetProperty("enum").EnumerateArray().Select(v => v.GetString()));

        // "integer" here would mean Swashbuckle described the enum numerically — WP6 would generate Int.
        Assert.NotEqual("integer", schema.GetProperty("type").GetString());
    }

    [Fact]
    public void All_twelve_contract_enums_are_published()
    {
        string[] expected =
        [
            "FixtureStatus", "PowerSource", "FixtureType", "FaultType", "FaultStatus", "Severity",
            "SourceChannel", "DataSource", "WorkOrderStatus", "NodeRole", "NodeStatus", "RoadClass",
        ];

        var schemas = Spec.GetProperty("components").GetProperty("schemas");
        foreach (var name in expected)
        {
            Assert.True(schemas.TryGetProperty(name, out _), $"Enum {name} is missing from the spec");
        }
    }

    [Fact]
    public void Instant_and_date_only_map_to_different_formats()
    {
        var properties = Schema("SchemaProbe").GetProperty("properties");

        Assert.Equal("date-time", properties.GetProperty("last_seen_at").GetProperty("format").GetString());
        Assert.Equal("date", properties.GetProperty("install_date").GetProperty("format").GetString());
        Assert.Equal("date", properties.GetProperty("warranty_expiry").GetProperty("format").GetString());

        Assert.Equal("string", properties.GetProperty("last_seen_at").GetProperty("type").GetString());
        Assert.Equal("string", properties.GetProperty("install_date").GetProperty("type").GetString());
    }

    [Fact]
    public void Schema_property_names_are_snake_case()
    {
        var names = Schema("SchemaProbe").GetProperty("properties")
            .EnumerateObject().Select(p => p.Name).ToArray();

        Assert.Contains("pole_id", names);
        Assert.Contains("fixture_status", names);
        Assert.Contains("near_sensitive_poi", names);
        Assert.Contains("last_seen_at", names);

        Assert.DoesNotContain("poleId", names);
        Assert.DoesNotContain("nearSensitivePoi", names);
    }

    [Fact]
    public void Bearer_security_scheme_is_declared()
    {
        var scheme = Spec.GetProperty("components").GetProperty("securitySchemes").GetProperty("Bearer");

        Assert.Equal("http", scheme.GetProperty("type").GetString());
        Assert.Equal("bearer", scheme.GetProperty("scheme").GetString());
        Assert.Equal("JWT", scheme.GetProperty("bearerFormat").GetString());
    }

    [Fact]
    public void Paths_use_the_resolved_api_v1_url_not_the_route_template()
    {
        var paths = Spec.GetProperty("paths").EnumerateObject().Select(p => p.Name).ToArray();

        Assert.Contains(paths, path => path.StartsWith("/api/v1/", StringComparison.Ordinal));
        Assert.DoesNotContain(paths, path => path.Contains("{version}", StringComparison.Ordinal));
    }
}
