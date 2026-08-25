using System.Text.Json;

namespace LuxMap.Api.Tests;

/// <summary>Contract mục 0 — <c>{page, page_size, total, items[]}</c>, <c>page_size</c> tối đa 200.</summary>
public class PaginationTests(LuxMapApiFactory factory) : IClassFixture<LuxMapApiFactory>
{
    private HttpClient Client => factory.CreateClient();

    private async Task<JsonElement> GetPagedAsync(string query)
    {
        var response = await Client.GetAsync($"/api/v1/_test/paged{query}");
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
    }

    [Fact]
    public async Task Response_uses_the_contract_pagination_shape()
    {
        var root = await GetPagedAsync("?page=2&page_size=50");

        Assert.Equal(["page", "page_size", "total", "items"], root.EnumerateObject().Select(p => p.Name));
        Assert.Equal(2, root.GetProperty("page").GetInt32());
        Assert.Equal(50, root.GetProperty("page_size").GetInt32());
        Assert.Equal(1337, root.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Page_size_500_is_clamped_to_200_without_an_error()
    {
        var root = await GetPagedAsync("?page=1&page_size=500");

        Assert.Equal(200, root.GetProperty("page_size").GetInt32());
    }

    [Fact]
    public async Task Defaults_apply_when_the_client_sends_nothing()
    {
        var root = await GetPagedAsync(string.Empty);

        Assert.Equal(1, root.GetProperty("page").GetInt32());
        Assert.Equal(50, root.GetProperty("page_size").GetInt32());
    }

    [Fact]
    public async Task Api_v1_url_segment_routes_correctly()
    {
        var response = await Client.GetAsync("/api/v1/_test/ok");
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("POLE-0001", body);
    }
}
