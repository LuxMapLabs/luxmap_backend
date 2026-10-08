using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using LuxMap.Modules.Assets.Entities;
using LuxMap.Modules.Telemetry.Entities;
using LuxMap.Persistence;
using LuxMap.Shared.Contracts.Enums;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;

namespace LuxMap.Api.Tests;

/// <summary>
/// LIGHT-CTRL 2a — the device registry (devices, relays, secrets) and the <c>Device</c> authentication scheme (drift LC-2, LC-7).
/// </summary>
/// <remarks>
/// ⚠️ SELF-SIGNED. Expectations are LITERALS. The scheme is exercised through <see cref="DeviceProbeController"/>, a test-only
/// device endpoint: the real ones arrive with the commands (2b).
/// </remarks>
[Collection(nameof(AssetDatabaseCollection))]
public sealed class DeviceRegistryTests(AssetImportFixture fixture, DeviceProbeFactory probe) : IClassFixture<DeviceProbeFactory>
{
    private const string Nodes = "/api/v1/assets/iot-nodes";
    private const string Probe = "/api/v1/_device-probe";

    private const double Lng = 109.4;
    private const double Lat = 13.4;

    // ── Registry ───────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_device_registers_in_a_cabinet_takes_its_commune_and_never_shows_a_secret()
    {
        var client = await fixture.ManagerClientAsync();
        var cabinet = await NewCabinetAsync(fixture.CommuneId);

        var nodeId = await CreateNodeAsync(client, cabinet, "calibration_rig", remote: true);
        var node = await GetAsync(client, $"{Nodes}/{nodeId}");

        string[] expected =
        [
            "cabinet_id", "commune_id", "credential_set_at", "data_source", "has_credential", "last_report_at", "node_id",
            "node_role", "relays", "supports_remote_control", "updated_at",
        ];
        Assert.Equal(expected, node.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal).ToArray());
        Assert.Equal(cabinet, node.GetProperty("cabinet_id").GetString());
        Assert.Equal(fixture.CommuneId, node.GetProperty("commune_id").GetString());
        Assert.False(node.GetProperty("has_credential").GetBoolean());
        Assert.Empty(node.GetProperty("relays").EnumerateArray());
    }

    [Theory]
    [InlineData("field")]
    [InlineData("public_imagery")]
    public async Task A_device_is_never_field_data(string source)
    {
        var client = await fixture.ManagerClientAsync();
        var response = await client.PostAsJsonAsync(Nodes, new { cabinet_id = await NewCabinetAsync(fixture.CommuneId), data_source = source });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_cabinet_carries_one_device_and_a_foreign_cabinet_is_a_404()
    {
        var client = await fixture.ManagerClientAsync();
        var cabinet = await NewCabinetAsync(fixture.CommuneId);
        await CreateNodeAsync(client, cabinet, "simulated");

        var second = await client.PostAsJsonAsync(Nodes, new { cabinet_id = cabinet, data_source = "simulated" });
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("ux_iot_node_cabinet_id", (await ErrorAsync(second)).GetProperty("details").GetProperty("constraint").GetString());

        var foreign = await client.PostAsJsonAsync(Nodes, new { cabinet_id = await NewCabinetAsync(fixture.ForeignCommuneId), data_source = "simulated" });
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
    }

    [Fact]
    public async Task Only_a_manager_registers_a_device()
    {
        var engineer = await fixture.FieldEngineerClientAsync();
        var response = await engineer.PostAsJsonAsync(Nodes, new { cabinet_id = await NewCabinetAsync(fixture.CommuneId), data_source = "simulated" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>
    /// A relay switches only a feeder of the device's OWN cabinet (CAB-4), one feeder is switched by one relay, and re-wiring a relay
    /// replaces its row — EF deletes before it inserts, so <c>ux_feeder_control_node_id_relay_no</c> holds.
    /// </summary>
    [Fact]
    public async Task Relays_wire_only_within_the_cabinet_one_feeder_one_relay_and_rewire_cleanly()
    {
        var client = await fixture.ManagerClientAsync();
        var cabinet = await NewCabinetAsync(fixture.CommuneId);
        var other = await NewCabinetAsync(fixture.CommuneId);
        var node = await CreateNodeAsync(client, cabinet, "calibration_rig", remote: true);
        var otherNode = await CreateNodeAsync(client, other, "calibration_rig");
        var odd = await NewFeederAsync(cabinet);
        var even = await NewFeederAsync(cabinet);
        var elsewhere = await NewFeederAsync(other);
        var loose = await NewFeederAsync(null);

        await PutAsync(client, $"{Nodes}/{node}/relays/1", new { feeder_id = odd });
        await PutAsync(client, $"{Nodes}/{node}/relays/2", new { feeder_id = even });
        Assert.Equal([(1, odd), (2, even)], await RelaysAsync(client, node));

        foreach (var feeder in new[] { elsewhere, loose })
        {
            var refused = await client.PutAsJsonAsync($"{Nodes}/{node}/relays/3", new { feeder_id = feeder });
            Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
            Assert.Equal("FEEDER_NOT_IN_CABINET", (await ErrorAsync(refused)).GetProperty("code").GetString());
        }

        var taken = await client.PutAsJsonAsync($"{Nodes}/{otherNode}/relays/1", new { feeder_id = elsewhere });
        Assert.Equal(HttpStatusCode.NoContent, taken.StatusCode);
        var held = await client.PutAsJsonAsync($"{Nodes}/{node}/relays/3", new { feeder_id = odd });
        Assert.Equal(HttpStatusCode.Conflict, held.StatusCode);
        Assert.Equal("ASSET_IN_USE", (await ErrorAsync(held)).GetProperty("code").GetString());

        // Re-wire relay 1 from `odd` to a new feeder, then unwire relay 2.
        var third = await NewFeederAsync(cabinet);
        await PutAsync(client, $"{Nodes}/{node}/relays/1", new { feeder_id = third });
        await PutAsync(client, $"{Nodes}/{node}/relays/2", new { feeder_id = (string?)null });
        Assert.Equal([(1, third)], await RelaysAsync(client, node));
    }

    [Theory]
    [InlineData(0, """{ "feeder_id": null }""")]
    [InlineData(33, """{ "feeder_id": null }""")]
    [InlineData(1, """{ }""")]
    [InlineData(1, """{ "feeder_id": 7 }""")]
    public async Task A_relay_number_out_of_range_or_a_missing_key_is_a_400(int relay, string body)
    {
        var client = await fixture.ManagerClientAsync();
        var node = await CreateNodeAsync(client, await NewCabinetAsync(fixture.CommuneId), "simulated");

        var response = await client.PutAsync($"{Nodes}/{node}/relays/{relay}",
            new StringContent(body, System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_device_with_a_wired_relay_is_not_deleted_and_an_unwired_one_is()
    {
        var client = await fixture.ManagerClientAsync();
        var cabinet = await NewCabinetAsync(fixture.CommuneId);
        var node = await CreateNodeAsync(client, cabinet, "simulated");
        await PutAsync(client, $"{Nodes}/{node}/relays/1", new { feeder_id = await NewFeederAsync(cabinet) });

        var refused = await client.DeleteAsync($"{Nodes}/{node}");
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal("ASSET_IN_USE", (await ErrorAsync(refused)).GetProperty("code").GetString());

        await PutAsync(client, $"{Nodes}/{node}/relays/1", new { feeder_id = (string?)null });
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"{Nodes}/{node}")).StatusCode);
    }

    // ── Credential + the Device scheme ─────────────────────────────────────────────────────────

    /// <summary>
    /// The secret is shown once and stored only as a hash; the device then authenticates with its commune — and ONLY its commune —
    /// as scope, so the ordinary query filter hides every other commune's devices from it.
    /// </summary>
    [Fact]
    public async Task A_device_authenticates_with_its_secret_and_gets_exactly_its_own_commune()
    {
        var client = await fixture.ManagerClientAsync();
        var node = await CreateNodeAsync(client, await NewCabinetAsync(fixture.CommuneId), "calibration_rig", remote: true);
        var foreignNode = await NewNodeAsync(await NewCabinetAsync(fixture.ForeignCommuneId));
        var secret = await IssueAsync(client, node);

        var stored = await fixture.QueryAsync(db => db.Set<IotNode>().IgnoreQueryFilters()
            .Where(n => n.NodeId == node).Select(n => n.CredentialHash).SingleAsync());
        Assert.NotEqual(secret, stored);
        Assert.Equal(64, stored!.Length);

        var whoami = await ProbeAsync($"{node}.{secret}");
        Assert.Equal(HttpStatusCode.OK, whoami.StatusCode);
        var body = JsonDocument.Parse(await whoami.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(node, body.GetProperty("node_id").GetString());
        Assert.Equal([fixture.CommuneId], body.GetProperty("communes").EnumerateArray().Select(c => c.GetString() ?? "<null>").ToArray());
        Assert.False(body.GetProperty("system_wide").GetBoolean());
        var visible = body.GetProperty("visible_nodes").EnumerateArray().Select(n => n.GetString()).ToArray();
        Assert.Contains(node, visible);
        Assert.DoesNotContain(foreignNode, visible);

        var row = await GetAsync(client, $"{Nodes}/{node}");
        Assert.True(row.GetProperty("has_credential").GetBoolean());
        Assert.DoesNotContain(secret, row.GetRawText(), StringComparison.Ordinal);
    }

    /// <summary>
    /// RFC 9110 §11: the scheme word is case-insensitive and 1*SP may follow it — firmware spelling it <c>device</c> must not be
    /// locked out. Whitespace INSIDE the credential, or a second credential, is still malformed.
    /// </summary>
    [Fact]
    public async Task The_scheme_word_is_case_insensitive_and_extra_spaces_are_allowed_but_not_inside_the_credential()
    {
        var client = await fixture.ManagerClientAsync();
        var node = await CreateNodeAsync(client, await NewCabinetAsync(fixture.CommuneId), "calibration_rig", remote: true);
        var secret = await IssueAsync(client, node);

        foreach (var header in new[] { $"Device {node}.{secret}", $"device {node}.{secret}", $"DEVICE   {node}.{secret}" })
        {
            Assert.True((await RawProbeAsync(header)).StatusCode == HttpStatusCode.OK, header.Split(' ')[0]);
        }

        foreach (var header in new[] { $"Device {node}. {secret}", $"Device {node}.{secret} {node}.{secret}", $"Device{node}.{secret}", "Device " })
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await RawProbeAsync(header)).StatusCode);
        }
    }

    /// <summary>A 401 from a device endpoint names the scheme it wants (RFC 9110 §11.6.1), and still carries the error envelope.</summary>
    [Fact]
    public async Task A_refused_device_gets_a_device_challenge_and_the_error_envelope()
    {
        var response = await RawProbeAsync("Device NODE-999999.wrong");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(["Device"], response.Headers.WwwAuthenticate.Select(challenge => challenge.Scheme).ToArray());
        Assert.Equal("UNAUTHENTICATED", (await ErrorAsync(response)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Every_wrong_credential_is_the_same_401_and_a_rotated_secret_stops_working()
    {
        var client = await fixture.ManagerClientAsync();
        var node = await CreateNodeAsync(client, await NewCabinetAsync(fixture.CommuneId), "calibration_rig", remote: true);
        var never = await CreateNodeAsync(client, await NewCabinetAsync(fixture.CommuneId), "simulated");
        var first = await IssueAsync(client, node);
        var second = await IssueAsync(client, node);

        Assert.Equal(HttpStatusCode.OK, (await ProbeAsync($"{node}.{second}")).StatusCode);

        foreach (var credential in new[]
                 {
                     $"{node}.{first}",          // rotated away
                     $"{node}.not-the-secret",   // wrong
                     $"{never}.{second}",        // device with no secret issued
                     $"NODE-999999.{second}",    // no such device
                     $"{node}",                  // malformed
                     $"{node}.{new string('x', 200)}", // too long
                 })
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await ProbeAsync(credential)).StatusCode);
        }
    }

    /// <summary>The schemes never cross: a user's Bearer token does not open a device endpoint, a device header does not open a user one.</summary>
    [Fact]
    public async Task A_bearer_token_cannot_reach_a_device_endpoint_and_a_device_cannot_reach_a_user_endpoint()
    {
        var manager = await fixture.ManagerClientAsync();
        var node = await CreateNodeAsync(manager, await NewCabinetAsync(fixture.CommuneId), "calibration_rig", remote: true);
        var secret = await IssueAsync(manager, node);

        using var bearerOnProbe = new HttpRequestMessage(HttpMethod.Get, Probe);
        bearerOnProbe.Headers.Authorization = manager.DefaultRequestHeaders.Authorization;
        Assert.Equal(HttpStatusCode.Unauthorized, (await probe.CreateClient().SendAsync(bearerOnProbe)).StatusCode);

        var device = fixture.CreateClient();
        device.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Device", $"{node}.{secret}");
        Assert.Equal(HttpStatusCode.Unauthorized, (await device.GetAsync(Nodes)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await device.GetAsync("/api/v1/auth/me")).StatusCode);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────────────────────

    /// <summary>The header exactly as given — <see cref="AuthenticationHeaderValue"/> would normalise the spacing.</summary>
    private async Task<HttpResponseMessage> RawProbeAsync(string header)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Probe);
        Assert.True(request.Headers.TryAddWithoutValidation("Authorization", header));
        return await probe.CreateClient().SendAsync(request);
    }

    private Task<HttpResponseMessage> ProbeAsync(string credential)
    {
        var client = probe.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Device", credential);
        return client.GetAsync(Probe);
    }

    private static async Task<string> CreateNodeAsync(HttpClient client, string cabinet, string source, bool remote = false)
    {
        var response = await client.PostAsJsonAsync(Nodes, new { cabinet_id = cabinet, data_source = source, supports_remote_control = remote });
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return response.Headers.Location!.OriginalString.Split('/')[^1];
    }

    private static async Task<string> IssueAsync(HttpClient client, string node)
    {
        var response = await client.PostAsync($"{Nodes}/{node}/credential", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(["credential_set_at", "node_id", "secret"], body.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal).ToArray());
        var secret = body.GetProperty("secret").GetString()!;
        Assert.Equal(43, secret.Length);
        return secret;
    }

    private static async Task<(int, string)[]> RelaysAsync(HttpClient client, string node)
        => [.. (await GetAsync(client, $"{Nodes}/{node}")).GetProperty("relays").EnumerateArray()
            .Select(relay => (relay.GetProperty("relay_no").GetInt32(), relay.GetProperty("feeder_id").GetString() ?? "<null>"))];

    private static async Task PutAsync(HttpClient client, string url, object body)
    {
        var response = await client.PutAsJsonAsync(url, body);
        Assert.True(response.StatusCode == HttpStatusCode.NoContent, $"{url} answered {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
    }

    private static async Task<JsonElement> GetAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{url} answered {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
    }

    private static async Task<JsonElement> ErrorAsync(HttpResponseMessage response)
        => JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("error").Clone();

    private Task<T> AsSystemAsync<T>(Func<LuxMapDbContext, Task<T>> write)
        => fixture.QueryAsync(async db =>
        {
            using (db.EnterUnscopedSystemWriteBackdoor())
            {
                return await write(db);
            }
        });

    private Task<string> NewCabinetAsync(string communeId)
        => AsSystemAsync(async db =>
        {
            var cabinet = new ElectricalCabinet
            {
                CabinetName = "registry probe cabinet",
                CommuneId = communeId,
                Geom = new Point(Lng, Lat) { SRID = 4326 },
                DataSource = DataSource.CalibrationRig,
            };
            db.Add(cabinet);
            await db.SaveChangesAsync();
            return cabinet.CabinetId;
        });

    private Task<string> NewFeederAsync(string? cabinetId)
        => AsSystemAsync(async db =>
        {
            var commune = cabinetId is null
                ? fixture.CommuneId
                : await db.Set<ElectricalCabinet>().IgnoreQueryFilters().Where(c => c.CabinetId == cabinetId).Select(c => c.CommuneId).SingleAsync();
            var feeder = new Feeder
            {
                FeederName = "registry probe feeder",
                CommuneId = commune,
                CabinetId = cabinetId,
                CabinetSource = cabinetId is null ? null : TopologySource.Inferred,
            };
            db.Add(feeder);
            await db.SaveChangesAsync();
            return feeder.FeederId;
        });

    private Task<string> NewNodeAsync(string cabinetId)
        => AsSystemAsync(async db =>
        {
            var cabinet = await db.Set<ElectricalCabinet>().IgnoreQueryFilters().SingleAsync(c => c.CabinetId == cabinetId);
            var node = new IotNode { CabinetId = cabinetId, CommuneId = cabinet.CommuneId, DataSource = DataSource.Simulated };
            db.Add(node);
            await db.SaveChangesAsync();
            return node.NodeId;
        });
}
