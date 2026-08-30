using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using LuxMap.Shared.Contracts;
using LuxMap.Shared.Contracts.Errors;

namespace LuxMap.Api.Tests;

/// <summary>
/// Contract section 0 — EVERY error uses the same <c>{ error: { code, message, details } }</c> shape
/// plus a correlation id, and nothing anywhere returns RFC 7807 ProblemDetails.
/// </summary>
public class ErrorShapeTests(LuxMapApiFactory factory) : IClassFixture<LuxMapApiFactory>
{
    private HttpClient Client => factory.CreateClient();

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response)
        => JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    private static void AssertContractErrorShape(JsonElement root)
    {
        Assert.Equal(["error"], root.EnumerateObject().Select(p => p.Name));
        var error = root.GetProperty("error");
        Assert.Equal(["code", "message", "details"], error.EnumerateObject().Select(p => p.Name));

        // ProblemDetails fingerprints — none of these may appear anywhere.
        Assert.False(root.TryGetProperty("type", out _));
        Assert.False(root.TryGetProperty("title", out _));
        Assert.False(root.TryGetProperty("status", out _));
        Assert.False(root.TryGetProperty("traceId", out _));
    }

    [Fact]
    public async Task Unhandled_exception_returns_contract_error_shape_with_correlation_id()
    {
        var response = await Client.GetAsync("/api/v1/_test/boom");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);

        var root = await ReadAsync(response);
        AssertContractErrorShape(root);

        var error = root.GetProperty("error");
        Assert.Equal(ErrorCodes.InternalError, error.GetProperty("code").GetString());

        var headerId = response.Headers.GetValues(ApiHeaders.CorrelationId).Single();
        Assert.Equal(headerId, error.GetProperty("details").GetProperty("correlation_id").GetString());

        // Production: neither the exception type nor any internal message may leak.
        Assert.False(error.GetProperty("details").TryGetProperty("exception", out _));
        Assert.DoesNotContain("deliberate blow-up", root.ToString());
    }

    [Fact]
    public async Task Validation_failure_returns_contract_error_shape_not_problem_details()
    {
        var body = new StringContent(
            """{ "note": "short", "status_confidence": 5 }""",
            Encoding.UTF8,
            "application/json");

        var response = await Client.PostAsync("/api/v1/_test/validated", body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var root = await ReadAsync(response);
        AssertContractErrorShape(root);

        var error = root.GetProperty("error");
        Assert.Equal(ErrorCodes.ValidationFailed, error.GetProperty("code").GetString());

        var details = error.GetProperty("details");
        Assert.True(details.TryGetProperty("note", out var note));
        Assert.True(note.GetArrayLength() > 0);
        Assert.True(details.TryGetProperty("status_confidence", out _));
        Assert.False(string.IsNullOrWhiteSpace(details.GetProperty("correlation_id").GetString()));
    }

    [Fact]
    public async Task Known_contract_error_keeps_its_code_status_and_details()
    {
        var response = await Client.GetAsync("/api/v1/_test/known-error");

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);

        var error = (await ReadAsync(response)).GetProperty("error");
        Assert.Equal(ErrorCodes.BboxTooLarge, error.GetProperty("code").GetString());
        Assert.Equal(4211, error.GetProperty("details").GetProperty("pole_count").GetInt32());
    }

    [Fact]
    public async Task Unmatched_route_returns_401_for_anonymous_callers_after_BE08()
    {
        // BE-08 installs a fallback policy, so every unauthenticated request stops at authorization,
        // EVEN for routes that do not exist. Useful side effect: strangers cannot probe which routes are real.
        var response = await Client.GetAsync("/api/v1/khong-ton-tai");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        AssertContractErrorShape(await ReadAsync(response));
    }

    [Fact]
    public async Task Unmatched_route_returns_404_once_authenticated()
    {
        var client = factory.CreateClient();
        var tokens = await client.LoginAsync("engineer", "SEED_ENGINEER_PASSWORD");
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", tokens.AccessToken);

        var response = await client.GetAsync("/api/v1/khong-ton-tai");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        AssertContractErrorShape(await ReadAsync(response));
    }

    [Fact]
    public async Task Every_known_contract_error_code_is_registered_with_its_status()
    {
        // 6 codes specified by the Contract + 3 /auth codes from BE-07 + 1 authentication code from BE-08.
        Assert.Equal(10, KnownErrors.All.Count);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, KnownErrors.Find(ErrorCodes.BboxTooLarge)!.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, KnownErrors.Find(ErrorCodes.PoleNotFound)!.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, KnownErrors.Find(ErrorCodes.LocationRequired)!.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, KnownErrors.Find(ErrorCodes.FaultTypeNotReportable)!.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, KnownErrors.Find(ErrorCodes.CommuneForbidden)!.StatusCode);

        // Contract section 2.8: a duplicate client_op_id returns 200, NOT an error.
        Assert.Equal(HttpStatusCode.OK, KnownErrors.Find(ErrorCodes.DuplicateOp)!.StatusCode);

        // BE-07 — wrong username and wrong password deliberately SHARE one code.
        Assert.Equal(HttpStatusCode.Unauthorized, KnownErrors.Find(ErrorCodes.InvalidCredentials)!.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, KnownErrors.Find(ErrorCodes.AccountLocked)!.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, KnownErrors.Find(ErrorCodes.InvalidRefreshToken)!.StatusCode);

        // BE-08 — one code for EVERY authentication failure.
        Assert.Equal(HttpStatusCode.Unauthorized, KnownErrors.Find(ErrorCodes.Unauthenticated)!.StatusCode);
    }
}
