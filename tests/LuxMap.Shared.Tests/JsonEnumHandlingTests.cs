using System.Text.Json;
using LuxMap.Shared.Contracts.Enums;
using LuxMap.Shared.Serialization;

namespace LuxMap.Shared.Tests;

public class JsonEnumHandlingTests
{
    private sealed record RequiredEnumBody(FaultStatus FaultStatus);
    private sealed record NullableEnumBody(FaultStatus? FaultStatus);

    [Theory]
    [InlineData("2")]
    [InlineData("999")]
    [InlineData("\"2\"")]
    [InlineData("\"999\"")]
    [InlineData("\"not_a_status\"")]
    public void Invalid_enum_values_are_rejected_at_the_json_boundary(string value)
    {
        var json = $$"""{"fault_status": {{value}}}""";

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<RequiredEnumBody>(json, LuxMapJsonOptions.Default));
    }

    [Theory]
    [InlineData("2")]
    [InlineData("999")]
    [InlineData("\"2\"")]
    [InlineData("\"999\"")]
    [InlineData("\"not_a_status\"")]
    public void Nullable_enums_also_reject_invalid_values(string value)
    {
        var json = $$"""{"fault_status": {{value}}}""";

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<NullableEnumBody>(json, LuxMapJsonOptions.Default));
    }

    [Fact]
    public void A_valid_snake_case_name_is_still_read()
    {
        Assert.Equal(new RequiredEnumBody(FaultStatus.InProgress), JsonSerializer.Deserialize<RequiredEnumBody>(
            """{"fault_status":"in_progress"}""", LuxMapJsonOptions.Default));
    }

    [Fact]
    public void A_nullable_enum_still_accepts_null()
    {
        Assert.Equal(new NullableEnumBody(null), JsonSerializer.Deserialize<NullableEnumBody>(
            """{"fault_status":null}""", LuxMapJsonOptions.Default));
    }

    [Fact]
    public void Writing_a_defined_enum_still_uses_a_snake_case_string()
    {
        Assert.Equal("""{"fault_status":"in_progress"}""", JsonSerializer.Serialize(
            new RequiredEnumBody(FaultStatus.InProgress), LuxMapJsonOptions.Default));
    }

    [Fact]
    public void Writing_an_undefined_enum_cannot_emit_an_integer()
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Serialize(
            new RequiredEnumBody((FaultStatus)999), LuxMapJsonOptions.Default));
    }
}
