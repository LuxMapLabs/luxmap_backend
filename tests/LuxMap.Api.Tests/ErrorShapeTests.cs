using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using LuxMap.Shared.Contracts;
using LuxMap.Shared.Contracts.Errors;

namespace LuxMap.Api.Tests;

/// <summary>
/// Contract mục 0 — MỌI lỗi phải cùng hình dạng <c>{ error: { code, message, details } }</c>
/// kèm correlation id, không có ngoại lệ nào trả RFC 7807 ProblemDetails.
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

        // Dấu hiệu của ProblemDetails — không được xuất hiện ở bất kỳ đâu.
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

        // Production: không lộ loại ngoại lệ hay thông điệp nội bộ.
        Assert.False(error.GetProperty("details").TryGetProperty("exception", out _));
        Assert.DoesNotContain("nổ có chủ đích", root.ToString());
    }

    [Fact]
    public async Task Validation_failure_returns_contract_error_shape_not_problem_details()
    {
        var body = new StringContent(
            """{ "note": "ngắn", "status_confidence": 5 }""",
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
    public async Task Unmatched_route_also_returns_contract_error_shape()
    {
        var response = await Client.GetAsync("/api/v1/khong-ton-tai");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        AssertContractErrorShape(await ReadAsync(response));
    }

    [Fact]
    public async Task Every_known_contract_error_code_is_registered_with_its_status()
    {
        Assert.Equal(6, KnownErrors.All.Count);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, KnownErrors.Find(ErrorCodes.BboxTooLarge)!.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, KnownErrors.Find(ErrorCodes.PoleNotFound)!.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, KnownErrors.Find(ErrorCodes.LocationRequired)!.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, KnownErrors.Find(ErrorCodes.FaultTypeNotReportable)!.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, KnownErrors.Find(ErrorCodes.CommuneForbidden)!.StatusCode);

        // Contract mục 2.8: trùng client_op_id trả 200, KHÔNG phải lỗi.
        Assert.Equal(HttpStatusCode.OK, KnownErrors.Find(ErrorCodes.DuplicateOp)!.StatusCode);
    }
}
