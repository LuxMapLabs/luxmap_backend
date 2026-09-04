using System.Text.Json;
using System.Text.Json.Serialization;
using LuxMap.Shared.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace LuxMap.Shared.Tests;

/// <summary>
/// Both JSON pipelines must carry the Contract's conventions — not just one of them.
/// </summary>
/// <remarks>
/// ASP.NET Core keeps TWO separate options objects: minimal APIs read
/// <c>Microsoft.AspNetCore.Http.Json.JsonOptions</c>, MVC controllers read
/// <c>Microsoft.AspNetCore.Mvc.JsonOptions</c>. Configuring one leaves the other emitting camelCase
/// and numeric enums, and nothing anywhere fails — the endpoints on the unconfigured pipeline simply
/// answer in a shape the front end cannot read.
/// <para>
/// ⚠️ Until this file existed, that risk was carried by a COMMENT on
/// <c>AddLuxMapJsonConventions</c> and nothing else: deleting either <c>Configure</c> call left all
/// 275 tests green. A comment is the weakest possible guard, and during this project one was in fact
/// deleted by a tool. This test is the guard that survives that.
/// </para>
/// <para>
/// It asserts on the resolved options rather than over HTTP on purpose: every production endpoint
/// today is MVC, so an HTTP test would exercise one pipeline and quietly prove nothing about the
/// other — which is the exact failure being guarded against.
/// </para>
/// </remarks>
public class JsonPipelineConventionTests
{
    private static ServiceProvider Configured()
        => new ServiceCollection().AddLuxMapJsonConventions().BuildServiceProvider();

    public static TheoryData<string, Func<ServiceProvider, JsonSerializerOptions>> Pipelines => new()
    {
        {
            "Minimal API (Http.Json.JsonOptions)",
            provider => provider.GetRequiredService<IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions>>()
                .Value.SerializerOptions
        },
        {
            "MVC controllers (Mvc.JsonOptions)",
            provider => provider.GetRequiredService<IOptions<Microsoft.AspNetCore.Mvc.JsonOptions>>()
                .Value.JsonSerializerOptions
        },
    };

    [Theory]
    [MemberData(nameof(Pipelines))]
    public void Both_pipelines_serialize_property_names_as_snake_case(
        string pipeline, Func<ServiceProvider, JsonSerializerOptions> resolve)
    {
        using var provider = Configured();

        var json = JsonSerializer.Serialize(new SampleBody("POLE-0001", 42), resolve(provider));

        Assert.Contains("\"pole_id\"", json, StringComparison.Ordinal);
        Assert.Contains("\"lamp_watt\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("poleId", json, StringComparison.Ordinal);
        Assert.NotNull(pipeline);
    }

    [Theory]
    [MemberData(nameof(Pipelines))]
    public void Both_pipelines_write_enums_as_lowercase_strings_never_numbers(
        string pipeline, Func<ServiceProvider, JsonSerializerOptions> resolve)
    {
        using var provider = Configured();

        var json = JsonSerializer.Serialize(new EnumBody(SampleStatus.OutOfService), resolve(provider));

        // Contract section 5.5 names this failure explicitly: a .NET int enum breaks the front end.
        Assert.Contains("\"out_of_service\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"status\":1", json, StringComparison.Ordinal);
        Assert.NotNull(pipeline);
    }

    [Theory]
    [MemberData(nameof(Pipelines))]
    public void Both_pipelines_carry_the_same_naming_policy_instance(
        string pipeline, Func<ServiceProvider, JsonSerializerOptions> resolve)
    {
        using var provider = Configured();
        var options = resolve(provider);

        Assert.Same(JsonNamingPolicy.SnakeCaseLower, options.PropertyNamingPolicy);
        Assert.Same(JsonNamingPolicy.SnakeCaseLower, options.DictionaryKeyPolicy);
        Assert.Contains(options.Converters, converter => converter is JsonStringEnumConverter);
        Assert.NotNull(pipeline);
    }

    private sealed record SampleBody(string PoleId, int LampWatt);

    private sealed record EnumBody(SampleStatus Status);

    private enum SampleStatus
    {
        InService,
        OutOfService,
    }
}
