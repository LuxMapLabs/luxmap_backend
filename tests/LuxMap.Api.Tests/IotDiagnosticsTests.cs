using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using LuxMap.Modules.Assets.Entities;
using LuxMap.Modules.IOT;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LuxMap.Api.Tests;

[Collection(nameof(AssetDatabaseCollection))]
public sealed class IotDiagnosticsTests(AssetImportFixture fixture)
{
    [Fact]
    public async Task Registration_requires_admin_and_allowlisted_origin_and_reads_are_commune_scoped()
    {
        using var factory = new LuxMapApiFactory();
        using var host = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("IotDiagnostics:AllowedOrigins:0", "https://telemetry.example.test");
            builder.UseSetting("Mqtt:ProvisioningKey", new string('a', 32));
            builder.ConfigureServices(services => services.AddHttpClient<IotDiagnosticsService>()
                .ConfigurePrimaryHttpMessageHandler(() => new InvalidUpstream()));
        });
        var rig = await new LightingTestRig(fixture).RigAsync(issueSecret: false);
        var pole = await fixture.QueryAsync(db => db.Set<Pole>().IgnoreQueryFilters()
            .Where(p => p.FeederId == rig.Feeders[1]).Select(p => p.PoleId).SingleAsync());
        using var adminToken = await fixture.SeededClientAsync("admin", "SEED_ADMIN_PASSWORD");
        using var managerToken = await fixture.ManagerClientAsync();
        using var foreignToken = await fixture.ForeignCommuneClientAsync();
        using var admin = AuthorizedClient(host.CreateClient(), adminToken);
        using var manager = AuthorizedClient(host.CreateClient(), managerToken);
        using var foreign = AuthorizedClient(host.CreateClient(), foreignToken);

        const string root = "/api/v1/iot";
        var id = "iot-" + Guid.NewGuid().ToString("N");
        var server = new { server_id = id, base_url = "https://telemetry.example.test", commune_id = fixture.CommuneId, location = new { lat = 10.78, lng = 106.7 } };
        var nodePath = $"{root}/nodes/{rig.Node}";
        var profile = new
        {
            server_id = id, device_id = "probe", feeder_id = rig.Feeders[1], pole_id = pole,
            relay_no = 2, nominal_voltage = 5, nominal_current_ma = 100, nominal_power_mw = 500,
        };

        Assert.Equal(HttpStatusCode.Forbidden, (await manager.PostAsJsonAsync($"{root}/servers", server)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.PostAsJsonAsync($"{nodePath}/source", profile)).StatusCode);
        foreach (var invalid in new[] { "http://127.0.0.1:5000", "http://10.0.0.8", "https://telemetry.example.test/private" })
        {
            Assert.Equal(HttpStatusCode.BadRequest,
                (await admin.PostAsJsonAsync($"{root}/servers", new { server_id = id, base_url = invalid })).StatusCode);
        }
        Assert.Equal(HttpStatusCode.Created, (await admin.PostAsJsonAsync($"{root}/servers", server)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync($"{root}/servers", server)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await foreign.PostAsJsonAsync($"{nodePath}/source", profile)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await manager.PostAsJsonAsync($"{nodePath}/source",
            new { profile.server_id, device_id = "unauthorized", profile.feeder_id, profile.pole_id,
                profile.relay_no, profile.nominal_voltage, profile.nominal_current_ma, profile.nominal_power_mw })).StatusCode);
        var credentialPath = $"{root}/servers/{id}/gateways/TEST-GW/credential";
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.PostAsync(credentialPath, null)).StatusCode);
        using var credential = await admin.PostAsync(credentialPath, null);
        Assert.Equal(HttpStatusCode.OK, credential.StatusCode);
        Assert.True(credential.Headers.CacheControl?.NoStore == true);
        Assert.Equal(HttpStatusCode.Created, (await manager.PostAsJsonAsync($"{nodePath}/source", profile)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await manager.PostAsJsonAsync($"{nodePath}/source", profile)).StatusCode);
        var auditCount = await fixture.QueryAsync(db => db.Database.SqlQuery<int>(
            $"SELECT count(*)::integer AS \"Value\" FROM iot_registration_audit WHERE server_id = {id}").SingleAsync());
        Assert.Equal(2, auditCount);

        foreach (var route in new[] { "electrical", "diagnostics" })
        {
            Assert.Equal(HttpStatusCode.NotFound, (await foreign.GetAsync($"{nodePath}/{route}")).StatusCode);
            var unavailable = await manager.GetAsync($"{nodePath}/{route}");
            Assert.Equal(HttpStatusCode.ServiceUnavailable, unavailable.StatusCode);
            var json = await unavailable.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("IOT_SOURCE_UNAVAILABLE", json.GetProperty("error").GetProperty("code").GetString());
        }

        // Other tests can leave earlier nodes in the shared fixture database.
        var matchingNodes = new List<JsonElement>();
        for (var page = 1; ; page++)
        {
            var nodes = await manager.GetFromJsonAsync<JsonElement>($"{root}/nodes?page={page}");
            matchingNodes.AddRange(nodes.GetProperty("items").EnumerateArray()
                .Where(item => item.GetProperty("node_id").GetString() == rig.Node));
            if (page * nodes.GetProperty("page_size").GetInt32() >= nodes.GetProperty("total").GetInt32())
                break;
        }
        var node = Assert.Single(matchingNodes);
        Assert.Equal("source_unavailable", node.GetProperty("status").GetString());
        var servers = await manager.GetFromJsonAsync<JsonElement>($"{root}/servers");
        var visible = Assert.Single(servers.EnumerateArray(), item => item.GetProperty("server_id").GetString() == id);
        Assert.Equal("unreachable", visible.GetProperty("status").GetString());
        var hidden = await foreign.GetFromJsonAsync<JsonElement>($"{root}/servers");
        Assert.DoesNotContain(hidden.EnumerateArray(), item => item.GetProperty("server_id").GetString() == id);
    }

    private static HttpClient AuthorizedClient(HttpClient client, HttpClient source)
    {
        client.DefaultRequestHeaders.Authorization = source.DefaultRequestHeaders.Authorization;
        return client;
    }

    private sealed class InvalidUpstream : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/authorization", StringComparison.Ordinal))
                return Task.FromResult(new HttpResponseMessage(path.EndsWith("/probe/authorization", StringComparison.Ordinal)
                    ? HttpStatusCode.NoContent : HttpStatusCode.NotFound));
            if (path.EndsWith("/TEST-GW/credential", StringComparison.Ordinal))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new { clientId = "TEST-GW", username = "TEST-GW",
                        password = new string('A', 64), nodeIds = new[] { "probe" } }),
                });
            var content = new StringContent("unexpected upstream response", Encoding.UTF8, "text/plain");
            if (request.RequestUri!.AbsolutePath.EndsWith("/mqtt", StringComparison.Ordinal))
                content.Headers.ContentType = null;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }
    }
}
