using System.Net;
using System.Text.Json;
using DotNet.Testcontainers.Builders;

namespace LuxMap.Api.Tests;

/// <summary>Production Caddy's early body guard, in a throwaway container; no real API or MinIO.</summary>
public sealed class PolePhotoProxyTests
{
    [Fact]
    public async Task Caddy_answers_413_for_a_declared_oversize_pole_photo_without_contacting_an_API()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "LuxMap.slnx"))) root = root.Parent;
        Assert.NotNull(root);
        var file = new FileInfo(Path.Combine(root.FullName, "deploy", "caddy", "Caddyfile"));
        const string image = "caddy:2.10.2-alpine@sha256:4c6e91c6ed0e2fa03efd5b44747b625fec79bc9cd06ac5235a779726618e530d";
        await using var caddy = new ContainerBuilder(image).WithPortBinding(80, true)
            .WithEnvironment("LUXMAP_DOMAIN", ":80").WithEnvironment("ACME_EMAIL", "proxy-test@example.invalid")
            .WithResourceMapping(file, "/etc/caddy/").Build();
        await caddy.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri($"http://{caddy.Hostname}:{caddy.GetMappedPublicPort(80)}") };
        for (var n = 0; ; n++)
        {
            try { await client.GetAsync("/api/v1/internal/proxy-test"); break; }
            catch (HttpRequestException) when (n < 40) { await Task.Delay(100); }
        }
        using var body = new ByteArrayContent(new byte[17 * 1024 * 1024 + 1]);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/work-orders/WO-0001/pole-photos") { Content = body };
        request.Headers.ExpectContinue = true; // Receive the declared-length refusal before streaming the body.
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Equal("UPLOAD_TOO_LARGE", JsonDocument.Parse(await response.Content.ReadAsStringAsync())
            .RootElement.GetProperty("error").GetProperty("code").GetString());
    }
}
