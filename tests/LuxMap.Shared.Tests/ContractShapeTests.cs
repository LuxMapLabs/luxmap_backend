using System.Text.Json;
using LuxMap.Shared.Contracts.Errors;
using LuxMap.Shared.Contracts.Paging;
using LuxMap.Shared.Serialization;

namespace LuxMap.Shared.Tests;

/// <summary>Contract v1.1 section 0 — the error shape and the pagination shape.</summary>
public class ContractShapeTests
{
    private static readonly JsonSerializerOptions Options = LuxMapJsonOptions.Default;

    [Fact]
    public void Error_response_has_exactly_the_contract_shape()
    {
        var json = JsonSerializer.Serialize(
            ApiErrorResponse.Create(ErrorCodes.BboxTooLarge, "Zoom in to see detail"),
            Options);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal(["error"], root.EnumerateObject().Select(p => p.Name));

        var error = root.GetProperty("error");
        Assert.Equal(["code", "message", "details"], error.EnumerateObject().Select(p => p.Name));
        Assert.Equal("BBOX_TOO_LARGE", error.GetProperty("code").GetString());
        Assert.Equal("{}", error.GetProperty("details").GetRawText());
    }

    [Fact]
    public void Error_details_keys_are_snake_cased()
    {
        var json = JsonSerializer.Serialize(
            ApiErrorResponse.Create(
                ErrorCodes.CommuneForbidden,
                "Outside the permitted commune scope",
                new Dictionary<string, object?> { ["RequestedCommuneId"] = "COM-009" }),
            Options);

        Assert.Contains("\"requested_commune_id\":\"COM-009\"", json);
    }

    [Fact]
    public void Paged_result_has_exactly_the_contract_shape()
    {
        var request = PageRequest.Create(page: 2, pageSize: 50);
        var json = JsonSerializer.Serialize(
            PagedResult<string>.From(request, total: 137, items: ["FAULT-0001", "FAULT-0002"]),
            Options);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal(["page", "page_size", "total", "items"], root.EnumerateObject().Select(p => p.Name));
        Assert.Equal(2, root.GetProperty("page").GetInt32());
        Assert.Equal(50, root.GetProperty("page_size").GetInt32());
        Assert.Equal(137, root.GetProperty("total").GetInt32());
        Assert.Equal(2, root.GetProperty("items").GetArrayLength());
    }

    [Theory]
    [InlineData(null, null, 1, 50)]      // defaults, matching the ?page=1&page_size=50 example
    [InlineData(3, 25, 3, 25)]
    [InlineData(1, 200, 1, 200)]         // exactly at the ceiling
    [InlineData(1, 500, 1, 200)]         // silently clamped to 200
    [InlineData(0, 0, 1, 1)]
    [InlineData(-5, -5, 1, 1)]
    public void Page_request_clamps_into_the_contract_range(int? page, int? pageSize, int expectedPage, int expectedPageSize)
    {
        var request = PageRequest.Create(page, pageSize);

        Assert.Equal(expectedPage, request.Page);
        Assert.Equal(expectedPageSize, request.PageSize);
    }

    [Fact]
    public void Page_size_ceiling_is_200()
        => Assert.Equal(200, PageRequest.MaxPageSize);

    [Fact]
    public void Skip_is_derived_from_the_clamped_values()
    {
        Assert.Equal(0, PageRequest.Create(1, 50).Skip);
        Assert.Equal(50, PageRequest.Create(2, 50).Skip);
        Assert.Equal(400, PageRequest.Create(3, 200).Skip);
    }
}
