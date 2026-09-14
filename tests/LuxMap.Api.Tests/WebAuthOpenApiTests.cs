using System.Text.Json;

namespace LuxMap.Api.Tests;

/// <summary>
/// The spec WP5 and WP6 generate from (Contract section 2.10): the web group documents its cookie,
/// and the mobile group's shapes — which FM-04 turns into Kotlin — did not move.
/// </summary>
public class WebAuthOpenApiTests(LuxMapSwaggerFactory factory) : IClassFixture<LuxMapSwaggerFactory>
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

    private JsonElement Schema(string name) => Spec.GetProperty("components").GetProperty("schemas").GetProperty(name);

    private JsonElement WebPost(string endpoint)
        => Spec.GetProperty("paths").GetProperty($"/api/v1/auth/web/{endpoint}").GetProperty("post");

    private static string[] Names(JsonElement element) => element.EnumerateObject().Select(p => p.Name).ToArray();

    private static string[] Strings(JsonElement array) => array.EnumerateArray().Select(v => v.GetString()!).ToArray();

    [Fact]
    public void The_cookie_security_scheme_is_declared()
    {
        var scheme = Spec.GetProperty("components").GetProperty("securitySchemes").GetProperty("RefreshTokenCookie");

        Assert.Equal("apiKey", scheme.GetProperty("type").GetString());
        Assert.Equal("cookie", scheme.GetProperty("in").GetString());
        Assert.Equal("__Secure-luxmap_rt", scheme.GetProperty("name").GetString());
    }

    [Theory]
    [InlineData("refresh")]
    [InlineData("logout")]
    public void Refresh_and_logout_are_secured_by_the_cookie_and_take_no_body(string endpoint)
    {
        var operation = WebPost(endpoint);
        var requirement = Assert.Single(operation.GetProperty("security").EnumerateArray());

        Assert.Equal(["RefreshTokenCookie"], Names(requirement));
        Assert.False(operation.TryGetProperty("requestBody", out _));
    }

    [Theory]
    [InlineData("login", "200")]
    [InlineData("refresh", "200")]
    [InlineData("logout", "204")]
    public void Every_web_success_response_documents_set_cookie_and_every_web_endpoint_documents_403(
        string endpoint, string successStatus)
    {
        var responses = WebPost(endpoint).GetProperty("responses");

        Assert.True(responses.GetProperty(successStatus).GetProperty("headers").TryGetProperty("Set-Cookie", out _));
        Assert.Contains("ORIGIN_NOT_ALLOWED", responses.GetProperty("403").GetProperty("description").GetString());
    }

    [Fact]
    public void Web_login_takes_an_optional_remember_me()
    {
        var schema = Schema("WebLoginRequest");

        Assert.Equal(["username", "password", "remember_me"], Names(schema.GetProperty("properties")));
        Assert.Equal(["password", "username"], Strings(schema.GetProperty("required")).Order());
        Assert.Equal("boolean", schema.GetProperty("properties").GetProperty("remember_me").GetProperty("type").GetString());
    }

    [Fact]
    public void Web_token_response_has_exactly_three_fields_and_no_refresh_token()
    {
        Assert.Equal(
            ["access_token", "token_type", "expires_in"],
            Names(Schema("WebAuthTokenResponse").GetProperty("properties")));
    }

    [Fact]
    public void The_mobile_auth_schemas_did_not_move()
    {
        Assert.Equal(
            ["access_token", "refresh_token", "token_type", "expires_in"],
            Names(Schema("AuthTokenResponse").GetProperty("properties")));
        Assert.Equal(["username", "password"], Names(Schema("LoginRequest").GetProperty("properties")));
        Assert.Equal(["refresh_token"], Strings(Schema("RefreshRequest").GetProperty("required")));
        Assert.Equal(["refresh_token"], Strings(Schema("LogoutRequest").GetProperty("required")));
    }
}
