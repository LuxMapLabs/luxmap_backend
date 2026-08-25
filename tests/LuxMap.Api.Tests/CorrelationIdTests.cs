using System.Net.Http.Json;
using LuxMap.Shared.Contracts;

namespace LuxMap.Api.Tests;

public class CorrelationIdTests(LuxMapApiFactory factory) : IClassFixture<LuxMapApiFactory>
{
    private HttpClient Client => factory.CreateClient();

    [Fact]
    public async Task Correlation_id_is_returned_on_successful_responses_too()
    {
        var response = await Client.GetAsync("/api/v1/_test/ok");

        response.EnsureSuccessStatusCode();
        Assert.True(response.Headers.Contains(ApiHeaders.CorrelationId));
        Assert.False(string.IsNullOrWhiteSpace(response.Headers.GetValues(ApiHeaders.CorrelationId).Single()));
    }

    [Fact]
    public async Task Client_supplied_correlation_id_is_echoed_back()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/_test/ok");
        request.Headers.Add(ApiHeaders.CorrelationId, "fm-android-42.7");

        var response = await Client.SendAsync(request);

        Assert.Equal("fm-android-42.7", response.Headers.GetValues(ApiHeaders.CorrelationId).Single());
    }

    [Fact]
    public async Task Correlation_id_is_generated_when_the_client_sends_none()
    {
        var first = await Client.GetAsync("/api/v1/_test/ok");
        var second = await Client.GetAsync("/api/v1/_test/ok");

        var a = first.Headers.GetValues(ApiHeaders.CorrelationId).Single();
        var b = second.Headers.GetValues(ApiHeaders.CorrelationId).Single();

        Assert.True(Guid.TryParse(a, out _));
        Assert.NotEqual(a, b);
    }

    [Theory]
    [InlineData("có\nxuống dòng")]
    [InlineData("dấu cách và ký tự lạ ()")]
    public async Task Hostile_correlation_id_from_client_is_replaced(string hostile)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/_test/ok");
        request.Headers.TryAddWithoutValidation(ApiHeaders.CorrelationId, hostile);

        var response = await Client.SendAsync(request);
        var returned = response.Headers.GetValues(ApiHeaders.CorrelationId).Single();

        Assert.True(Guid.TryParse(returned, out _));
    }
}
