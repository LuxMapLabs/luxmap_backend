using System.Net;
using System.Text.Json;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

namespace LuxMap.Api.Tests;

/// <summary>Production Caddy's early body guards, in a throwaway container; no real API or MinIO.</summary>
public sealed class PolePhotoProxyTests
{
    private static async Task<(IContainer Caddy, HttpClient Client)> StartCaddy()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "LuxMap.slnx"))) root = root.Parent;
        Assert.NotNull(root);
        var file = new FileInfo(Path.Combine(root.FullName, "deploy", "caddy", "Caddyfile"));
        const string image = "caddy:2.10.2-alpine@sha256:4c6e91c6ed0e2fa03efd5b44747b625fec79bc9cd06ac5235a779726618e530d";
        var caddy = new ContainerBuilder(image).WithPortBinding(80, true)
            .WithEnvironment("LUXMAP_DOMAIN", ":80").WithEnvironment("ACME_EMAIL", "proxy-test@example.invalid")
            .WithResourceMapping(file, "/etc/caddy/").Build();
        await caddy.StartAsync();
        var client = new HttpClient { BaseAddress = new Uri($"http://{caddy.Hostname}:{caddy.GetMappedPublicPort(80)}") };
        for (var n = 0; ; n++)
        {
            try { await client.GetAsync("/api/v1/internal/proxy-test"); break; }
            catch (HttpRequestException) when (n < 40) { await Task.Delay(100); }
        }
        return (caddy, client);
    }

    private static async Task<HttpResponseMessage> Send(HttpClient client, HttpMethod method, string path, int bytes)
    {
        using var body = new ByteArrayContent(new byte[bytes]);
        using var request = new HttpRequestMessage(method, path) { Content = body };
        request.Headers.ExpectContinue = true; // Receive the declared-length refusal before streaming the body.
        return await client.SendAsync(request);
    }

    private static async Task AssertTooLarge(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Equal("UPLOAD_TOO_LARGE", JsonDocument.Parse(await response.Content.ReadAsStringAsync())
            .RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Caddy_answers_413_for_a_declared_oversize_pole_photo_without_contacting_an_API()
    {
        var (caddy, client) = await StartCaddy();
        await using var _ = caddy; using var __ = client;
        await AssertTooLarge(await Send(client, HttpMethod.Post, "/api/v1/work-orders/WO-0001/pole-photos", 17 * 1024 * 1024 + 1));
    }

    /// <summary>POLE-VIDEO D-6: a clip declared over 100 MiB is refused by Caddy itself, on THIS route only — a survey clip may be 300 MiB.</summary>
    [Fact]
    public async Task Caddy_answers_413_for_a_declared_oversize_pole_video_on_its_route_only()
    {
        var (caddy, client) = await StartCaddy();
        await using var _ = caddy; using var __ = client;
        const int overHundredMiB = 100 * 1024 * 1024 + 1;
        await AssertTooLarge(await Send(client, HttpMethod.Put, "/api/v1/work-orders/WO-0001/pole-videos/0f5a7b6e-6a0c-4f3b-9d3e-2f6d1f0a9c11", overHundredMiB));
        // The same size to a survey clip is within ITS limit: Caddy does not answer 413 and tries the API, which is not there in this test —
        // so the answer is a 502, or the connection is closed under the body. Only a 413 would mean the new guard leaked onto this route.
        var survey = await Record.ExceptionAsync(async () =>
        {
            using var response = await Send(client, HttpMethod.Put, "/api/v1/sweeps/SWP-0001/clips/0", overHundredMiB);
            Assert.NotEqual(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        });
        Assert.True(survey is null or HttpRequestException, survey?.ToString());
    }
}
