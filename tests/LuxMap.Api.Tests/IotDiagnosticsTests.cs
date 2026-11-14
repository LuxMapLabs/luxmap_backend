using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
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
            server_id = id, device_id = "probe", feeder_id = rig.Feeders[0],
            nominal_voltage = 5, nominal_current_ma = 100, nominal_power_mw = 500,
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
            new { profile.server_id, device_id = "unauthorized", profile.feeder_id,
                profile.nominal_voltage, profile.nominal_current_ma, profile.nominal_power_mw })).StatusCode);
        var credentialPath = $"{root}/servers/{id}/gateways/TEST-GW/credential";
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.PostAsync(credentialPath, null)).StatusCode);
        using var credential = await admin.PostAsync(credentialPath, null);
        Assert.Equal(HttpStatusCode.OK, credential.StatusCode);
        Assert.True(credential.Headers.CacheControl?.NoStore == true);
        var setup = new LightingTestRig(fixture);
        var otherFeeder = await setup.NewFeederAsync(await setup.NewCabinetAsync());
        var wrongFeeder = new
        {
            profile.server_id, profile.device_id, feeder_id = otherFeeder,
            profile.nominal_voltage, profile.nominal_current_ma, profile.nominal_power_mw,
        };
        Assert.Equal(HttpStatusCode.BadRequest, (await manager.PostAsJsonAsync($"{nodePath}/source", wrongFeeder)).StatusCode);
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

        Assert.Equal(HttpStatusCode.NotFound, (await foreign.PutAsJsonAsync($"{nodePath}/source", profile)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await foreign.DeleteAsync($"{nodePath}/source")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await manager.PutAsJsonAsync($"{nodePath}/source", wrongFeeder)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await manager.PutAsJsonAsync($"{nodePath}/source", profile)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await manager.DeleteAsync($"{nodePath}/source")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await manager.DeleteAsync($"{nodePath}/source")).StatusCode);
        var legacyDeviceId = "legacy-probe";
        var legacyProfile = JsonSerializer.Serialize(new { profile.server_id, device_id = legacyDeviceId, profile.feeder_id, pole_id = "legacy-pole", relay_no = 2, profile.nominal_voltage, profile.nominal_current_ma, profile.nominal_power_mw });
        await fixture.QueryAsync(db => db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO iot_source_registration (node_id, server_id, device_id, profile) VALUES ({rig.Node}, {id}, {legacyDeviceId}, CAST({legacyProfile} AS jsonb))"));
        using var legacy = await manager.GetAsync($"{nodePath}/diagnostics");
        var legacyJson = await legacy.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(legacy.IsSuccessStatusCode, legacyJson.GetRawText());
        Assert.Equal(profile.feeder_id, legacyJson.GetProperty("feeder_id").GetString());
        Assert.Equal("no_data", legacyJson.GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.NoContent, (await manager.DeleteAsync($"{nodePath}/source")).StatusCode);
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
        using var factory = new LuxMapApiFactory();
        using var host = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("IotDiagnostics:AllowedOrigins:0", "https://telemetry.example.test");
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
        var segment = await setup.NewSegmentAsync();
        var server = "iot-" + Guid.NewGuid().ToString("N");
        using var registration = await admin.PostAsJsonAsync("/api/v1/iot/servers",
            new { server_id = server, base_url = "https://telemetry.example.test", commune_id = fixture.CommuneId,
                location = new { lat = 13.5, lng = 109.5 } });
        Assert.Equal(HttpStatusCode.Created, registration.StatusCode);

        using var created = await manager.PostAsJsonAsync("/api/v1/assets/iot-nodes",
            new { cabinet_id = cabinet, data_source = "calibration_rig", supports_remote_control = true });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var node = created.Headers.Location!.OriginalString.Split('/')[^1];
        var nodePath = $"/api/v1/iot/nodes/{node}";

        using var source = await manager.PostAsJsonAsync($"{nodePath}/source", new
        {
            server_id = server, device_id = "probe", feeder_id = feeder,
            nominal_voltage = 5, nominal_current_ma = 100, nominal_power_mw = 500,
        });
        Assert.Equal(HttpStatusCode.Created, source.StatusCode);

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
        await AssertPowerAsync("unknown", on, false);
        bridge.Acknowledge("ON");
        await AssertPowerAsync("on", on, true);

        var off = await SubmitAsync("MANUAL", "OFF");
        await AssertPowerAsync("on", off, false);
        bridge.Acknowledge("OFF");
        await AssertPowerAsync("off", off, true);

        var auto = await SubmitAsync("AUTO", null);
        bridge.Acknowledge("ON");
        await AssertPowerAsync("on", auto, true);
        bridge.ReportTelemetry("OFF", DateTimeOffset.UtcNow.AddSeconds(-10));
        await AssertPowerAsync("off", auto, true);
        bridge.AgeReports();
        await AssertPowerAsync("unknown", auto, true);

        async Task<string?> SubmitAsync(string mode, string? command)
        {
            using var response = await manager.PostAsJsonAsync($"{nodePath}/command", new { mode, command });
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

    private sealed class ReportedPowerBridge : HttpMessageHandler
    {
        private JsonElement? desired;
        private Ack? lastAck;
        private Sample? latestTelemetry;

        private sealed record Ack(DateTimeOffset ReceivedAt, string CommandId, string LampCommand);
        private sealed record Sample(DateTimeOffset MeasuredAt, string CommandId, string LampCommand);

        private string CommandId => desired!.Value.GetProperty("commandId").GetString()!;

        public void Acknowledge(string lampCommand) => lastAck = new(DateTimeOffset.UtcNow.AddSeconds(-20), CommandId, lampCommand);

        public void ReportTelemetry(string lampCommand, DateTimeOffset measuredAt) =>
            latestTelemetry = new(measuredAt, CommandId, lampCommand);

        public void AgeReports()
        {
            lastAck = lastAck! with { ReceivedAt = DateTimeOffset.UtcNow.AddMinutes(-2) };
            latestTelemetry = latestTelemetry! with { MeasuredAt = DateTimeOffset.UtcNow.AddMinutes(-2) };
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            return request.RequestUri!.AbsolutePath switch
            {
                "/api/luxmap/gateways/by-device/probe/authorization" => new(HttpStatusCode.NoContent),
                "/api/luxmap/gateways/by-device/probe/status" => Json(new { status = "online" }),
                "/api/luxmap/devices/probe/command" => Json(await SaveCommandAsync(request, ct)),
                "/api/luxmap/devices/probe" => Json(new
                {
                    deviceId = "probe",
                    desiredCommand = desired,
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
            if (path.EndsWith("/gateways/routes", StringComparison.Ordinal))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new[] { new { gatewayId = "TEST-GW", deviceIds = new[] { "probe" },
                        credentialIssued = true } }),
                });
            if (path.EndsWith("/authorization", StringComparison.Ordinal))
                return Task.FromResult(new HttpResponseMessage(path.EndsWith("/probe/authorization", StringComparison.Ordinal)
                    ? HttpStatusCode.NoContent : HttpStatusCode.NotFound));
            if (path.EndsWith("/TEST-GW/credential", StringComparison.Ordinal))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new { clientId = "TEST-GW", username = "TEST-GW",
                        password = new string('A', 64), nodeIds = new[] { "probe" } }),
                });
            if (path.EndsWith("/devices/legacy-probe", StringComparison.Ordinal))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            var content = new StringContent("unexpected upstream response", Encoding.UTF8, "text/plain");
            if (request.RequestUri!.AbsolutePath.EndsWith("/mqtt", StringComparison.Ordinal))
                content.Headers.ContentType = null;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }
    }
}
