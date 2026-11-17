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
        const string register = "/api/v1/iot/nodes";
        using var serverResponse = await admin.PostAsJsonAsync("/api/v1/iot/servers", new
        {
            server_id = server, commune_id = fixture.CommuneId, location = new { lat = 13.5, lng = 109.5 },
        });
        Assert.Equal(HttpStatusCode.Created, serverResponse.StatusCode);

        object Payload(string feederId, string deviceId) => new
        {
            server_id = server, cabinet_id = cabinet, feeder_id = feederId, device_id = deviceId,
            data_source = "calibration_rig", supports_remote_control = true,
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
        Assert.Equal("GW-TEST", firstBody.GetProperty("gateway_id").GetString());
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

    private sealed class RegistrationBridge : HttpMessageHandler
    {
        public List<string> Devices { get; } = [];
        public bool RejectAppend { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Assert.Equal("registration.example.test", request.RequestUri!.Host);
            Assert.True(request.Headers.Contains("X-Iot-Provisioning-Key"));
            if (request.Method == HttpMethod.Get && request.RequestUri.AbsolutePath == "/api/luxmap/gateways/routes")
                return new(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new[] { new { gatewayId = "GW-TEST", deviceIds = Devices, credentialIssued = true } }),
                };
            Assert.Equal(HttpMethod.Put, request.Method);
            Assert.Equal("/api/luxmap/gateways/GW-TEST/route", request.RequestUri.AbsolutePath);
            if (RejectAppend) return new(HttpStatusCode.ServiceUnavailable);
            var route = await request.Content!.ReadFromJsonAsync<JsonElement>(ct);
            Devices.Add(route.GetProperty("deviceId").GetString()!);
            return new(HttpStatusCode.NoContent);
        }
    }

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
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.PutAsJsonAsync(nodePath, profile)).StatusCode);
        foreach (var invalid in new[] { "http://127.0.0.1:5000", "http://10.0.0.8", "https://telemetry.example.test/private" })
        {
            Assert.Equal(HttpStatusCode.BadRequest,
                (await admin.PostAsJsonAsync($"{root}/servers", new { server_id = id, base_url = invalid })).StatusCode);
        }
        Assert.Equal(HttpStatusCode.Created, (await admin.PostAsJsonAsync($"{root}/servers", server)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync($"{root}/servers", server)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await foreign.PutAsJsonAsync(nodePath, profile)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await manager.PutAsJsonAsync(nodePath,
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
            new { server_id = server, base_url = "https://telemetry.example.test", commune_id = fixture.CommuneId,
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

        var telemetry = $"{nodePath}/electrical/history";
        var received = await manager.GetFromJsonAsync<JsonElement>(telemetry);
        Assert.Equal(1, received.GetProperty("page").GetInt32());
        Assert.Equal(50, received.GetProperty("page_size").GetInt32());
        Assert.Equal(3, received.GetProperty("total").GetInt32());
        Assert.Equal(3, received.GetProperty("items")[0].GetProperty("cursor").GetInt64());
        var measured = await manager.GetFromJsonAsync<JsonElement>($"{telemetry}?page=2&page_size=1&sort_by=measured_at");
        Assert.Equal(2, measured.GetProperty("page").GetInt32());
        Assert.Equal(1, Assert.Single(measured.GetProperty("items").EnumerateArray()).GetProperty("cursor").GetInt64());
        var legacy = await manager.GetFromJsonAsync<JsonElement>($"{telemetry}?after=0&limit=1");
        Assert.Equal(1, legacy.GetProperty("next_cursor").GetInt64());
        Assert.True(legacy.GetProperty("has_more").GetBoolean());
        Assert.Equal(HttpStatusCode.BadRequest, (await manager.GetAsync($"{telemetry}?sort_by=unknown")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await manager.GetAsync($"{telemetry}?page=1&after=0")).StatusCode);

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
        var preservedElectrical = await manager.GetFromJsonAsync<JsonElement>($"{telemetry}?after=0&limit=1");
        Assert.Equal(1, preservedElectrical.GetProperty("next_cursor").GetInt64());
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
        clock.SetUtcNow(rejectedAt.AddSeconds(13));
        bridge.ReceiptTimes[newest[0].GetProperty("command_id").GetString()!] = rejectedAt.AddSeconds(12.001);
        var expired = await manager.GetFromJsonAsync<JsonElement>($"{history}?page=1&page_size=1");
        Assert.Equal("expired", expired.GetProperty("items")[0].GetProperty("status").GetString());
        Assert.True(acceptedAuto.GetProperty("bridge_accepted_at").GetDateTimeOffset() >=
            acceptedAuto.GetProperty("requested_at").GetDateTimeOffset());

        var page2 = await manager.GetFromJsonAsync<JsonElement>($"{history}?page=2&page_size=2");
        Assert.Equal(new[] { off, on }, page2.GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("command_id").GetString()));
        Assert.All(page2.GetProperty("items").EnumerateArray(),
            item => Assert.Equal("success", item.GetProperty("status").GetString()));
        var empty = await manager.GetFromJsonAsync<JsonElement>($"{history}?page=2147483647&page_size=200");
        Assert.Empty(empty.GetProperty("items").EnumerateArray());
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
    public Task Four_volt_diagnostics_counts_contiguous_samples_and_reports_only_supported_alerts()
    {
        var bridge = new ReportedPowerBridge();
        return WithSharedCabinetAsync(async (client, rig, _services) =>
        {
            var nodePath = $"/api/v1/iot/nodes/{rig.First}";
            object Source(string calibration, double nominalVoltage = 4.08) => new
            {
                server_id = rig.Server, device_id = "probe", feeder_id = rig.Feeder,
                nominal_voltage = nominalVoltage, nominal_current_ma = 100, nominal_power_mw = 500,
                lamp_count_calibration = calibration,
            };
            using var invalid = await client.PutAsJsonAsync(nodePath, Source("bad"));
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
            using var created = await client.PutAsJsonAsync(nodePath, Source("4v"));
            Assert.Equal(HttpStatusCode.NoContent, created.StatusCode);

            var diagnoses = $"{nodePath}/diagnostics";
            foreach (var (count, currents) in new[]
            {
                (10, Window(68.75, 13, 56.25)),
                (9, Window(63.75, 13, 56.25)),
                (8, Window(51.25, 12, 46.25)),
                (7, Window(47.5, 14, 43.75)),
                (6, Window(42.5, 13, 38.75)),
            })
            {
                bridge.DiagnosticDevice = DiagnosticTelemetry(currents);
                var payload = await client.GetFromJsonAsync<JsonElement>(diagnoses);
                var estimate = payload.GetProperty("lamp_count");
                Assert.Equal("estimated", estimate.GetProperty("status").GetString());
                Assert.Equal(25, estimate.GetProperty("samples").GetInt32());
                Assert.Equal(count, estimate.GetProperty("estimated_lit").GetInt32());
                Assert.Equal(10 - count, estimate.GetProperty("estimated_missing").GetInt32());
                Assert.Equal(currents.Count(current => current > 65),
                    estimate.GetProperty("threshold_counts").GetProperty("above_65_ma").GetInt32());
                var anomalies = payload.GetProperty("anomalies").EnumerateArray();
                if (count == 10)
                    Assert.Empty(anomalies);
                else
                {
                    var alert = Assert.Single(anomalies);
                    Assert.Equal("lamp_count_shortfall", alert.GetProperty("code").GetString());
                    Assert.Equal(10 - count, alert.GetProperty("observed").GetDouble());
                }
            }

            bridge.DiagnosticDevice = DiagnosticTelemetry(Window(68.75, 1, 63.75));
            await AssertStatusAsync("undetermined", 25);
            bridge.DiagnosticDevice = DiagnosticTelemetry(Window(47.5, 12, 43.75), skipSequence: 12);
            await AssertStatusAsync("insufficient_samples", 13);
            bridge.DiagnosticDevice = DiagnosticTelemetry(Window(47.5, 12, 43.75), oldRtc: true);
            await AssertStatusAsync("insufficient_samples", 0);

            bridge.DiagnosticDevice = DiagnosticTelemetry(Window(50, 25, 50), voltage: 0.04);
            await AssertSingleAlertAsync("voltage_loss");
            bridge.DiagnosticDevice = DiagnosticTelemetry(Window(50, 25, 50), voltage: 4.6);
            await AssertSingleAlertAsync("voltage_deviation");
            bridge.DiagnosticDevice = DiagnosticTelemetry(Window(50, 25, 50), staleDelivery: true);
            await AssertSingleAlertAsync("telemetry_stale");
            bridge.DiagnosticDevice = DiagnosticTelemetry(Window(50, 25, 50), lampCommand: "OFF");
            var off = await client.GetFromJsonAsync<JsonElement>(diagnoses);
            Assert.Empty(off.GetProperty("anomalies").EnumerateArray());
            Assert.Equal(JsonValueKind.Null, off.GetProperty("lamp_count").ValueKind);

            using var updated = await client.PutAsJsonAsync(nodePath, new
            {
                server_id = rig.Server, device_id = "probe", feeder_id = rig.Feeder,
                nominal_voltage = 5, nominal_current_ma = 100, nominal_power_mw = 500, use_ten_lamp_calibration = true,
            });
            Assert.Equal(HttpStatusCode.NoContent, updated.StatusCode);
            bridge.DiagnosticDevice = DiagnosticTelemetry(Window(100, 25, 100), voltage: 5);
            var legacy = await client.GetFromJsonAsync<JsonElement>(diagnoses);
            var legacyCount = legacy.GetProperty("lamp_count");
            Assert.Equal(10, legacyCount.GetProperty("estimated_lit").GetInt32());
            Assert.Equal(15, legacyCount.GetProperty("required_votes").GetInt32());
            Assert.False(legacyCount.TryGetProperty("threshold_counts", out _));

            async Task AssertStatusAsync(string status, int samples)
            {
                var payload = await client.GetFromJsonAsync<JsonElement>(diagnoses);
                var estimate = payload.GetProperty("lamp_count");
                Assert.Equal(status, estimate.GetProperty("status").GetString());
                Assert.Equal(samples, estimate.GetProperty("samples").GetInt32());
                Assert.Equal(JsonValueKind.Null, estimate.GetProperty("estimated_missing").ValueKind);
                Assert.Empty(payload.GetProperty("anomalies").EnumerateArray());
            }

            async Task AssertSingleAlertAsync(string code)
            {
                var payload = await client.GetFromJsonAsync<JsonElement>(diagnoses);
                Assert.Equal(code, Assert.Single(payload.GetProperty("anomalies").EnumerateArray())
                    .GetProperty("code").GetString());
                Assert.Equal(JsonValueKind.Null, payload.GetProperty("lamp_count").ValueKind);
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

    private static double[] Window(double first, int count, double rest) =>
        [.. Enumerable.Repeat(first, count), .. Enumerable.Repeat(rest, 25 - count)];

    private static object DiagnosticTelemetry(double[] currents, double voltage = 4.07, string lampCommand = "ON",
        int? skipSequence = null, bool oldRtc = false, bool staleDelivery = false)
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
                data = new { lampCommand, voltage, current, power = voltage * current, sessionId = "calibration",
                    sequenceNumber = index + 1, timestamp = measured.AddSeconds(-offset).ToOffset(TimeSpan.FromHours(7))
                        .ToString("yyyy-MM-ddTHH:mm:ss", System.Globalization.CultureInfo.InvariantCulture) },
            };
        }).ToArray();
        return new
        {
            deviceId = "probe", latestTelemetry = rows[^1],
            history = rows.Where(row => row.data.sequenceNumber != skipSequence)
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
                var sortBy = query["sortBy"].ToString();
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

            var measured = await client.GetFromJsonAsync<JsonElement>($"{feeder}/history?page=1&page_size=1&sort_by=measured_at");
            Assert.Equal(2, measured.GetProperty("total").GetInt32());
            Assert.Equal(rig.Second, measured.GetProperty("items")[0].GetProperty("node_id").GetString());
            var next = await client.GetFromJsonAsync<JsonElement>($"{feeder}/history?page=2&page_size=1&sort_by=measured_at");
            Assert.Equal(1, next.GetProperty("items")[0].GetProperty("cursor").GetInt64());
            var received = await client.GetFromJsonAsync<JsonElement>($"{feeder}/history?page_size=1&sort_by=received_at");
            Assert.Equal(rig.First, received.GetProperty("items")[0].GetProperty("node_id").GetString());
            var newLatest = await client.GetFromJsonAsync<JsonElement>(feeder);
            Assert.Equal("online", newLatest.GetProperty("status").GetString());

            using var foreignToken = await fixture.ForeignCommuneClientAsync();
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{feeder}/history");
            request.Headers.Authorization = foreignToken.DefaultRequestHeaders.Authorization;
            using var foreign = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
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
        Func<HttpClient, SharedCabinet, IServiceProvider, Task> check, ReportedPowerBridge? bridge = null)
    {
        using var factory = new LuxMapApiFactory();
        using var host = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("IotDiagnostics:AllowedOrigins:0", "https://telemetry.example.test");
            builder.UseSetting("Mqtt:ProvisioningKey", new string('a', 32));
            builder.ConfigureServices(services => services.AddHttpClient<IotDiagnosticsService>()
                .ConfigurePrimaryHttpMessageHandler(() => bridge ?? new ReportedPowerBridge()));
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
            server_id = server, base_url = "https://telemetry.example.test", commune_id = fixture.CommuneId,
            location = new { lat = 13.5, lng = 109.5 },
        })).StatusCode);
        await check(client, new(cabinet, feeder, other, pole, nodes[0], nodes[1], server), host.Services);
    }

    private sealed class ReportedPowerBridge : HttpMessageHandler
    {
        public bool RejectCommand { get; set; }
        public object? DiagnosticDevice { get; set; }
        public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? FeederHistory { get; set; }
        private JsonElement? desired;
        private Ack? lastAck;
        private Sample? latestTelemetry;

        private sealed record Ack(DateTimeOffset ReceivedAt, string CommandId, string LampCommand);
        private sealed record Sample(DateTimeOffset MeasuredAt, string CommandId, string LampCommand);

        private string CommandId => desired!.Value.GetProperty("commandId").GetString()!;

        public Dictionary<string, DateTimeOffset> ReceiptTimes { get; } = [];
        public void Acknowledge(string lampCommand)
        {
            lastAck = new(DateTimeOffset.UtcNow.AddSeconds(-20), CommandId, lampCommand);
            ReceiptTimes[CommandId] = DateTimeOffset.UtcNow;
        }

        private async Task<HttpResponseMessage> ReadReceiptsAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var keys = await request.Content!.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
            var receipts = keys.EnumerateArray()
                .Select(key => new { deviceId = key.GetProperty("deviceId").GetString(), commandId = key.GetProperty("commandId").GetString()! })
                .Where(key => key.deviceId == "probe" && ReceiptTimes.ContainsKey(key.commandId))
                .Select(key => new { key.deviceId, key.commandId, receivedAt = ReceiptTimes[key.commandId] });
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
                "/api/luxmap/devices/feeder-history" when FeederHistory is { } history => await history(request, ct),
                "/api/luxmap/devices/command-receipts" => await ReadReceiptsAsync(request, ct),
                "/api/luxmap/devices/probe/command" when RejectCommand => new(HttpStatusCode.ServiceUnavailable),
                "/api/luxmap/devices/probe/command" => Json(await SaveCommandAsync(request, ct)),
                "/api/luxmap/devices/probe" when DiagnosticDevice is { } diagnostic => Json(diagnostic),
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
