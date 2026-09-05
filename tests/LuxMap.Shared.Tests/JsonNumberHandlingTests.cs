using System.Text.Json;
using LuxMap.Shared.Serialization;

namespace LuxMap.Shared.Tests;

/// <summary>
/// The JSON layer must never accept <c>NaN</c> or <c>Infinity</c> as numbers.
/// </summary>
/// <remarks>
/// This is the OUTER half of a two-layer defence; the inner half is
/// <c>ck_lux_reading_value_non_negative</c>, pinned by <c>LuxValueFinitenessTests</c>.
/// <para>
/// ⚠️ It currently holds only because <c>JsonNumberHandling.Strict</c> is the System.Text.Json
/// DEFAULT — nothing in <c>LuxMapJsonOptions</c> states it. A default nobody asserts is a decision
/// nobody made, and setting <c>AllowNamedFloatingPointLiterals</c> anywhere in that file would
/// silently open the door. That file is also the one a tool stripped explanatory comments from
/// during this project, which is precisely why the guarantee belongs in a test rather than in prose.
/// </para>
/// <para>
/// Verified by temporarily adding <c>AllowNamedFloatingPointLiterals</c>: these tests go red.
/// </para>
/// </remarks>
public class JsonNumberHandlingTests
{
    private sealed record Measurement(double LuxValue);

    [Theory]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("-Infinity")]
    public void A_named_floating_point_literal_is_refused_rather_than_parsed(string literal)
    {
        // Quoted, because bare NaN is not valid JSON at all — the quoted form is the one
        // AllowNamedFloatingPointLiterals would accept, so it is the one worth guarding.
        var json = $$"""{"lux_value": "{{literal}}"}""";

        Assert.ThrowsAny<JsonException>(
            () => JsonSerializer.Deserialize<Measurement>(json, LuxMapJsonOptions.Default));
    }

    [Fact]
    public void A_bare_unquoted_NaN_token_is_not_valid_json_either()
    {
        Assert.ThrowsAny<JsonException>(
            () => JsonSerializer.Deserialize<Measurement>(
                """{"lux_value": NaN}""", LuxMapJsonOptions.Default));
    }

    [Fact]
    public void Writing_a_non_finite_double_throws_instead_of_emitting_an_unreadable_document()
    {
        // The mirror case. Were this allowed, the API would answer with a body no strict JSON parser
        // can read — and the client would see a transport error rather than a data problem.
        Assert.ThrowsAny<JsonException>(
            () => JsonSerializer.Serialize(new Measurement(double.NaN), LuxMapJsonOptions.Default));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(12.4)]
    [InlineData(99999)]
    public void Ordinary_measurements_still_round_trip(double luxValue)
    {
        var json = JsonSerializer.Serialize(new Measurement(luxValue), LuxMapJsonOptions.Default);
        var back = JsonSerializer.Deserialize<Measurement>(json, LuxMapJsonOptions.Default);

        Assert.Equal(luxValue, back!.LuxValue, precision: 6);
    }
}
