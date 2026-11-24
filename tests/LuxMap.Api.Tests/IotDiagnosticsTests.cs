using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using LuxMap.Modules.IOT;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace LuxMap.Api.Tests;

[Collection(nameof(AssetDatabaseCollection))]
public sealed class IotDiagnosticsTests(AssetImportFixture fixture)
{
    [Fact]
    public async Task Admin_registers_two_feeders_on_one_gateway_and_retries_without_creating_another_node()
    {
        var bridge = new RegistrationBridge();
        using var factory = new LuxMapApiFactory();
        using var host = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("IotDiagnostics:BaseUrl", "https://registration.example.test");
            builder.UseSetting("IotDiagnostics:AllowedOrigins:0", "https://registration.example.test");
            builder.UseSetting("Mqtt:ProvisioningKey", new string('a', 32));
            builder.ConfigureServices(services => services.AddHttpClient<IotDiagnosticsService>()
                .ConfigurePrimaryHttpMessageHandler(() => bridge));
        });
        using var adminToken = await fixture.SeededClientAsync("admin", "SEED_ADMIN_PASSWORD");
        using var managerToken = await fixture.ManagerClientAsync();
        using var admin = AuthorizedClient(host.CreateClient(), adminToken);
        using var manager = AuthorizedClient(host.CreateClient(), managerToken);
        var setup = new LightingTestRig(fixture);
        var cabinet = await setup.NewCabinetAsync();
        var feeder = await setup.NewFeederAsync(cabinet);
        var otherFeeder = await setup.NewFeederAsync(cabinet);
        var server = "iot-" + Guid.NewGuid().ToString("N");
        bridge.ServerId = server;
        const string register = "/api/v1/iot/nodes";
        using var serverResponse = await admin.PostAsJsonAsync("/api/v1/iot/servers", new
        {
            server_id = server, commune_id = fixture.CommuneId, location = new { lat = 13.5, lng = 109.5 }, radius_km = 0.75,
        });
        Assert.Equal(HttpStatusCode.Created, serverResponse.StatusCode);
        var serverRow = await fixture.QueryAsync(db => db.Set<LuxMap.Modules.IOT.IotServerRegistration>().SingleAsync(row => row.ServerId == server));
        Assert.Equal(0.75, serverRow.RadiusKm);
        using var invalidServerRadius = await admin.PutAsJsonAsync($"/api/v1/iot/servers/{server}/location", new { commune_id = fixture.CommuneId, location = new { lat = 13.5, lng = 109.5 }, radius_km = -0.1 });
        Assert.Equal(HttpStatusCode.BadRequest, invalidServerRadius.StatusCode);

        object Payload(string feederId, string deviceId) => new
        {
            server_id = server, feeder_id = feederId, device_id = deviceId, radius_km = 0.1,
            nominal_voltage = 5, nominal_current_ma = 100, nominal_power_mw = 500,
        };

        using var forbidden = await manager.PostAsJsonAsync(register, Payload(feeder, "P001"));
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        bridge.RejectAppend = true;
        using var failed = await admin.PostAsJsonAsync(register, Payload(feeder, "P001"));
        Assert.True(failed.StatusCode == HttpStatusCode.ServiceUnavailable, await failed.Content.ReadAsStringAsync());
        Assert.Equal(0, await fixture.QueryAsync(db => db.Set<LuxMap.Modules.IOT.Entities.IotNode>().IgnoreQueryFilters()
            .CountAsync(node => node.CabinetId == cabinet)));

        bridge.RejectAppend = false;
        using var first = await admin.PostAsJsonAsync(register, Payload(feeder, "P001"));
        Assert.True(first.StatusCode == HttpStatusCode.Created, await first.Content.ReadAsStringAsync());
        var firstBody = await first.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(server, firstBody.GetProperty("server_id").GetString());
        Assert.False(firstBody.TryGetProperty("gateway_id", out _));
        var storedNode = await fixture.QueryAsync(db => db.Set<LuxMap.Modules.IOT.Entities.IotNode>().IgnoreQueryFilters()
            .SingleAsync(node => node.NodeId == firstBody.GetProperty("node_id").GetString()));
        Assert.Equal(LuxMap.Shared.Contracts.Enums.DataSource.CalibrationRig, storedNode.DataSource);
        Assert.True(storedNode.SupportsRemoteControl);
        Assert.Equal(cabinet, storedNode.CabinetId);
        Assert.Equal(0.1, storedNode.RadiusKm);
        using var invalidNodeRadius = await manager.PutAsJsonAsync($"/api/v1/iot/nodes/{storedNode.NodeId}", new { server_id = server, feeder_id = feeder, device_id = "P001", nominal_voltage = 5, nominal_current_ma = 100, nominal_power_mw = 500, radius_km = 1001.0 });
        Assert.Equal(HttpStatusCode.BadRequest, invalidNodeRadius.StatusCode);
        using var repeat = await admin.PostAsJsonAsync(register, Payload(feeder, "P001"));
        Assert.Equal(HttpStatusCode.OK, repeat.StatusCode);
        var repeatBody = await repeat.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(firstBody.GetProperty("node_id").GetString(), repeatBody.GetProperty("node_id").GetString());

        using var conflict = await admin.PostAsJsonAsync(register, Payload(otherFeeder, "P001"));
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        using var second = await admin.PostAsJsonAsync(register, Payload(otherFeeder, "P002"));
        Assert.True(second.StatusCode == HttpStatusCode.Created, await second.Content.ReadAsStringAsync());
        Assert.Equal(new[] { "P001", "P002" }, bridge.Devices);
        Assert.Equal(2, await fixture.QueryAsync(db => db.Set<LuxMap.Modules.IOT.Entities.IotNode>().IgnoreQueryFilters()
            .CountAsync(node => node.CabinetId == cabinet)));
    }

    [Fact]
    public async Task Server_identity_scopes_status_keys_nodes_and_repairs_existing_source_routes()
    {
        var bridge = new IdentityBridge();
        using var factory = new LuxMapApiFactory();
        using var host = factory.WithWebHostBuilder(builder => {
            builder.UseSetting("IotDiagnostics:BaseUrl", "https://identity.example.test");
            builder.UseSetting("IotDiagnostics:AllowedOrigins:0", "https://identity.example.test");
            builder.UseSetting("Mqtt:ProvisioningKey", new string('a', 32));
            builder.ConfigureServices(services => services.AddHttpClient<IotDiagnosticsService>()
                .ConfigurePrimaryHttpMessageHandler(() => bridge));
        });
        using var adminToken = await fixture.SeededClientAsync("admin", "SEED_ADMIN_PASSWORD");
        using var managerToken = await fixture.ManagerClientAsync();
        using var foreignToken = await fixture.ForeignCommuneClientAsync();
        using var admin = AuthorizedClient(host.CreateClient(), adminToken);
        using var manager = AuthorizedClient(host.CreateClient(), managerToken);
        using var foreign = AuthorizedClient(host.CreateClient(), foreignToken);
        var first = "identity-" + Guid.NewGuid().ToString("N");
        var second = "identity-" + Guid.NewGuid().ToString("N");
        foreach (var server in new[] { first, second }) {
            using var created = await admin.PostAsJsonAsync("/api/v1/iot/servers", new {
                server_id = server, commune_id = fixture.CommuneId, location = new { lat = 13.5, lng = 109.5 }, radius_km = 0.75,
            });
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            using var key = await admin.PostAsync($"/api/v1/iot/servers/{server}/credential", null);
            Assert.Equal(HttpStatusCode.OK, key.StatusCode);
            var body = await key.Content.ReadFromJsonAsync<JsonElement>();
            foreach (var field in new[] { "server_id", "client_id", "username" })
                Assert.Equal(server, body.GetProperty(field).GetString());
        }
        bridge.Online.Add(first);
        var servers = await manager.GetFromJsonAsync<JsonElement>("/api/v1/iot/servers");
        Assert.Equal("online", servers.EnumerateArray().Single(x => x.GetProperty("server_id").GetString() == first).GetProperty("status").GetString());
        Assert.Equal("offline", servers.EnumerateArray().Single(x => x.GetProperty("server_id").GetString() == second).GetProperty("status").GetString());
        Assert.Equal(0.75, servers.EnumerateArray().Single(x => x.GetProperty("server_id").GetString() == first).GetProperty("radius_km").GetDouble());
        using var serverLocation = await admin.PutAsJsonAsync($"/api/v1/iot/servers/{first}/location", new { commune_id = fixture.CommuneId, location = new { lat = 13.6, lng = 109.6 }, radius_km = 1.25 });
        Assert.Equal(HttpStatusCode.NoContent, serverLocation.StatusCode);
        var updatedServers = await manager.GetFromJsonAsync<JsonElement>("/api/v1/iot/servers");
        Assert.Equal(1.25, updatedServers.EnumerateArray().Single(x => x.GetProperty("server_id").GetString() == first).GetProperty("radius_km").GetDouble());
        Assert.Equal(HttpStatusCode.NotFound, (await foreign.GetAsync($"/api/v1/iot/servers/{first}/nodes")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.PostAsync($"/api/v1/iot/servers/{first}/credential", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.GetAsync($"/api/v1/iot/servers/{first}/gateways/{second}/credential")).StatusCode);

        var setup = new LightingTestRig(fixture);
        var cabinet = await setup.NewCabinetAsync();
        var feeder = await setup.NewFeederAsync(cabinet);
        var requestedRadius = 0.25;
        object Payload(string server) => new {
            server_id = server, feeder_id = feeder, device_id = "identity-probe", radius_km = requestedRadius,
            nominal_voltage = 5, nominal_current_ma = 100, nominal_power_mw = 500,
        };
        using var registration = await admin.PostAsJsonAsync("/api/v1/iot/nodes", Payload(first));
        Assert.True(registration.StatusCode == HttpStatusCode.Created, await registration.Content.ReadAsStringAsync());
        var node = (await registration.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("node_id").GetString();
        // Replayed POST and unchanged PUT must repair a missing ACL, without creating another node.
        bridge.Devices[first].Clear();
        using var replay = await admin.PostAsJsonAsync("/api/v1/iot/nodes", Payload(first));
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(node, (await replay.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("node_id").GetString());
        Assert.Contains("identity-probe", bridge.Devices[first]);
        bridge.Devices[first].Clear();
        Assert.Equal(HttpStatusCode.NoContent, (await manager.PutAsJsonAsync($"/api/v1/iot/nodes/{node}", Payload(first))).StatusCode);
        Assert.Contains("identity-probe", bridge.Devices[first]);

        // An established DB source may reconcile a leftover unregistered legacy MQTT owner.
        bridge.Devices[first].Clear();
        bridge.Devices["LEGACY-PROBE"] = ["identity-probe", "unrelated-device"];
        Assert.Equal(HttpStatusCode.NoContent, (await manager.PutAsJsonAsync($"/api/v1/iot/nodes/{node}", Payload(first))).StatusCode);
        Assert.Contains("identity-probe", bridge.Devices[first]);
        Assert.Equal(new[] { "unrelated-device" }, bridge.Devices["LEGACY-PROBE"]);
        bridge.Devices[first].Clear();
        bridge.Devices["LEGACY-PROBE"].Add("identity-probe");
        using var replayLegacy = await admin.PostAsJsonAsync("/api/v1/iot/nodes", Payload(first));
        Assert.Equal(HttpStatusCode.OK, replayLegacy.StatusCode);
        Assert.Contains("identity-probe", bridge.Devices[first]);

        // The same operation must not steal an ACL from another registered server.
        bridge.Devices[first].Clear();
        bridge.Devices[second].Add("identity-probe");
        using var conflict = await manager.PutAsJsonAsync($"/api/v1/iot/nodes/{node}", Payload(first));
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Equal("IOT_REGISTRATION_CONFLICT", (await conflict.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetProperty("code").GetString());
        Assert.Contains("identity-probe", bridge.Devices[second]);
        Assert.Empty(bridge.Devices[first]);
        bridge.Devices[second].Clear();
        bridge.Devices[first].Add("identity-probe");
        requestedRadius = 0.4;
        Assert.Equal(HttpStatusCode.NoContent, (await manager.PutAsJsonAsync($"/api/v1/iot/nodes/{node}", Payload(first))).StatusCode);
        var radiusUpdated = await manager.GetFromJsonAsync<JsonElement>($"/api/v1/iot/nodes/{node}");
        Assert.Equal(0.4, radiusUpdated.GetProperty("radius_km").GetDouble());
        // Configuration remains readable while Bridge is unavailable, without exposing secrets/internal thresholds.
        bridge.RejectRequests = true;
        using var detailsResponse = await manager.GetAsync($"/api/v1/iot/nodes/{node}");
        Assert.Equal(HttpStatusCode.OK, detailsResponse.StatusCode);
        Assert.True(detailsResponse.Headers.CacheControl?.NoStore == true);
        var details = await detailsResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(8, details.EnumerateObject().Count());
        Assert.Equal(node, details.GetProperty("node_id").GetString());
        Assert.Equal(first, details.GetProperty("server_id").GetString());
        Assert.Equal("identity-probe", details.GetProperty("device_id").GetString());
        Assert.Equal(feeder, details.GetProperty("feeder_id").GetString());
        Assert.Equal(5, details.GetProperty("nominal_voltage").GetDouble());
        Assert.Equal(100, details.GetProperty("nominal_current_ma").GetDouble());
        Assert.Equal(500, details.GetProperty("nominal_power_mw").GetDouble());
        Assert.Equal(0.4, details.GetProperty("radius_km").GetDouble());
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync($"/api/v1/iot/nodes/{node}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await foreign.GetAsync($"/api/v1/iot/nodes/{node}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await manager.GetAsync("/api/v1/iot/nodes/NODE-999999999")).StatusCode);
        using (var anonymous = host.CreateClient())
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync($"/api/v1/iot/nodes/{node}")).StatusCode);
        bridge.RejectRequests = false;
        var list = await manager.GetFromJsonAsync<JsonElement>($"/api/v1/iot/servers/{first}/nodes?page=1&page_size=1");
        Assert.Equal(1, list.GetProperty("total").GetInt32());
        Assert.Equal(node, list.GetProperty("items")[0].GetProperty("node_id").GetString());
        var empty = await manager.GetFromJsonAsync<JsonElement>($"/api/v1/iot/servers/{first}/nodes?page=2147483647&page_size=200");
        Assert.Empty(empty.GetProperty("items").EnumerateArray());
        using var moved = await manager.PutAsJsonAsync($"/api/v1/iot/nodes/{node}", Payload(second));
        Assert.True(moved.StatusCode == HttpStatusCode.NoContent, await moved.Content.ReadAsStringAsync());
        Assert.Empty(bridge.Devices[first]);
        Assert.Contains("identity-probe", bridge.Devices[second]);
        var movedDetails = await manager.GetFromJsonAsync<JsonElement>($"/api/v1/iot/nodes/{node}");
        Assert.Equal(second, movedDetails.GetProperty("server_id").GetString());
        var unlinkPath = $"/api/v1/iot/nodes/{node}/feeder";
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.DeleteAsync(unlinkPath)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await foreign.DeleteAsync(unlinkPath)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await manager.DeleteAsync(unlinkPath)).StatusCode);
        Assert.Empty(bridge.Devices[second]);
        Assert.Equal(HttpStatusCode.NotFound, (await manager.GetAsync($"/api/v1/iot/nodes/{node}")).StatusCode);
        var mounted = await manager.GetFromJsonAsync<JsonElement>($"/api/v1/assets/iot-nodes/{node}");
        Assert.Equal(cabinet, mounted.GetProperty("cabinet_id").GetString());
        var removedSource = await fixture.QueryAsync(db => db.Set<IotSourceRegistration>().SingleAsync(s => s.NodeId == node));
        Assert.NotNull(removedSource.DetachedAt);
        var auditBeforeRetry = await fixture.QueryAsync(db => db.Set<IotRegistrationAudit>().CountAsync(a => a.NodeId == node));
        Assert.Equal(HttpStatusCode.NoContent, (await manager.DeleteAsync(unlinkPath)).StatusCode);
        Assert.Equal(auditBeforeRetry, await fixture.QueryAsync(db => db.Set<IotRegistrationAudit>().CountAsync(a => a.NodeId == node)));
        // Persisted removal must suppress an old appsettings source even though the node stays mounted.
        var legacySettings = new IotOptions { Nodes = new() { [node!] = new NodeProfile {
            ServerId = second, DeviceId = "identity-probe", FeederId = feeder,
            NominalVoltage = 5, NominalCurrentMa = 100, NominalPowerMw = 500,
        } } };
        var options = host.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<IotOptions>>().Value;
        options.Nodes[node!] = legacySettings.Nodes[node!];
        var nodeList = await manager.GetFromJsonAsync<JsonElement>("/api/v1/iot/nodes?page=1&page_size=200");
        var unbound = nodeList.GetProperty("items").EnumerateArray().Single(item => item.GetProperty("node_id").GetString() == node);
        Assert.Equal("unconfigured", unbound.GetProperty("status").GetString());
        Assert.Equal(0.4, unbound.GetProperty("radius_km").GetDouble());
        Assert.Contains("identity-probe", removedSource.Profile); // preserved for historical lookups
        options.Nodes.Remove(node!);
        using var reattached = await manager.PutAsJsonAsync($"/api/v1/iot/nodes/{node}", Payload(second));
        Assert.Equal(HttpStatusCode.NoContent, reattached.StatusCode);
        Assert.Contains("identity-probe", bridge.Devices[second]);

        Assert.Equal(HttpStatusCode.NoContent, (await manager.DeleteAsync($"/api/v1/iot/nodes/{node}")).StatusCode);
        Assert.Empty(bridge.Devices[second]);
        Assert.Equal(1, await fixture.QueryAsync(db => db.Set<LuxMap.Modules.IOT.Entities.IotNode>().IgnoreQueryFilters().CountAsync(n => n.NodeId == node)));

        // Removed endpoint must not mutate existing server registrations or bindings.
        using var removedMigration = await admin.PostAsJsonAsync($"/api/v1/iot/servers/{second}/gateway-migration",
            new { legacy_gateway_id = "LEGACY-IDENTITY-TEST" });
        Assert.Equal(HttpStatusCode.NotFound, removedMigration.StatusCode);
        var row = await fixture.QueryAsync(db => db.Set<IotServerRegistration>().SingleAsync(s => s.ServerId == second));
        Assert.Null(row.LegacyGatewayId);
        Assert.Empty(bridge.Devices[second]);
    }

    private sealed class IdentityBridge : HttpMessageHandler
    {
        public Dictionary<string, List<string>> Devices { get; } = [];
        public HashSet<string> Online { get; } = [];
        public bool RejectRequests { get; set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (RejectRequests) throw new HttpRequestException("Bridge unavailable for configuration-read test");
            var parts = request.RequestUri!.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 4 && parts[2] == "gateways" && parts[3] == "routes")
                return new(HttpStatusCode.OK) { Content = JsonContent.Create(Devices.Select(pair => new {
                    gatewayId = pair.Key, deviceIds = pair.Value, credentialIssued = true,
                })) };
            if (parts.Length < 5 || parts[2] != "gateways") return new(HttpStatusCode.NotFound);
            var id = parts[3]; var kind = parts[4];
            Devices.TryAdd(id, []);
            HttpResponseMessage Json(object value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
            if (kind == "credential") return Json(new { clientId = id, username = id, password = new string('A', 64), nodeIds = Devices[id] });
            if (kind == "status") return Json(new { status = Online.Contains(id) ? "online" : "offline", credentialIssued = true });
            if (kind == "route") {
                if (request.Method == HttpMethod.Put) {
                    var body = await request.Content!.ReadFromJsonAsync<JsonElement>(ct);
                    var device = body.GetProperty("deviceId").GetString()!;
                    if (Devices.Any(pair => pair.Key != id && pair.Value.Contains(device))) return new(HttpStatusCode.Conflict);
                    if (!Devices[id].Contains(device)) Devices[id].Add(device);
                    return new(HttpStatusCode.NoContent);
                }
                return Json(new { gatewayId = id, deviceIds = Devices[id], credentialIssued = true });
            }
            if (kind == "devices" && parts.Length == 7 && parts[6] == "binding") {
                var body = await request.Content!.ReadFromJsonAsync<JsonElement>(ct);
                var device = parts[5];
                var owner = Devices.SingleOrDefault(pair => pair.Value.Contains(device)).Key;
                var expected = body.GetProperty("expectedGatewayId").GetString();
                var detach = body.GetProperty("detach").GetBoolean();
                if (owner != expected && !(owner == id && !detach) && !(owner is null && detach)) return new(HttpStatusCode.Conflict);
                foreach (var devices in Devices.Values) devices.Remove(device);
                if (!body.GetProperty("detach").GetBoolean()) Devices[id].Add(device);
                return new(HttpStatusCode.NoContent);
            }
            return new(HttpStatusCode.NotFound);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("http://127.0.0.1:5000")]
    [InlineData("https://telemetry.example.test/private")]
    public async Task Server_registration_rejects_invalid_default_bridge_configuration(string defaultOrigin)
    {
        using var factory = new LuxMapApiFactory();
        using var host = factory.WithWebHostBuilder(builder => {
            builder.UseSetting("IotDiagnostics:BaseUrl", defaultOrigin);
            builder.UseSetting("IotDiagnostics:AllowedOrigins:0", "https://telemetry.example.test");
            builder.UseSetting("IotDiagnostics:AllowedOrigins:1", "https://alternate.example.test");
        });
        using var token = await fixture.SeededClientAsync("admin", "SEED_ADMIN_PASSWORD");
        using var admin = AuthorizedClient(host.CreateClient(), token);
        using var response = await admin.PostAsJsonAsync("/api/v1/iot/servers", new {
            server_id = "iot-" + Guid.NewGuid().ToString("N"),
            commune_id = fixture.CommuneId, location = new { lat = 13.5, lng = 109.5 },
        });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var error = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error");
        Assert.Equal("IOT_SOURCE_UNAVAILABLE", error.GetProperty("code").GetString());
    }

    private sealed class RegistrationBridge : HttpMessageHandler
    {
        public string ServerId { get; set; } = "";
        public List<string> Devices { get; } = [];
        public bool RejectAppend { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Assert.Equal("registration.example.test", request.RequestUri!.Host);
            Assert.True(request.Headers.Contains("X-Iot-Provisioning-Key"));
            if (request.Method == HttpMethod.Get && request.RequestUri.AbsolutePath == $"/api/luxmap/gateways/{ServerId}/route")
                return new(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new { gatewayId = ServerId, deviceIds = Devices, credentialIssued = true }),
                };
            Assert.Equal(HttpMethod.Put, request.Method);
            Assert.Equal($"/api/luxmap/gateways/{ServerId}/route", request.RequestUri.AbsolutePath);
            if (RejectAppend) return new(HttpStatusCode.ServiceUnavailable);
            var route = await request.Content!.ReadFromJsonAsync<JsonElement>(ct);
            var device = route.GetProperty("deviceId").GetString()!;
            if (!Devices.Contains(device)) Devices.Add(device);
            return new(HttpStatusCode.NoContent);
        }
    }

    [Fact]
    public async Task Registration_requires_admin_and_allowlisted_origin_and_reads_are_commune_scoped()
    {
        var origin = "https://registration-" + Guid.NewGuid().ToString("N") + ".example.test";
        using var factory = new LuxMapApiFactory();
        using var host = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("IotDiagnostics:BaseUrl", origin);
            builder.UseSetting("IotDiagnostics:AllowedOrigins:0", origin);
            builder.UseSetting("Mqtt:ProvisioningKey", new string('a', 32));
            builder.ConfigureServices(services => services.AddHttpClient<IotDiagnosticsService>()
                .ConfigurePrimaryHttpMessageHandler(() => new InvalidUpstream()));
        });
        var rig = await new LightingTestRig(fixture).RigAsync(issueSecret: false);
        using var adminToken = await fixture.SeededClientAsync("admin", "SEED_ADMIN_PASSWORD");
        using var managerToken = await fixture.ManagerClientAsync();
        using var foreignToken = await fixture.ForeignCommuneClientAsync();
        using var admin = AuthorizedClient(host.CreateClient(), adminToken);
        using var manager = AuthorizedClient(host.CreateClient(), managerToken);
        using var foreign = AuthorizedClient(host.CreateClient(), foreignToken);

        const string root = "/api/v1/iot";
        var id = "iot-" + Guid.NewGuid().ToString("N");
        var server = new { server_id = id, commune_id = fixture.CommuneId, location = new { lat = 10.78, lng = 106.7 } };
        var nodePath = $"{root}/nodes/{rig.Node}";
        var profile = new
        {
            server_id = id, device_id = "probe", feeder_id = rig.Feeders[0],
            nominal_voltage = 5, nominal_current_ma = 100, nominal_power_mw = 500,
        };

        Assert.Equal(HttpStatusCode.Forbidden, (await manager.PostAsJsonAsync($"{root}/servers", server)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.PutAsJsonAsync(nodePath, profile)).StatusCode);
        foreach (var invalid in new[] { "", "bad/server", "iot server" })
        {
            Assert.Equal(HttpStatusCode.BadRequest,
                (await admin.PostAsJsonAsync($"{root}/servers", new { server_id = invalid })).StatusCode);
        }
        Assert.Equal(HttpStatusCode.Created, (await admin.PostAsJsonAsync($"{root}/servers", server)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync($"{root}/servers", server)).StatusCode);
        var ignoredId = "iot-" + Guid.NewGuid().ToString("N");
        using var ignored = await admin.PostAsJsonAsync($"{root}/servers", new {
            server_id = ignoredId, base_url = "http://127.0.0.1:9999/private",
            commune_id = fixture.CommuneId, location = new { lat = 10.78, lng = 106.7 },
        });
        Assert.Equal(HttpStatusCode.Created, ignored.StatusCode);
        var stored = await fixture.QueryAsync(db => db.Set<IotServerRegistration>()
            .SingleAsync(row => row.ServerId == ignoredId));
        Assert.Equal(origin, stored.BaseUrl);
        Assert.Equal(HttpStatusCode.NotFound, (await foreign.PutAsJsonAsync(nodePath, profile)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await manager.PutAsJsonAsync(nodePath,
            new { profile.server_id, device_id = "unauthorized", profile.feeder_id,
                profile.nominal_voltage, profile.nominal_current_ma, profile.nominal_power_mw })).StatusCode);
        var credentialPath = $"{root}/servers/{id}/credential";
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.PostAsync(credentialPath, null)).StatusCode);
        using var credential = await admin.PostAsync(credentialPath, null);
        Assert.Equal(HttpStatusCode.OK, credential.StatusCode);
        Assert.True(credential.Headers.CacheControl?.NoStore == true);
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.GetAsync(credentialPath)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.DeleteAsync(credentialPath)).StatusCode);
        using var retrieved = await admin.GetAsync(credentialPath);
        Assert.Equal(HttpStatusCode.OK, retrieved.StatusCode);
        Assert.True(retrieved.Headers.CacheControl?.NoStore == true);
        var storedCredential = await retrieved.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(new string('A', 64), storedCredential.GetProperty("password").GetString());
        using var legacyKey = await admin.GetAsync($"{root}/servers/{id}/gateways/LEGACY-GW/credential");
        Assert.Equal(HttpStatusCode.Conflict, legacyKey.StatusCode);
        var legacyBody = await legacyKey.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("IOT_REGISTRATION_CONFLICT", legacyBody.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.Conflict,
            (await admin.GetAsync($"{root}/servers/{id}/gateways/MISSING-GW/credential")).StatusCode);
        using var revoked = await admin.DeleteAsync(credentialPath);
        Assert.Equal(HttpStatusCode.NoContent, revoked.StatusCode);
        var setup = new LightingTestRig(fixture);
        var otherFeeder = await setup.NewFeederAsync(await setup.NewCabinetAsync());
        var wrongFeeder = new
        {
            profile.server_id, profile.device_id, feeder_id = otherFeeder,
            profile.nominal_voltage, profile.nominal_current_ma, profile.nominal_power_mw,
        };
        Assert.Equal(HttpStatusCode.BadRequest, (await manager.PutAsJsonAsync(nodePath, wrongFeeder)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await manager.PutAsJsonAsync(nodePath, profile)).StatusCode);
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

        Assert.Equal(HttpStatusCode.NotFound, (await foreign.PutAsJsonAsync(nodePath, profile)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await foreign.DeleteAsync(nodePath)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await manager.PutAsJsonAsync(nodePath, wrongFeeder)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await manager.PutAsJsonAsync(nodePath, profile)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await manager.DeleteAsync(nodePath)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await manager.DeleteAsync(nodePath)).StatusCode);
        var detached = await manager.GetFromJsonAsync<JsonElement>($"/api/v1/assets/iot-nodes/{rig.Node}");
        Assert.Equal(JsonValueKind.Null, detached.GetProperty("cabinet_id").ValueKind);
        Assert.Empty(detached.GetProperty("relays").EnumerateArray());
        Assert.Equal(HttpStatusCode.NoContent, (await manager.PutAsJsonAsync(nodePath, profile)).StatusCode);
        var legacyDeviceId = "legacy-probe";
        var legacyProfile = JsonSerializer.Serialize(new { profile.server_id, device_id = legacyDeviceId, profile.feeder_id, pole_id = "legacy-pole", relay_no = 2, profile.nominal_voltage, profile.nominal_current_ma, profile.nominal_power_mw });
        await fixture.QueryAsync(db => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE iot_source_registration SET device_id = {legacyDeviceId}, profile = CAST({legacyProfile} AS jsonb) WHERE node_id = {rig.Node}"));
        using var legacy = await manager.GetAsync($"{nodePath}/diagnostics");
        var legacyJson = await legacy.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(legacy.IsSuccessStatusCode, legacyJson.GetRawText());
        Assert.Equal(profile.feeder_id, legacyJson.GetProperty("feeder_id").GetString());
        Assert.Equal("no_data", legacyJson.GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.NoContent, (await manager.DeleteAsync(nodePath)).StatusCode);
        var empty = await manager.GetFromJsonAsync<JsonElement>($"{root}/nodes?page=2147483647&page_size=200");
        Assert.Empty(empty.GetProperty("items").EnumerateArray());
        var routesPath = $"{root}/servers/{id}/gateways/routes";
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.GetAsync(routesPath)).StatusCode);
        var routes = await admin.GetFromJsonAsync<JsonElement>(routesPath);
        Assert.True(Assert.Single(routes.EnumerateArray()).GetProperty("credential_issued").GetBoolean());
    }


    [Fact]
    public async Task Direct_feeder_control_reports_confirmed_and_automatic_power_for_poles_and_fixtures()
    {
        var bridge = new ReportedPowerBridge();
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        using var factory = new LuxMapApiFactory();
        using var host = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("IotDiagnostics:BaseUrl", "https://telemetry.example.test");
            builder.UseSetting("IotDiagnostics:AllowedOrigins:0", "https://telemetry.example.test");
            builder.UseSetting("Mqtt:ProvisioningKey", new string('a', 32));
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<TimeProvider>(clock);
                services.AddHttpClient<IotDiagnosticsService>().ConfigurePrimaryHttpMessageHandler(() => bridge);
            });
        });
        using var adminToken = await fixture.SeededClientAsync("admin", "SEED_ADMIN_PASSWORD");
        using var managerToken = await fixture.ManagerClientAsync();
        using var admin = AuthorizedClient(host.CreateClient(), adminToken);
        using var manager = AuthorizedClient(host.CreateClient(), managerToken);

        var setup = new LightingTestRig(fixture);
        var cabinet = await setup.NewCabinetAsync();
        var feeder = await setup.NewFeederAsync(cabinet);
        var otherFeeder = await setup.NewFeederAsync(cabinet);
        var segment = await setup.NewSegmentAsync();
        var server = "iot-" + Guid.NewGuid().ToString("N");
        using var registration = await admin.PostAsJsonAsync("/api/v1/iot/servers",
            new { server_id = server, commune_id = fixture.CommuneId,
                location = new { lat = 13.5, lng = 109.5 } });
        Assert.Equal(HttpStatusCode.Created, registration.StatusCode);

        using var created = await manager.PostAsJsonAsync("/api/v1/assets/iot-nodes",
            new { cabinet_id = cabinet, data_source = "calibration_rig", supports_remote_control = true });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var node = created.Headers.Location!.OriginalString.Split('/')[^1];
        var nodePath = $"/api/v1/iot/nodes/{node}";

        using var source = await manager.PutAsJsonAsync(nodePath, new
        {
            server_id = server, device_id = "probe", feeder_id = feeder,
            nominal_voltage = 5, nominal_current_ma = 100, nominal_power_mw = 500,
        });
        Assert.Equal(HttpStatusCode.NoContent, source.StatusCode);

        // Public electrical history belongs to the feeder, not the removable node.
        Assert.Equal(HttpStatusCode.NotFound,
            (await manager.GetAsync($"{nodePath}/electrical/history")).StatusCode);

        // Registration and diagnostics work before the feeder has any poles or a relay assignment.
        var diagnosis = await manager.GetFromJsonAsync<JsonElement>($"{nodePath}/diagnostics");
        Assert.Equal(feeder, diagnosis.GetProperty("feeder_id").GetString());
        var first = await setup.NewPoleAsync(segment, feeder);
        var second = await setup.NewPoleAsync(segment, feeder);
        var unaffected = await setup.NewPoleAsync(segment, otherFeeder);
        foreach (var pole in new[] { first, second, unaffected })
        {
            using var installed = await manager.PostAsJsonAsync("/api/v1/assets/fixtures", new
            {
                pole_id = pole, fixture_type = "led_road_lamp", power_source = "grid", lamp_watt = 60,
                install_date = "2026-10-01", data_source = "calibration_rig",
            });
            Assert.Equal(HttpStatusCode.Created, installed.StatusCode);
        }

        foreach (var pole in new[] { first, second })
        {
            var control = await manager.GetFromJsonAsync<JsonElement>($"/api/v1/iot/poles/{pole}/control");
            Assert.Equal(node, control.GetProperty("node_id").GetString());
            Assert.True(control.GetProperty("can_control_feeder").GetBoolean());
        }

        var on = await SubmitAsync("MANUAL", "ON");
        await AssertPowerAsync("unknown", null, false);
        bridge.Acknowledge("ON");
        await AssertPowerAsync("on", on, true);

        var off = await SubmitAsync("MANUAL", "OFF");
        await AssertPowerAsync("on", on, true);
        bridge.Acknowledge("OFF");
        await AssertPowerAsync("off", off, true);

        var auto = await SubmitAsync("AUTO", null, new { day_start = "07:00", night_start = "19:00" });
        bridge.Acknowledge("ON");
        await AssertPowerAsync("on", auto, true);
        bridge.ReportTelemetry("OFF", DateTimeOffset.UtcNow.AddSeconds(-10));
        await AssertPowerAsync("off", auto, true);
        bridge.AgeReports();
        await AssertPowerAsync("unknown", auto, true);

        bridge.RejectCommand = true;
        using var rejected = await manager.PostAsJsonAsync($"{nodePath}/command", new { mode = "MANUAL", command = "OFF" });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, rejected.StatusCode);
        using var removed = await manager.DeleteAsync(nodePath);
        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        using var detachedCommand = await manager.PostAsJsonAsync($"{nodePath}/command", new { mode = "MANUAL", command = "ON" });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, detachedCommand.StatusCode);
        var detachedPole = await manager.GetFromJsonAsync<JsonElement>($"/api/v1/iot/poles/{first}/control");
        Assert.Equal(JsonValueKind.Null, detachedPole.GetProperty("node_id").ValueKind);

        var history = $"{nodePath}/command/history";
        var page1 = await manager.GetFromJsonAsync<JsonElement>($"{history}?page=1&page_size=2");
        Assert.Equal(1, page1.GetProperty("page").GetInt32());
        Assert.Equal(2, page1.GetProperty("page_size").GetInt32());
        Assert.Equal(4, page1.GetProperty("total").GetInt32());
        var newest = page1.GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(2, newest.Length);
        Assert.Equal("OFF", newest[0].GetProperty("command").GetString());
        Assert.Equal(JsonValueKind.Null, newest[0].GetProperty("bridge_accepted_at").ValueKind);
        var acceptedAuto = newest[1];
        Assert.Equal(auto, acceptedAuto.GetProperty("command_id").GetString());
        Assert.Equal("AUTO", acceptedAuto.GetProperty("mode").GetString());
        Assert.Equal(JsonValueKind.Null, acceptedAuto.GetProperty("command").ValueKind);
        Assert.Equal("07:00", acceptedAuto.GetProperty("auto_schedule").GetProperty("day_start").GetString());
        Assert.Equal(feeder, acceptedAuto.GetProperty("feeder_id").GetString());
        Assert.Equal(server, acceptedAuto.GetProperty("server_id").GetString());
        var managerProfile = await manager.GetFromJsonAsync<JsonElement>("/api/v1/auth/me");
        Assert.Equal(managerProfile.GetProperty("full_name").GetString(), acceptedAuto.GetProperty("requested_by").GetString());
        Assert.Equal("pending", newest[0].GetProperty("status").GetString());
        Assert.Equal("success", acceptedAuto.GetProperty("status").GetString());
        var rejectedAt = newest[0].GetProperty("requested_at").GetDateTimeOffset();
        clock.SetUtcNow(rejectedAt.AddDays(1));
        var waiting = await manager.GetFromJsonAsync<JsonElement>($"{history}?page=1&page_size=1");
        Assert.Equal("expired", waiting.GetProperty("items")[0].GetProperty("status").GetString());
        bridge.ReceiptTimes[newest[0].GetProperty("command_id").GetString()!] = rejectedAt.AddSeconds(12.001);
        var lateAck = await manager.GetFromJsonAsync<JsonElement>($"{history}?page=1&page_size=1");
        Assert.Equal("success", lateAck.GetProperty("items")[0].GetProperty("status").GetString());
        Assert.True(acceptedAuto.GetProperty("bridge_accepted_at").GetDateTimeOffset() >=
            acceptedAuto.GetProperty("requested_at").GetDateTimeOffset());

        var page2 = await manager.GetFromJsonAsync<JsonElement>($"{history}?page=2&page_size=2");
        Assert.Equal(new[] { off, on }, page2.GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("command_id").GetString()));
        Assert.All(page2.GetProperty("items").EnumerateArray(),
            item => Assert.Equal("success", item.GetProperty("status").GetString()));
        var empty = await manager.GetFromJsonAsync<JsonElement>($"{history}?page=2147483647&page_size=200");
        Assert.Empty(empty.GetProperty("items").EnumerateArray());
        bridge.AutoTransitions = [new {
            eventId = "sensor-100", deviceId = "probe", feederId = feeder, command = "OFF",
            occurredAt = rejectedAt.AddSeconds(-0.001), receivedAt = rejectedAt.AddSeconds(1)
        }];
        var combined = await manager.GetFromJsonAsync<JsonElement>($"{history}?page=1&page_size=2");
        Assert.Equal(5, combined.GetProperty("total").GetInt32());
        var automatic = combined.GetProperty("items")[1];
        Assert.Equal("sensor", automatic.GetProperty("requested_by").GetString());
        Assert.Equal("AUTO", automatic.GetProperty("mode").GetString());
        Assert.Equal("OFF", automatic.GetProperty("command").GetString());
        Assert.Equal("success", automatic.GetProperty("status").GetString());
        var again = await manager.GetFromJsonAsync<JsonElement>($"{history}?page=1&page_size=2");
        Assert.Equal(5, again.GetProperty("total").GetInt32());
        Assert.Equal("sensor-100", again.GetProperty("items")[1].GetProperty("command_id").GetString());
        var shifted = await manager.GetFromJsonAsync<JsonElement>($"{history}?page=2&page_size=2");
        Assert.Equal(auto, shifted.GetProperty("items")[0].GetProperty("command_id").GetString());
        using var foreignToken = await fixture.ForeignCommuneClientAsync();
        using var foreign = AuthorizedClient(host.CreateClient(), foreignToken);
        Assert.Equal(HttpStatusCode.NotFound, (await foreign.GetAsync(history)).StatusCode);

        async Task<string?> SubmitAsync(string mode, string? command, object? autoSchedule = null)
        {
            using var response = await manager.PostAsJsonAsync($"{nodePath}/command", new { mode, command, auto_schedule = autoSchedule });
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
            var result = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.False(result.GetProperty("command_acknowledged").GetBoolean());
            return result.GetProperty("command_id").GetString();
        }

        async Task AssertPowerAsync(string expected, string? commandId, bool acknowledged)
        {
            var command = await manager.GetFromJsonAsync<JsonElement>($"{nodePath}/command");
            Assert.Equal(commandId, command.GetProperty("command_id").GetString());
            Assert.Equal(acknowledged, command.GetProperty("command_acknowledged").GetBoolean());
            Assert.Equal(expected, command.GetProperty("power_state").GetString());
            var electrical = await manager.GetFromJsonAsync<JsonElement>($"{nodePath}/electrical");
            Assert.Equal(expected, electrical.GetProperty("power_state").GetString());
            foreach (var (pole, power) in new[] { (first, expected), (second, expected), (unaffected, "unknown") })
            {
                var detail = await manager.GetFromJsonAsync<JsonElement>($"/api/v1/map/poles/{pole}");
                Assert.Equal(power, detail.GetProperty("power_state").GetString());
                var lamp = Assert.Single(detail.GetProperty("fixtures").EnumerateArray());
                Assert.Equal(power, lamp.GetProperty("power_state").GetString());
                Assert.Equal("unknown", detail.GetProperty("current_status").GetProperty("fixture_status").GetString());
            }
        }
    }

    [Fact]
    public Task Feeder_diagnostics_uses_five_live_samples_for_current_and_voltage_without_lamp_counts()
    {
        var bridge = new ReportedPowerBridge();
        return WithSharedCabinetAsync(async (client, rig, services) =>
        {
            var path = $"/api/v1/iot/feeders/{rig.Feeder}/diagnostics";
            using var configured = await client.PutAsJsonAsync($"/api/v1/iot/nodes/{rig.First}", new {
                server_id = rig.Server, device_id = "probe", feeder_id = rig.Feeder,
                nominal_voltage = 5, nominal_current_ma = 93.5546875, nominal_power_mw = 467.7734375,
            });
            Assert.Equal(HttpStatusCode.NoContent, configured.StatusCode);
            foreach (var (current, expected) in new[] {
                (93.5546875, false), (86.6015625, true), (77.8515625, true), (68.515625, true) })
            {
                bridge.DiagnosticDevice = DiagnosticTelemetry([current, current, current, current, current], voltage: 5);
                var result = await client.GetFromJsonAsync<JsonElement>(path);
                Assert.Equal(rig.Feeder, result.GetProperty("feeder_id").GetString());
                Assert.False(result.TryGetProperty("lamp_count", out _));
                var window = result.GetProperty("electrical_window");
                Assert.Equal("evaluated", window.GetProperty("status").GetString());
                Assert.Equal(5, window.GetProperty("samples").GetInt32());
                Assert.Equal(5, window.GetProperty("required_samples").GetInt32());
                var anomalies = result.GetProperty("anomalies").EnumerateArray().ToArray();
                if (expected) {
                    var alert = Assert.Single(anomalies);
                    Assert.Equal("current_drop", alert.GetProperty("code").GetString());
                    Assert.Equal(current, alert.GetProperty("observed").GetDouble());
                    Assert.Equal(93.5546875, alert.GetProperty("reference").GetDouble());
                    Assert.Equal(5, alert.GetProperty("limit").GetDouble());
                } else Assert.Empty(anomalies);
            }
            bridge.DiagnosticDevice = DiagnosticTelemetry([0d], voltage: 5);
            await Insufficient();
            bridge.DiagnosticDevice = DiagnosticTelemetry([0d, 0d, 0d, 0d], voltage: 5);
            await Insufficient(4);
            bridge.DiagnosticDevice = DiagnosticTelemetry([0d, 0d, 0d, 0d, 0d], voltage: 5, skipSequence: 3);
            await Insufficient(2);
            bridge.DiagnosticDevice = DiagnosticTelemetry([0d, 0d, 0d], voltage: 5, skipSequence: 2);
            await Insufficient();
            bridge.DiagnosticDevice = DiagnosticTelemetry([0d, 0d, 0d, 0d, 0d], voltage: 5, duplicatePrevious: true);
            await Insufficient();
            bridge.DiagnosticDevice = DiagnosticTelemetry([0d, 0d, 0d, 0d, 0d], voltage: 5, foreignPrevious: true);
            await Insufficient();
            bridge.DiagnosticDevice = DiagnosticTelemetry([0d, 0d, 0d, 0d, 0d], voltage: 0.04);
            await SingleAlert("voltage_loss");
            bridge.DiagnosticDevice = DiagnosticTelemetry([0d, 0d, 0d, 0d, 0d], voltage: 4.4);
            await SingleAlert("voltage_deviation");
            bridge.DiagnosticDevice = DiagnosticTelemetry([0d, 0d, 0d, 0d, 0d], voltage: 5, oldRtc: true);
            await SingleAlert("telemetry_stale");
            bridge.DiagnosticDevice = DiagnosticTelemetry([0d, 0d, 0d, 0d, 0d], voltage: 5, lampCommand: "OFF");
            var off = await client.GetFromJsonAsync<JsonElement>(path);
            Assert.Empty(off.GetProperty("anomalies").EnumerateArray());
            Assert.Equal(JsonValueKind.Null, off.GetProperty("electrical_window").ValueKind);

            using var foreignToken = await fixture.ForeignCommuneClientAsync();
            using var foreignFactory = new LuxMapApiFactory();
            using var foreign = AuthorizedClient(foreignFactory.CreateClient(), foreignToken);
            Assert.Equal(HttpStatusCode.NotFound, (await foreign.GetAsync(path)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await foreign.GetAsync(path + "/history")).StatusCode);

            async Task Insufficient(int expectedSamples = 1) {
                var payload = await client.GetFromJsonAsync<JsonElement>(path);
                Assert.Equal("insufficient_samples", payload.GetProperty("electrical_window").GetProperty("status").GetString());
                Assert.Equal(expectedSamples, payload.GetProperty("electrical_window").GetProperty("samples").GetInt32());
                Assert.Empty(payload.GetProperty("anomalies").EnumerateArray());
            }
            async Task SingleAlert(string code) {
                var payload = await client.GetFromJsonAsync<JsonElement>(path);
                Assert.Equal(code, Assert.Single(payload.GetProperty("anomalies").EnumerateArray()).GetProperty("code").GetString());
            }
        }, bridge);
    }

    [Fact]
    public Task Sensor_health_uses_the_common_anomaly_shape_with_or_without_electrical_data()
    {
        var bridge = new ReportedPowerBridge();
        return WithSharedCabinetAsync(async (client, rig, _) =>
        {
            var nodePath = $"/api/v1/iot/nodes/{rig.First}";
            using var source = await client.PutAsJsonAsync(nodePath, SourceBody(rig));
            Assert.Equal(HttpStatusCode.NoContent, source.StatusCode);
            var diagnoses = $"{nodePath}/diagnostics";
            var now = DateTimeOffset.UtcNow;
            object Report(DateTimeOffset received, params string[] codes) => new
            {
                receivedAt = received,
                data = new { deviceId = "probe", sessionId = "health", sequenceNumber = 1,
                    firmwareVersion = "sensor-health-v1", codes },
            };

            bridge.DiagnosticDevice = new { deviceId = "probe", sensorHealth = Report(now, "ina226_unavailable") };
            var failed = await client.GetFromJsonAsync<JsonElement>(diagnoses);
            Assert.Equal("anomaly", failed.GetProperty("status").GetString());
            Assert.Equal("fresh", failed.GetProperty("sensor_health").GetProperty("status").GetString());
            Assert.Equal(JsonValueKind.Null, failed.GetProperty("measurement").ValueKind);
            var error = Assert.Single(failed.GetProperty("anomalies").EnumerateArray());
            Assert.Equal("ina226_unavailable", error.GetProperty("code").GetString());
            foreach (var field in new[] { "observed", "reference", "deviation_percent", "limit" })
                Assert.Equal(JsonValueKind.Null, error.GetProperty(field).ValueKind);

            bridge.DiagnosticDevice = new
            {
                deviceId = "probe",
                latestTelemetry = new
                {
                    receivedAt = now.AddMinutes(-2),
                    data = new { lampCommand = "ON", voltage = 0.02, current = 0, power = 0 },
                },
                sensorHealth = Report(now, "ina226_unavailable", "bh1750_read_failed", "microsd_write_failed", "lora_tx_blocked"),
            };
            var combined = await client.GetFromJsonAsync<JsonElement>(diagnoses);
            Assert.Equal("anomaly", combined.GetProperty("status").GetString());
            Assert.Equal(new[] { "telemetry_stale", "ina226_unavailable", "bh1750_read_failed", "microsd_write_failed", "lora_tx_blocked" },
                combined.GetProperty("anomalies")
                .EnumerateArray().Select(item => item.GetProperty("code").GetString()).ToArray());

            foreach (var (when, expected) in new[] { (now, "fresh"), (now.AddMinutes(-2), "stale") })
            {
                bridge.DiagnosticDevice = new
                {
                    deviceId = "probe", sensorHealth = expected == "fresh" ? Report(when) : Report(when, "ina226_unavailable"),
                };
                var result = await client.GetFromJsonAsync<JsonElement>(diagnoses);
                Assert.Equal("no_data", result.GetProperty("status").GetString());
                Assert.Empty(result.GetProperty("anomalies").EnumerateArray());
                Assert.Equal(expected, result.GetProperty("sensor_health").GetProperty("status").GetString());
            }
        }, bridge);
    }

    private static object DiagnosticTelemetry(double[] currents, double voltage = 5, string lampCommand = "ON",
        int? skipSequence = null, bool oldRtc = false, bool staleDelivery = false,
        bool duplicatePrevious = false, bool foreignPrevious = false)
    {
        var now = DateTimeOffset.UtcNow;
        var measured = now.AddSeconds(oldRtc ? -120 : -5);
        var received = now.AddSeconds(staleDelivery ? -120 : -1);
        var rows = currents.Select((current, index) =>
        {
            var offset = (currents.Length - 1 - index) * 10;
            return new
            {
                receivedAt = index == currents.Length - 1 ? received : received.AddSeconds(-offset),
                data = new { lampCommand, voltage, current, power = voltage * current, sessionId = foreignPrevious && index < currents.Length - 1 ? "foreign" : "calibration",
                    sequenceNumber = index + 1, timestamp = measured.AddSeconds(-offset).ToOffset(TimeSpan.FromHours(7))
                        .ToString("yyyy-MM-ddTHH:mm:ss", System.Globalization.CultureInfo.InvariantCulture) },
            };
        }).ToArray();
        return new
        {
            deviceId = "probe", latestTelemetry = rows[^1],
            history = rows.Concat(duplicatePrevious && rows.Length > 1 ? [rows[^2]] : []).Where(row => row.data.sequenceNumber != skipSequence)
                .OrderBy(row => row.data.sequenceNumber % 2).ToArray(),
        };
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public Task A_source_and_a_relay_cannot_assign_different_nodes_to_the_same_feeder(bool relayFirst)
        => WithSharedCabinetAsync(async (client, rig, _) =>
        {
            HttpResponseMessage rejected;
            if (relayFirst)
            {
                Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync(
                    $"/api/v1/assets/iot-nodes/{rig.Second}/relays/1", new { feeder_id = rig.Feeder })).StatusCode);
                rejected = await client.PutAsJsonAsync($"/api/v1/iot/nodes/{rig.First}", SourceBody(rig));
            }
            else
            {
                Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync(
                $"/api/v1/iot/nodes/{rig.First}", SourceBody(rig))).StatusCode);
                rejected = await client.PutAsJsonAsync($"/api/v1/assets/iot-nodes/{rig.Second}/relays/1", new { feeder_id = rig.Feeder });
            }
            await AssertBindingConflictAsync(rejected, rig.Feeder);
        });

    [Fact]
    public Task Matching_sources_and_relays_keep_two_nodes_in_one_cabinet_independent()
        => WithSharedCabinetAsync(async (client, rig, services) =>
        {
            Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync(
                $"/api/v1/iot/nodes/{rig.First}", SourceBody(rig))).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync(
                $"/api/v1/iot/nodes/{rig.Second}", SourceBody(rig, rig.OtherFeeder, "probe-2"))).StatusCode);
            foreach (var (node, feeder) in new[] { (rig.First, rig.Feeder), (rig.Second, rig.OtherFeeder) })
            {
                Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync(
                    $"/api/v1/assets/iot-nodes/{node}/relays/1", new { feeder_id = feeder })).StatusCode);
                var (result, replayed) = await CreateLightingAsync(feeder, services);
                Assert.False(replayed);
                Assert.Equal(node, Assert.Single(result.Commands).NodeId);
            }
            var cabinet = await client.GetFromJsonAsync<JsonElement>($"/api/v1/assets/cabinets/{rig.Cabinet}");
            Assert.Equal(new[] { rig.First, rig.Second }, cabinet.GetProperty("cabinet").GetProperty("iot_node_ids")
                .EnumerateArray().Select(id => id.GetString()).ToArray());
            var control = await client.GetFromJsonAsync<JsonElement>($"/api/v1/iot/poles/{rig.Pole}/control");
            Assert.Equal(rig.First, control.GetProperty("node_id").GetString());

            using var detached = await client.DeleteAsync($"/api/v1/iot/nodes/{rig.First}");
            Assert.Equal(HttpStatusCode.NoContent, detached.StatusCode);
            var updatedCabinet = await client.GetFromJsonAsync<JsonElement>($"/api/v1/assets/cabinets/{rig.Cabinet}");
            Assert.Equal(rig.Second, Assert.Single(updatedCabinet.GetProperty("cabinet").GetProperty("iot_node_ids")
                .EnumerateArray()).GetString());
            using var freedFeeder = await client.PutAsJsonAsync($"/api/v1/assets/iot-nodes/{rig.Second}/relays/2",
                new { feeder_id = rig.Feeder });
            Assert.Equal(HttpStatusCode.NoContent, freedFeeder.StatusCode);
            using var claimed = await client.PutAsJsonAsync($"/api/v1/iot/nodes/{rig.Second}", SourceBody(rig, device: "probe-2"));
            Assert.Equal(HttpStatusCode.NoContent, claimed.StatusCode);
            Assert.Equal(LuxMap.Modules.IOT.Lighting.LightingCommandStatus.Superseded,
                await fixture.QueryAsync(db => db.Set<LuxMap.Modules.IOT.Lighting.LightingCommand>()
                    .IgnoreQueryFilters().AsNoTracking().Where(command => command.FeederId == rig.Feeder)
                    .Select(command => command.Status).SingleAsync()));
        });

    [Fact]
    public Task Configured_and_database_sources_for_one_feeder_return_a_clear_conflict_instead_of_500()
        => WithSharedCabinetAsync(async (client, rig, services) =>
        {
            Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync(
                $"/api/v1/iot/nodes/{rig.First}", SourceBody(rig))).StatusCode);
            services.GetRequiredService<Microsoft.Extensions.Options.IOptions<IotOptions>>().Value.Nodes[rig.Second] = new NodeProfile
            {
                ServerId = rig.Server, DeviceId = "configured-probe", FeederId = rig.Feeder,
                NominalVoltage = 5, NominalCurrentMa = 100, NominalPowerMw = 500,
            };

            await AssertBindingConflictAsync(await client.GetAsync("/api/v1/assets/feeders?page_size=200"), rig.Feeder);
            await AssertBindingConflictAsync(await client.GetAsync($"/api/v1/map/poles/{rig.Pole}"), rig.Feeder);
            await AssertBindingConflictAsync(await client.GetAsync($"/api/v1/iot/poles/{rig.Pole}/control"), rig.Feeder);
            await AssertBindingConflictAsync(await client.PostAsJsonAsync($"/api/v1/iot/nodes/{rig.First}/command",
                new { mode = "MANUAL", command = "OFF" }), rig.Feeder);
            await AssertLightingBindingConflictAsync(rig.Feeder, services);

            using var foreignToken = await fixture.ForeignCommuneClientAsync();
            using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/assets/feeders?page_size=200");
            request.Headers.Authorization = foreignToken.DefaultRequestHeaders.Authorization;
            using var foreignRead = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, foreignRead.StatusCode);
            Assert.DoesNotContain((await foreignRead.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("items").EnumerateArray(),
                item => item.GetProperty("feeder_id").GetString() == rig.Feeder);
        });

    [Fact]
    public Task An_existing_cross_channel_conflict_blocks_reads_commands_and_MQTT_delivery()
        => WithSharedCabinetAsync(async (client, rig, services) =>
        {
            Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync(
                $"/api/v1/iot/nodes/{rig.First}", SourceBody(rig))).StatusCode);
            // Simulate a conflicting mapping imported before the guard existed.
            await fixture.QueryAsync(db => db.Database.ExecuteSqlAsync(
                $"INSERT INTO feeder_control (feeder_id,node_id,commune_id,cabinet_id,relay_no) VALUES ({rig.Feeder},{rig.Second},{fixture.CommuneId},{rig.Cabinet},1)"));
            try
            {
                await AssertBindingConflictAsync(await client.GetAsync("/api/v1/assets/feeders?page_size=200"), rig.Feeder);
                await AssertBindingConflictAsync(await client.PostAsJsonAsync($"/api/v1/iot/nodes/{rig.First}/command",
                    new { mode = "MANUAL", command = "OFF" }), rig.Feeder);
                await AssertLightingBindingConflictAsync(rig.Feeder, services);

                await using var scope = services.GetRequiredService<LuxMap.Modules.IOT.Mqtt.LightingScopeFactory>()
                    .Open(fixture.CommuneId, "binding-guard-test");
                var error = await Assert.ThrowsAsync<LuxMap.Shared.Http.LuxMapException>(
                    () => scope.Service.PollAsync(rig.Second, CancellationToken.None));
                Assert.Equal("IOT_REGISTRATION_CONFLICT", error.Code);
            }
            finally
            {
                await fixture.QueryAsync(db => db.Database.ExecuteSqlAsync($"DELETE FROM feeder_control WHERE feeder_id = {rig.Feeder}"));
            }
        });

    [Fact]
    public Task Concurrent_bridge_sources_cannot_take_the_same_feeder()
        => WithSharedCabinetAsync(async (client, rig, _) =>
        {
            var responses = await Task.WhenAll(
                client.PutAsJsonAsync($"/api/v1/iot/nodes/{rig.First}", SourceBody(rig)),
                client.PutAsJsonAsync($"/api/v1/iot/nodes/{rig.Second}", SourceBody(rig, device: "probe-2")));
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.NoContent);
            var rejected = Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);
            Assert.Equal("IOT_REGISTRATION_CONFLICT", (await rejected.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("error").GetProperty("code").GetString());
        });

    [Fact]
    public Task Concurrent_source_and_relay_writes_wait_for_the_feeder_and_leave_one_controller()
        => WithSharedCabinetAsync(async (client, rig, services) =>
        {
            await using var scope = services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<LuxMap.Persistence.LuxMapDbContext>();
            await using var transaction = await db.Database.BeginTransactionAsync();
            await db.Database.ExecuteSqlAsync($"SELECT 1 FROM feeder WHERE feeder_id = {rig.Feeder} FOR NO KEY UPDATE");
            var source = client.PutAsJsonAsync($"/api/v1/iot/nodes/{rig.First}", SourceBody(rig));
            var relay = client.PutAsJsonAsync($"/api/v1/assets/iot-nodes/{rig.Second}/relays/1", new { feeder_id = rig.Feeder });
            try
            {
                await Task.Delay(250);
                Assert.False(source.IsCompleted, "The source write bypassed the feeder lock.");
                Assert.False(relay.IsCompleted, "The relay write bypassed the feeder lock.");
            }
            finally
            {
                await transaction.RollbackAsync();
            }
            var responses = await Task.WhenAll(source, relay);
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.NoContent);
            await AssertBindingConflictAsync(Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict), rig.Feeder);
        });

    [Fact]
    public Task Feeder_electrical_history_survives_detach_and_combines_replacement_nodes()
    {
        var bridge = new ReportedPowerBridge();
        return WithSharedCabinetAsync(async (client, rig, _) =>
        {
            var now = DateTimeOffset.UtcNow;
            var assigned = now.AddMinutes(-3);
            var detachedAt = now.AddMinutes(-2);
            var replacementAt = now.AddMinutes(-1);
            var before = now.AddSeconds(-150);
            var after = now.AddSeconds(-5);
            var samples = new[]
            {
                (Device: "probe", Id: 1, Measured: before, Received: now.AddSeconds(-1)),
                (Device: "probe", Id: 2, Measured: detachedAt.AddSeconds(5), Received: now),
                (Device: "probe-2", Id: 3, Measured: after, Received: now.AddSeconds(-4)),
            };
            bridge.FeederHistory = async (request, ct) =>
            {
                var windows = await request.Content!.ReadFromJsonAsync<JsonElement>(ct);
                var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(request.RequestUri!.Query);
                var sortBy = query.GetValueOrDefault("sortBy").ToString();
                var skip = int.Parse(query["skip"].ToString(), System.Globalization.CultureInfo.InvariantCulture);
                var take = int.Parse(query["take"].ToString(), System.Globalization.CultureInfo.InvariantCulture);
                var selected = (
                    from source in windows.EnumerateArray()
                    let deviceId = source.GetProperty("deviceId").GetString()
                    let fromTime = source.GetProperty("from").GetDateTimeOffset()
                    let until = source.GetProperty("until").ValueKind == JsonValueKind.Null
                        ? (DateTimeOffset?)null : source.GetProperty("until").GetDateTimeOffset()
                    from sample in samples
                    where sample.Device == deviceId && sample.Measured >= fromTime &&
                        (until is null || sample.Measured < until)
                    orderby (sortBy == "measured_at" ? sample.Measured : sample.Received) descending
                    select new
                    {
                        cursor = sample.Id,
                        nodeId = source.GetProperty("nodeId").GetString(),
                        serverId = source.GetProperty("serverId").GetString(),
                        active = until is null,
                        reading = new
                        {
                            receivedAt = sample.Received, gatewayId = "TEST-GW",
                            data = new
                            {
                                voltage = 4.07, current = 50, power = 203.5, lampCommand = "ON",
                                timestamp = sample.Measured.ToOffset(TimeSpan.FromHours(7)).ToString(
                                    "yyyy-MM-ddTHH:mm:ss", System.Globalization.CultureInfo.InvariantCulture),
                            },
                        },
                    }).ToArray();
                if (request.RequestUri.AbsolutePath.EndsWith("/averages", StringComparison.Ordinal))
                {
                    var basis = query["timeBasis"].ToString();
                    DateTimeOffset? from = DateTimeOffset.TryParse(query.GetValueOrDefault("from"), out var lower) ? lower : null;
                    DateTimeOffset? to = DateTimeOffset.TryParse(query.GetValueOrDefault("to"), out var upper) ? upper : null;
                    var buckets = selected.Select(sample => new
                    {
                        Sample = sample,
                        At = basis == "received_at" ? sample.reading.receivedAt
                            : new DateTimeOffset(DateTime.ParseExact(sample.reading.data.timestamp,
                                "yyyy-MM-ddTHH:mm:ss", System.Globalization.CultureInfo.InvariantCulture), TimeSpan.FromHours(7)),
                    }).Where(sample => (from is null || sample.At >= from) && (to is null || sample.At < to))
                        .GroupBy(sample => sample.At.ToUnixTimeSeconds() / 30 * 30)
                        .OrderByDescending(group => group.Key)
                        .Select(group => new
                        {
                            period = group.Key, sampleCount = group.LongCount(),
                            voltageAvg = group.Average(item => item.Sample.reading.data.voltage),
                            currentMaAvg = group.Average(item => item.Sample.reading.data.current),
                            powerMwAvg = group.Average(item => item.Sample.reading.data.power),
                        }).ToArray();
                    return new(HttpStatusCode.OK)
                    {
                        Content = JsonContent.Create(new
                        {
                            total = buckets.Length, periods = buckets.Select(bucket => bucket.period),
                            items = query["keysOnly"] == "True" ? buckets.Take(0) : buckets.Skip(skip).Take(take),
                        }),
                    };
                }
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new { total = selected.Length, items = selected.Skip(skip).Take(take) }),
                };
            };

            using var attach = await client.PutAsJsonAsync($"/api/v1/iot/nodes/{rig.First}", SourceBody(rig));
            Assert.Equal(HttpStatusCode.NoContent, attach.StatusCode);
            using var detach = await client.DeleteAsync($"/api/v1/iot/nodes/{rig.First}");
            Assert.Equal(HttpStatusCode.NoContent, detach.StatusCode);
            await fixture.QueryAsync(db => db.Database.ExecuteSqlAsync($"""
                UPDATE iot_registration_audit
                SET created_at = CASE WHEN details ->> 'action' = 'node_detached' THEN {detachedAt} ELSE {assigned} END
                WHERE node_id = {rig.First}
                """));

            var feeder = $"/api/v1/iot/feeders/{rig.Feeder}/electrical";
            var latest = await client.GetFromJsonAsync<JsonElement>(feeder);
            Assert.Equal(rig.Feeder, latest.GetProperty("feeder_id").GetString());
            Assert.Equal(rig.First, latest.GetProperty("node_id").GetString());
            Assert.Equal("offline", latest.GetProperty("status").GetString());
            Assert.Equal(4.07, latest.GetProperty("measurement").GetProperty("voltage").GetDouble());

            using var replacement = await client.PutAsJsonAsync($"/api/v1/iot/nodes/{rig.Second}", SourceBody(rig, device: "probe-2"));
            Assert.Equal(HttpStatusCode.NoContent, replacement.StatusCode);
            await fixture.QueryAsync(db => db.Database.ExecuteSqlAsync($"""
                UPDATE iot_registration_audit SET created_at = {replacementAt} WHERE node_id = {rig.Second}
                """));

            using var debugMeasured = await client.GetAsync($"{feeder}/history?page=1&page_size=1");
            Assert.True(debugMeasured.IsSuccessStatusCode, await debugMeasured.Content.ReadAsStringAsync());
            var measured = await debugMeasured.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("measured_at", measured.GetProperty("time_basis").GetString());
            Assert.Equal(30, measured.GetProperty("interval_seconds").GetInt32());
            Assert.Equal(2, measured.GetProperty("total").GetInt32());
            Assert.Equal(1, measured.GetProperty("items")[0].GetProperty("sample_count").GetInt64());
            var next = await client.GetFromJsonAsync<JsonElement>($"{feeder}/history?page=2&page_size=1");
            Assert.True(next.GetProperty("items")[0].GetProperty("period_start").GetDateTime() <
                measured.GetProperty("items")[0].GetProperty("period_start").GetDateTime());
            var received = await client.GetFromJsonAsync<JsonElement>($"{feeder}/history/received?page_size=1");
            Assert.Equal("received_at", received.GetProperty("time_basis").GetString());
            Assert.Equal(2, received.GetProperty("items").EnumerateArray().Sum(item => item.GetProperty("sample_count").GetInt64()) +
                (received.GetProperty("total").GetInt32() > 1 ? 1 : 0));
            var from = Uri.EscapeDataString(after.AddSeconds(-1).ToString("O"));
            var to = Uri.EscapeDataString(after.AddSeconds(1).ToString("O"));
            var filtered = await client.GetFromJsonAsync<JsonElement>($"{feeder}/history?from={from}&to={to}");
            Assert.Equal(1, filtered.GetProperty("total").GetInt32());
            var reversed = await client.GetAsync($"{feeder}/history?from={to}&to={from}");
            Assert.Equal(HttpStatusCode.BadRequest, reversed.StatusCode);
            Assert.Equal("VALIDATION_FAILED", (await reversed.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("error").GetProperty("code").GetString());
            Assert.Equal(HttpStatusCode.BadRequest,
                (await client.GetAsync($"{feeder}/history/received?from=18/11/2026")).StatusCode);
            var empty = await client.GetFromJsonAsync<JsonElement>($"{feeder}/history?page=999");
            Assert.Empty(empty.GetProperty("items").EnumerateArray());
            Assert.Equal(2, empty.GetProperty("total").GetInt32());
            var newLatest = await client.GetFromJsonAsync<JsonElement>(feeder);
            Assert.Equal("online", newLatest.GetProperty("status").GetString());

            using var foreignToken = await fixture.ForeignCommuneClientAsync();
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{feeder}/history");
            request.Headers.Authorization = foreignToken.DefaultRequestHeaders.Authorization;
            using var foreign = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        }, bridge);
    }

    [Fact]
    public Task Feeder_average_history_merges_overlapping_bridge_buckets_by_sample_count()
    {
        var bridge = new ReportedPowerBridge();
        return WithSharedCabinetAsync(async (client, rig, services) =>
        {
            using var admin = await fixture.SeededClientAsync("admin", "SEED_ADMIN_PASSWORD");
            var alternate = "iot-" + Guid.NewGuid().ToString("N");
            using var register = new HttpRequestMessage(HttpMethod.Post, "/api/v1/iot/servers")
            {
                Content = JsonContent.Create(new
                {
                    server_id = alternate,
                    commune_id = fixture.CommuneId, location = new { lat = 13.5, lng = 109.5 },
                }),
            };
            register.Headers.Authorization = admin.DefaultRequestHeaders.Authorization;
            var bridgeDefaults = services.GetRequiredService<Microsoft.Extensions.Options.IOptions<IotOptions>>().Value;
            var originalOrigin = bridgeDefaults.BaseUrl;
            bridgeDefaults.BaseUrl = "https://alternate.example.test";
            try { Assert.Equal(HttpStatusCode.Created, (await client.SendAsync(register)).StatusCode); }
            finally { bridgeDefaults.BaseUrl = originalOrigin; }
            Assert.Equal(HttpStatusCode.NoContent,
                (await client.PutAsJsonAsync($"/api/v1/iot/nodes/{rig.First}", SourceBody(rig))).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent,
                (await client.DeleteAsync($"/api/v1/iot/nodes/{rig.First}")).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent,
                (await client.PutAsJsonAsync($"/api/v1/iot/nodes/{rig.Second}", new
                {
                    server_id = alternate, device_id = "probe", feeder_id = rig.Feeder,
                    nominal_voltage = 5, nominal_current_ma = 100, nominal_power_mw = 500,
                })).StatusCode);

            var period = DateTimeOffset.UtcNow.AddMinutes(-5).ToUnixTimeSeconds() / 30 * 30;
            bridge.FeederHistory = (request, _) =>
            {
                var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(request.RequestUri!.Query);
                var second = request.RequestUri.Host == "alternate.example.test";
                var all = new[]
                {
                    new { period = period + (second ? 30 : -30), sampleCount = 1L, voltageAvg = 5.0,
                        currentMaAvg = 5.0, powerMwAvg = 25.0 },
                    new { period, sampleCount = second ? 3L : 1L, voltageAvg = 5.0,
                        currentMaAvg = second ? 30.0 : 10.0, powerMwAvg = second ? 150.0 : 50.0 },
                };
                var lower = DateTimeOffset.TryParse(query.GetValueOrDefault("from"), out var from) ? from.ToUnixTimeSeconds() : long.MinValue;
                var upper = DateTimeOffset.TryParse(query.GetValueOrDefault("to"), out var to) ? to.ToUnixTimeSeconds() : long.MaxValue;
                var selected = all.Where(row => row.period >= lower && row.period < upper)
                    .OrderByDescending(row => row.period).ToArray();
                var skip = int.Parse(query["skip"].ToString(), System.Globalization.CultureInfo.InvariantCulture);
                var take = int.Parse(query["take"].ToString(), System.Globalization.CultureInfo.InvariantCulture);
                var keysOnly = bool.TryParse(query["keysOnly"], out var keys) && keys;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new
                    {
                        total = selected.Length, periods = selected.Select(row => row.period),
                        items = keysOnly ? selected.Take(0) : selected.Skip(skip).Take(take),
                    }),
                });
            };
            var url = $"/api/v1/iot/feeders/{rig.Feeder}/electrical/history";
            using var debugPage = await client.GetAsync($"{url}?page=2&page_size=1");
            Assert.True(debugPage.IsSuccessStatusCode, await debugPage.Content.ReadAsStringAsync());
            var page = await debugPage.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(3, page.GetProperty("total").GetInt32()); // Union, not sum of Bridge counts.
            var item = Assert.Single(page.GetProperty("items").EnumerateArray());
            Assert.Equal(4, item.GetProperty("sample_count").GetInt64());
            Assert.Equal(25, item.GetProperty("current_ma_avg").GetDouble());
            Assert.Equal(125, item.GetProperty("power_mw_avg").GetDouble());
            Assert.True(item.GetProperty("is_complete").GetBoolean());
            var defaults = await client.GetFromJsonAsync<JsonElement>(url);
            Assert.Equal(20, defaults.GetProperty("page_size").GetInt32());
            bridge.FeederHistory = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { total = 1, periods = new[] { period }, items = new object?[] { null } }),
            });
            var invalid = await client.GetAsync(url);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, invalid.StatusCode);
            Assert.Equal("IOT_SOURCE_UNAVAILABLE", (await invalid.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("error").GetProperty("code").GetString());
        }, bridge);
    }

    [Fact]
    public Task Late_execution_receipt_recovers_expired_history_and_audits_once()
    {
        var bridge = new ReportedPowerBridge();
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        return WithSharedCabinetAsync(async (client, rig, _) =>
        {
            Assert.Equal(HttpStatusCode.NoContent,
                (await client.PutAsJsonAsync($"/api/v1/iot/nodes/{rig.First}", SourceBody(rig))).StatusCode);
            var path = $"/api/v1/iot/nodes/{rig.First}/command";
            using var sent = await client.PostAsJsonAsync(path, new { mode = "MANUAL", command = "ON" });
            Assert.Equal(HttpStatusCode.Accepted, sent.StatusCode);
            var pending = await sent.Content.ReadFromJsonAsync<JsonElement>();
            var id = pending.GetProperty("command_id").GetString()!;
            var expires = pending.GetProperty("expires_at").GetDateTimeOffset();
            Assert.Equal("no_data", (await client.GetFromJsonAsync<JsonElement>(path)).GetProperty("status").GetString());
            time.SetUtcNow(expires.AddSeconds(1));
            var expired = await client.GetFromJsonAsync<JsonElement>($"{path}/history");
            Assert.Equal("expired", expired.GetProperty("items")[0].GetProperty("status").GetString());
            bridge.ReceiptTimes[id] = time.GetUtcNow();
            bridge.ExecutionTimes[id] = expires.AddSeconds(-2).ToUnixTimeSeconds();
            bridge.ExpiryTimes[id] = expires.ToUnixTimeSeconds();
            var success = await client.GetFromJsonAsync<JsonElement>($"{path}/history");
            var item = success.GetProperty("items")[0];
            Assert.Equal(1, success.GetProperty("total").GetInt32());
            Assert.Equal(id, item.GetProperty("command_id").GetString());
            Assert.Equal("success", item.GetProperty("status").GetString());
            Assert.True(item.GetProperty("late_ack").GetBoolean());
            Assert.Equal(expires.AddSeconds(-2), item.GetProperty("executed_at").GetDateTimeOffset());
            await client.GetFromJsonAsync<JsonElement>($"{path}/history");
            Assert.Equal(1, await fixture.QueryAsync(db => db.Database.SqlQuery<int>($"""
                SELECT count(*)::int AS "Value" FROM iot_registration_audit
                WHERE node_id = {rig.First} AND details ->> 'command_id' = {id}
                    AND details ->> 'event_type' = 'command_late_ack'
                """).SingleAsync()));
        }, bridge, time);
    }

    [Fact]
    public Task Node_contact_and_electrical_freshness_are_independent_and_ack_expires()
    {
        var bridge = new ReportedPowerBridge();
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        return WithSharedCabinetAsync(async (client, rig, _) =>
        {
            Assert.Equal(HttpStatusCode.NoContent,
                (await client.PutAsJsonAsync($"/api/v1/iot/nodes/{rig.First}", SourceBody(rig))).StatusCode);
            var now = time.GetUtcNow();
            var old = now.AddMinutes(-5);
            object Reading(DateTimeOffset received, DateTimeOffset measured) => new
            {
                receivedAt = received,
                data = new
                {
                    timestamp = measured.ToOffset(TimeSpan.FromHours(7)).ToString("yyyy-MM-ddTHH:mm:ss"),
                    voltage = 5, current = 100, power = 500, lampCommand = "ON",
                    sessionId = "session", sequenceNumber = 1,
                },
            };
            object Ack(DateTimeOffset at) => new
            {
                receivedAt = at, data = new { lampCommand = "OFF", commandId = Guid.NewGuid().ToString("N") },
            };
            async Task<JsonElement> Node()
            {
                for (var page = 1; ; page++)
                {
                    var result = await client.GetFromJsonAsync<JsonElement>($"/api/v1/iot/nodes?page={page}&page_size=200");
                    foreach (var item in result.GetProperty("items").EnumerateArray())
                        if (item.GetProperty("node_id").GetString() == rig.First) return item;
                    Assert.True(page * 200 < result.GetProperty("total").GetInt32(), "Registered node missing from its commune.");
                }
            }
            var url = $"/api/v1/iot/nodes/{rig.First}";
            // Legacy Bridge has no lastSeenAt field: a fresh ACK still proves contact.
            bridge.DiagnosticDevice = new { deviceId = "probe", latestTelemetry = Reading(old, old), lastCommandAck = Ack(now) };
            var node = await Node();
            Assert.Equal("online", node.GetProperty("status").GetString());
            Assert.Equal("stale", node.GetProperty("electrical_status").GetString());
            Assert.Equal(now.UtcDateTime, node.GetProperty("last_seen_at").GetDateTime());
            Assert.Equal(now.UtcDateTime, node.GetProperty("last_command_ack_at").GetDateTime());
            Assert.Equal(old.UtcDateTime, node.GetProperty("last_received_at").GetDateTime());
            var electrical = await client.GetFromJsonAsync<JsonElement>($"{url}/electrical");
            Assert.Equal("online", electrical.GetProperty("status").GetString());
            Assert.Equal("stale", electrical.GetProperty("electrical_status").GetString());
            Assert.Equal("off", electrical.GetProperty("power_state").GetString());
            var diagnosis = await client.GetFromJsonAsync<JsonElement>($"{url}/diagnostics");
            Assert.Equal("online", diagnosis.GetProperty("connection_status").GetString());
            Assert.Equal("stale", diagnosis.GetProperty("electrical_status").GetString());
            Assert.Contains(diagnosis.GetProperty("anomalies").EnumerateArray(), item => item.GetProperty("code").GetString() == "telemetry_stale");

            time.Advance(TimeSpan.FromSeconds(60));
            Assert.Equal("online", (await Node()).GetProperty("status").GetString());
            time.Advance(TimeSpan.FromSeconds(1));
            Assert.Equal("offline", (await Node()).GetProperty("status").GetString());

            now = time.GetUtcNow();
            bridge.DiagnosticDevice = new { deviceId = "probe", lastCommandAck = Ack(now) };
            Assert.Equal("online", (await Node()).GetProperty("status").GetString());
            Assert.Equal("no_data", (await Node()).GetProperty("electrical_status").GetString());
            electrical = await client.GetFromJsonAsync<JsonElement>($"{url}/electrical");
            Assert.Equal(JsonValueKind.Null, electrical.GetProperty("measurement").ValueKind);

            // A fresh packet carrying an old RTC sample proves contact, not fresh electricity.
            bridge.DiagnosticDevice = new { deviceId = "probe", latestTelemetry = Reading(now, old) };
            Assert.Equal("online", (await Node()).GetProperty("status").GetString());
            Assert.Equal("stale", (await Node()).GetProperty("electrical_status").GetString());
            bridge.DiagnosticDevice = new { deviceId = "probe", latestTelemetry = Reading(now, now) };
            Assert.Equal("fresh", (await Node()).GetProperty("electrical_status").GetString());

            // New Bridge records contact while replay leaves latestTelemetry untouched.
            bridge.DiagnosticDevice = new { deviceId = "probe", latestTelemetry = Reading(old, old), lastSeenAt = now };
            Assert.Equal("online", (await Node()).GetProperty("status").GetString());
            Assert.Equal("stale", (await Node()).GetProperty("electrical_status").GetString());

            bridge.DiagnosticDevice = new
            {
                deviceId = "probe", sensorHealth = new
                {
                    receivedAt = now,
                    data = new { deviceId = "probe", sessionId = "session", sequenceNumber = 1,
                        firmwareVersion = "firmware-v1", codes = new[] { "ina226_unavailable" } },
                },
            };
            Assert.Equal("online", (await Node()).GetProperty("status").GetString());
            Assert.Equal("no_data", (await Node()).GetProperty("electrical_status").GetString());

            bridge.DiagnosticDevice = new { deviceId = "probe", lastSeenAt = now.AddHours(1), lastCommandAck = Ack(now.AddHours(1)) };
            Assert.Equal("no_data", (await Node()).GetProperty("status").GetString());
            bridge.DiagnosticDevice = new { deviceId = "probe" };
            Assert.Equal("no_data", (await Node()).GetProperty("status").GetString());
        }, bridge, time);
    }

    [Fact]
    public Task Auto_types_round_trip_through_commands_and_history_and_reject_conflicting_inputs()
    {
        var bridge = new ReportedPowerBridge();
        return WithSharedCabinetAsync(async (client, rig, _) =>
        {
            Assert.Equal(HttpStatusCode.NoContent,
                (await client.PutAsJsonAsync($"/api/v1/iot/nodes/{rig.First}", SourceBody(rig))).StatusCode);
            var path = $"/api/v1/iot/nodes/{rig.First}/command";
            foreach (var type in new string?[] { "full_time", "scheduled", null })
            {
                using var sent = await client.PostAsJsonAsync(path, new { mode = "AUTO", auto_type = type,
                    auto_schedule = type == "full_time" ? null : new { day_start = "06:00", night_start = "18:00" } });
                Assert.Equal(HttpStatusCode.Accepted, sent.StatusCode);
                var result = await sent.Content.ReadFromJsonAsync<JsonElement>();
                Assert.Equal(type ?? "scheduled", result.GetProperty("auto_type").GetString());
                bridge.Acknowledge("ON");
                var current = await client.GetFromJsonAsync<JsonElement>(path);
                Assert.Equal(type ?? "scheduled", current.GetProperty("auto_type").GetString());
                Assert.True(current.GetProperty("command_acknowledged").GetBoolean());
                var history = await client.GetFromJsonAsync<JsonElement>($"{path}/history");
                Assert.Equal(type ?? "scheduled", history.GetProperty("items")[0].GetProperty("auto_type").GetString());
            }
            foreach (var body in new object[] {
                new { mode = "AUTO", auto_type = "unknown" },
                new { mode = "MANUAL", command = "ON", auto_type = "scheduled" },
                new { mode = "AUTO", auto_type = "full_time",
                    auto_schedule = new { day_start = "06:00", night_start = "18:00" } },
                new { mode = "AUTO", auto_type = "scheduled",
                    auto_schedule = new { day_start = "06:00", night_start = "06:00" } }
            })
            {
                using var rejected = await client.PostAsJsonAsync(path, body);
                Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
                Assert.Equal("VALIDATION_FAILED", (await rejected.Content.ReadFromJsonAsync<JsonElement>())
                    .GetProperty("error").GetProperty("code").GetString());
            }
            bridge.AutoTransitions = [new {
                eventId = "sensor-42", deviceId = "probe", feederId = rig.Feeder, command = "OFF",
                occurredAt = DateTimeOffset.UtcNow, receivedAt = DateTimeOffset.UtcNow, autoType = "full_time"
            }];
            var withSensor = await client.GetFromJsonAsync<JsonElement>($"{path}/history");
            var sensor = withSensor.GetProperty("items")[0];
            Assert.Equal("sensor", sensor.GetProperty("requested_by").GetString());
            Assert.Equal("full_time", sensor.GetProperty("auto_type").GetString());
        }, bridge);
    }

    private sealed record SharedCabinet(string Cabinet, string Feeder, string OtherFeeder, string Pole, string First, string Second, string Server);

    private static object SourceBody(SharedCabinet rig, string? feeder = null, string device = "probe") => new
    {
        server_id = rig.Server, device_id = device, feeder_id = feeder ?? rig.Feeder,
        nominal_voltage = 5, nominal_current_ma = 100, nominal_power_mw = 500,
    };

    private static async Task AssertBindingConflictAsync(HttpResponseMessage response, string feeder)
    {
        using (response)
        {
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            var error = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error");
            Assert.Equal("IOT_REGISTRATION_CONFLICT", error.GetProperty("code").GetString());
            Assert.Equal(feeder, error.GetProperty("details").GetProperty("feeder_id").GetString());
        }
    }

    private Task<(LuxMap.Modules.IOT.Lighting.LightingRequestResult Result, bool Replayed)> CreateLightingAsync(
        string feeder, IServiceProvider services)
        => new LightingTestRig(fixture).CreateAsync(new LuxMap.Modules.IOT.Lighting.CreateLightingRequest
        {
            FeederId = feeder, Mode = LuxMap.Shared.Contracts.Enums.FeederControlMode.Off, ClientOpId = Guid.NewGuid(),
        }, services);

    private async Task AssertLightingBindingConflictAsync(string feeder, IServiceProvider services)
    {
        var error = await Assert.ThrowsAsync<LuxMap.Shared.Http.LuxMapException>(() => CreateLightingAsync(feeder, services));
        Assert.Equal(HttpStatusCode.Conflict, error.StatusCode);
        Assert.Equal("IOT_REGISTRATION_CONFLICT", error.Code);
        Assert.Equal(feeder, error.Details["feeder_id"]);
    }

    private async Task WithSharedCabinetAsync(
        Func<HttpClient, SharedCabinet, IServiceProvider, Task> check, ReportedPowerBridge? bridge = null, TimeProvider? clock = null)
    {
        var origin = "https://isolated-" + Guid.NewGuid().ToString("N") + ".example.test";
        using var factory = new LuxMapApiFactory();
        using var host = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("IotDiagnostics:BaseUrl", origin);
            builder.UseSetting("IotDiagnostics:AllowedOrigins:0", origin);
            builder.UseSetting("IotDiagnostics:AllowedOrigins:1", "https://alternate.example.test");
            builder.UseSetting("Mqtt:ProvisioningKey", new string('a', 32));
            builder.ConfigureServices(services =>
            {
                if (clock is not null) services.AddSingleton<TimeProvider>(clock);
                services.AddHttpClient<IotDiagnosticsService>()
                    .ConfigurePrimaryHttpMessageHandler(() => bridge ?? new ReportedPowerBridge());
            });
        });
        using var adminToken = await fixture.SeededClientAsync("admin", "SEED_ADMIN_PASSWORD");
        using var managerToken = await fixture.ManagerClientAsync();
        using var admin = AuthorizedClient(host.CreateClient(), adminToken);
        using var client = AuthorizedClient(host.CreateClient(), managerToken);
        var setup = new LightingTestRig(fixture);
        var cabinet = await setup.NewCabinetAsync();
        var feeder = await setup.NewFeederAsync(cabinet);
        var other = await setup.NewFeederAsync(cabinet);
        var pole = await setup.NewPoleAsync(await setup.NewSegmentAsync(), feeder);
        var nodes = new List<string>();
        for (var i = 0; i < 2; i++)
        {
            using var created = await client.PostAsJsonAsync("/api/v1/assets/iot-nodes",
                new { cabinet_id = cabinet, data_source = "calibration_rig", supports_remote_control = true });
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            var node = created.Headers.Location!.OriginalString.Split('/')[^1];
            nodes.Add(node);
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsync($"/api/v1/assets/iot-nodes/{node}/credential", null)).StatusCode);
        }
        var server = "iot-" + Guid.NewGuid().ToString("N");
        Assert.Equal(HttpStatusCode.Created, (await admin.PostAsJsonAsync("/api/v1/iot/servers", new
        {
            server_id = server, commune_id = fixture.CommuneId,
            location = new { lat = 13.5, lng = 109.5 },
        })).StatusCode);
        await check(client, new(cabinet, feeder, other, pole, nodes[0], nodes[1], server), host.Services);
    }

    private sealed class ReportedPowerBridge : HttpMessageHandler
    {
        public bool RejectCommand { get; set; }
        public object? DiagnosticDevice { get; set; }
        public object[] AutoTransitions { get; set; } = [];
        public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? FeederHistory { get; set; }
        private JsonElement? desired;
        private JsonElement? successful;
        private Ack? successfulAck;
        private Ack? lastAck;
        private Sample? latestTelemetry;

        private sealed record Ack(DateTimeOffset ReceivedAt, string CommandId, string LampCommand);
        private sealed record Sample(DateTimeOffset MeasuredAt, string CommandId, string LampCommand);

        private string CommandId => desired!.Value.GetProperty("commandId").GetString()!;

        public Dictionary<string, DateTimeOffset> ReceiptTimes { get; } = [];
        public Dictionary<string, long?> ExecutionTimes { get; } = [];
        public Dictionary<string, long?> ExpiryTimes { get; } = [];
        public void Acknowledge(string lampCommand)
        {
            lastAck = new(DateTimeOffset.UtcNow.AddSeconds(-20), CommandId, lampCommand);
            ReceiptTimes[CommandId] = DateTimeOffset.UtcNow;
            successful = desired;
            successfulAck = lastAck;
        }

        private async Task<HttpResponseMessage> ReadReceiptsAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var keys = await request.Content!.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
            var receipts = keys.EnumerateArray()
                .Select(key => new { deviceId = key.GetProperty("deviceId").GetString(), commandId = key.GetProperty("commandId").GetString()! })
                .Where(key => key.deviceId == "probe" && ReceiptTimes.ContainsKey(key.commandId))
                .Select(key => new { key.deviceId, key.commandId, receivedAt = ReceiptTimes[key.commandId],
                    executedAtUnixSeconds = ExecutionTimes.GetValueOrDefault(key.commandId),
                    expiresAtUnixSeconds = ExpiryTimes.GetValueOrDefault(key.commandId) });
            return Json(receipts);
        }

        public void ReportTelemetry(string lampCommand, DateTimeOffset measuredAt) =>
            latestTelemetry = new(measuredAt, CommandId, lampCommand);

        public void AgeReports()
        {
            lastAck = lastAck! with { ReceivedAt = DateTimeOffset.UtcNow.AddMinutes(-2) };
            latestTelemetry = latestTelemetry! with { MeasuredAt = DateTimeOffset.UtcNow.AddMinutes(-2) };
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var pieces = request.RequestUri!.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (pieces.Length >= 5 && pieces[2] == "gateways") {
                var serverId = pieces[3];
                if (pieces[^1] == "route" && request.Method == HttpMethod.Get)
                    return Json(new { gatewayId = serverId, deviceIds = new[] { "probe", "probe-2" }, credentialIssued = true });
                if (pieces[^1] == "route" || pieces[^1] == "binding") return new(HttpStatusCode.NoContent);
                if (pieces[^1] == "status") return Json(new { status = "online", credentialIssued = true });
                if (pieces[^1] == "authorization")
                    return new(pieces[^2] is "probe" or "probe-2" ? HttpStatusCode.NoContent : HttpStatusCode.NotFound);
            }
            return request.RequestUri!.AbsolutePath switch
            {
                "/api/luxmap/gateways/by-device/probe/authorization" or "/api/luxmap/gateways/by-device/probe-2/authorization" => new(HttpStatusCode.NoContent),
                "/api/luxmap/gateways/by-device/probe/status" => Json(new { status = "online" }),
                "/api/luxmap/devices/probe/telemetry/history/page" => request.RequestUri.Query switch
                {
                    "?page=1&pageSize=50&sortBy=received_at" => Json(new { page = 1, pageSize = 50, total = 3,
                        items = new[] { Archived(3), Archived(2), Archived(1) } }),
                    "?page=2&pageSize=1&sortBy=measured_at" => Json(new { page = 2, pageSize = 1, total = 3,
                        items = new[] { Archived(1) } }),
                    _ => new(HttpStatusCode.BadRequest),
                },
                "/api/luxmap/devices/probe/telemetry/history" when request.RequestUri.Query == "?after=0&limit=1"
                    => Json(new { items = new[] { Archived(1) }, nextCursor = 1, hasMore = true }),
                "/api/luxmap/devices/feeder-history" or "/api/luxmap/devices/feeder-history/averages"
                    when FeederHistory is { } history => await history(request, ct),
                "/api/luxmap/devices/auto-transitions" => Json(AutoTransitions),
                "/api/luxmap/devices/command-receipts" => await ReadReceiptsAsync(request, ct),
                "/api/luxmap/devices/probe/command" when RejectCommand => new(HttpStatusCode.ServiceUnavailable),
                "/api/luxmap/devices/probe/command" => Json(await SaveCommandAsync(request, ct)),
                "/api/luxmap/devices/probe" when DiagnosticDevice is { } diagnostic => Json(diagnostic),
                "/api/luxmap/devices/probe" => Json(new
                {
                    deviceId = "probe",
                    desiredCommand = desired,
                    lastSuccessfulCommand = successful,
                    lastSuccessfulAck = successfulAck is { } success ? new {
                        receivedAt = success.ReceivedAt, data = new { success.CommandId, success.LampCommand },
                    } : null,
                    commandAcknowledged = desired is not null && lastAck?.CommandId == CommandId,
                    lastCommandAck = lastAck is { } ack ? new
                    {
                        receivedAt = ack.ReceivedAt, data = new { ack.CommandId, ack.LampCommand },
                    } : null,
                    latestTelemetry = latestTelemetry is { } sample ? new
                    {
                        receivedAt = DateTimeOffset.UtcNow,
                        data = new
                        {
                            sample.LampCommand, appliedCommandId = sample.CommandId,
                            timestamp = sample.MeasuredAt.ToOffset(TimeSpan.FromHours(7)).ToString(
                                "yyyy-MM-ddTHH:mm:ss", System.Globalization.CultureInfo.InvariantCulture),
                            voltage = 5, current = 100, power = 500,
                        },
                    } : null,
                }),
                _ => new(HttpStatusCode.NotFound),
            };
        }

        private static object Archived(int cursor) => new
        {
            cursor, reading = new { data = new { voltage = 5 }, receivedAt = DateTimeOffset.UtcNow, gatewayId = "TEST-GW" },
        };

        private async Task<JsonElement> SaveCommandAsync(HttpRequestMessage request, CancellationToken ct)
        {
            desired = await request.Content!.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
            return desired.Value;
        }

        private static HttpResponseMessage Json(object value) =>
            new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
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
            var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 5 && parts[2] == "gateways") {
                var id = parts[3];
                if (parts[^1] == "route" && request.Method == HttpMethod.Get)
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) {
                        Content = JsonContent.Create(new { gatewayId = id, deviceIds = new[] { "probe" }, credentialIssued = true }),
                    });
                if (parts[^1] is "binding" or "route")
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
                if (parts[^1] == "authorization")
                    return Task.FromResult(new HttpResponseMessage(parts[^2] == "probe" ? HttpStatusCode.NoContent : HttpStatusCode.NotFound));
                if (parts[^1] == "credential")
                    return Task.FromResult(request.Method == HttpMethod.Delete ? new HttpResponseMessage(HttpStatusCode.NoContent)
                        : new HttpResponseMessage(HttpStatusCode.OK) {
                            Content = JsonContent.Create(new { clientId = id, username = id, password = new string('A', 64), nodeIds = new[] { "probe" } }),
                        });
            }
            if (path.EndsWith("/devices/legacy-probe", StringComparison.Ordinal))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            var content = new StringContent("unexpected upstream response", Encoding.UTF8, "text/plain");
            if (request.RequestUri!.AbsolutePath.EndsWith("/mqtt", StringComparison.Ordinal))
                content.Headers.ContentType = null;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }
    }
}
