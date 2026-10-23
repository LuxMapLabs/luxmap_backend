using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LuxMap.Modules.Faults.Entities;
using LuxMap.Modules.Telemetry.Mqtt;
using LuxMap.Persistence.Versions;
using LuxMap.Shared.Contracts.Enums;
using LuxMap.Shared.Contracts.Errors;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Buffers;
using System.Text;

namespace LuxMap.Api.Tests;

/// <summary>
/// BE-34 — the version registry for the system admin (<c>/admin/artifact-versions</c>): list, register a firmware ahead, and count
/// the results a version produced.
/// </summary>
/// <remarks>
/// In <see cref="AssetDatabaseCollection"/>: the usage counts read faults and IoT devices that other classes there write, so every
/// version here is a fresh string no other row can carry. Registry rows are immutable and stay; they are registered as the seeded
/// <c>admin</c>, never a throwaway account a teardown would have to delete.
/// </remarks>
[Collection(nameof(AssetDatabaseCollection))]
public sealed class ArtifactVersionTests(AssetImportFixture fixture)
{
    private const string Url = "/api/v1/admin/artifact-versions";

    private static string Fresh(string prefix) => prefix + Guid.NewGuid().ToString("N")[..12];

    [Fact]
    public async Task The_admin_registers_a_firmware_ahead_with_its_hash_and_it_lists_newest_first()
    {
        var admin = await AdminAsync();
        var version = Fresh("fw-");
        var hash = new string('a', 64);

        var created = await admin.PostAsJsonAsync(Url, new { component = "node_firmware", version, artifact_hash = hash, metadata = new { board = "esp32" } });

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var item = await created.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal((version, hash, "esp32"), (item.GetProperty("version").GetString(), item.GetProperty("artifact_hash").GetString(),
            item.GetProperty("metadata").GetProperty("board").GetString()));
        Assert.StartsWith("USR-", item.GetProperty("created_by").GetString(), StringComparison.Ordinal);

        var listed = await admin.GetFromJsonAsync<JsonElement>($"{Url}?component=node_firmware&page_size=200");
        Assert.Equal(version, listed.GetProperty("items")[0].GetProperty("version").GetString());
        Assert.All(listed.GetProperty("items").EnumerateArray(), i => Assert.Equal("node_firmware", i.GetProperty("component").GetString()));
    }

    public static TheoryData<string> Refused => new()
    {
        """{"component":"cv_model","version":"x"}""",
        """{"component":"node_firmware"}""",
        """{"component":"node_firmware","version":"   "}""",
        """{"component":"node_firmware","version":"1","artifact_hash":"ABC"}""",
        """{"component":"node_firmware","version":"1","artifact_hash":"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"}""",
        """{"component":"node_firmware","version":"1","metadata":[1,2]}""",
        // `$` in .NET also matches before a final newline: 64 hex + "\n" must not pass as a hash (Codex review).
        """{"component":"node_firmware","version":"1","artifact_hash":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\n"}""",
        // Valid JSON, but jsonb stores no NUL: a 400 about the metadata, never a 500.
        """{"component":"node_firmware","version":"nul-metadata","metadata":{"note":"\u0000"}}""",
    };

    /// <summary>Only a firmware can be registered; the server writes every other component from the code it runs.</summary>
    [Theory]
    [MemberData(nameof(Refused))]
    public async Task A_registration_that_is_not_a_clean_firmware_is_refused(string body)
    {
        var response = await (await AdminAsync()).PostAsync(Url, new StringContent(body, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(ErrorCodes.ValidationFailed, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Registering_a_version_twice_is_a_conflict()
    {
        var admin = await AdminAsync();
        var version = Fresh("fw-");
        Assert.Equal(HttpStatusCode.Created, (await admin.PostAsJsonAsync(Url, new { component = "lux_module_firmware", version })).StatusCode);

        var again = await admin.PostAsJsonAsync(Url, new { component = "lux_module_firmware", version, artifact_hash = new string('b', 64) });

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Contains(ErrorCodes.ArtifactVersionExists, await again.Content.ReadAsStringAsync());
    }

    /// <summary>A device reported the firmware first: its row has no hash and, being immutable, cannot gain one — 409, row unchanged.</summary>
    [Fact]
    public async Task Registering_a_version_a_device_already_reported_is_a_conflict_and_leaves_its_row()
    {
        var rig = await new LightingTestRig(fixture).RigAsync();
        var version = Fresh("fw-");
        await Heartbeat(rig.Node, version);

        var registered = await (await AdminAsync()).PostAsJsonAsync(Url, new { component = "node_firmware", version, artifact_hash = new string('d', 64) });

        Assert.Equal(HttpStatusCode.Conflict, registered.StatusCode);
        Assert.Contains(ErrorCodes.ArtifactVersionExists, await registered.Content.ReadAsStringAsync());
        var row = await fixture.QueryAsync(db => db.Set<ArtifactVersion>().AsNoTracking().SingleAsync(v => v.Version == version));
        Assert.Equal(((string?)null, (string?)null), (row.ArtifactHash, row.CreatedBy));
    }

    /// <summary>
    /// The task's question: which results came from this version. Every registered kind is listed, zero when unused; a firmware
    /// counts the devices running it and its history rows, split by data source where the row has one.
    /// </summary>
    [Fact]
    public async Task Usage_counts_the_devices_running_a_firmware_and_every_other_kind_at_zero()
    {
        var rig = await new LightingTestRig(fixture).RigAsync();
        var version = Fresh("fw-");
        await Heartbeat(rig.Node, version);
        var id = await IdAsync(ArtifactComponents.NodeFirmware, version);

        var usage = await (await AdminAsync()).GetFromJsonAsync<JsonElement>($"{Url}/{id}/usage");

        var results = usage.GetProperty("results").EnumerateArray().ToDictionary(r => r.GetProperty("result").GetString()!);
        Assert.Equal(1, results["iot_node"].GetProperty("count").GetInt64());
        Assert.Equal(1, results["iot_node"].GetProperty("by_data_source").EnumerateObject().Sum(p => p.Value.GetInt64()));
        Assert.Equal(1, results["iot_node_firmware_change"].GetProperty("count").GetInt64());
        foreach (var kind in new[] { "survey_run", "survey_frame", "detection", "luminance_baseline", "survey_pass", "pole_observation",
                     "luminance_history", "fault", "fault_cluster", "pole_photo", "citizen_photo" })
            Assert.Equal(0, results[kind].GetProperty("count").GetInt64());
        Assert.Equal(version, usage.GetProperty("version").GetProperty("version").GetString());
    }

    /// <summary>
    /// A fault from a pole photo names "{model}+{rules}": it counts under the model — but a different model whose version merely
    /// STARTS with the same text does not (the match is "the version, or the version and '+'", never a prefix).
    /// </summary>
    [Fact]
    public async Task A_composed_fault_version_counts_under_its_model_and_a_longer_lookalike_does_not()
    {
        var model = Fresh("m-");
        var id = await fixture.QueryAsync(async db =>
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO artifact_version (component, version, artifact_hash, metadata) VALUES ('cv_model', {model}, {new string('c', 64)}, '{{}}'::jsonb)");
            return await db.Set<ArtifactVersion>().Where(v => v.Version == model).Select(v => v.VersionId).SingleAsync();
        });
        await PlantFaultsAsync(model, model + "+rules1", model + "x", model + "x+rules1");

        var usage = await (await AdminAsync()).GetFromJsonAsync<JsonElement>($"{Url}/{id}/usage");

        var fault = usage.GetProperty("results").EnumerateArray().Single(r => r.GetProperty("result").GetString() == "fault");
        Assert.Equal(2, fault.GetProperty("count").GetInt64());
        Assert.Equal(2, fault.GetProperty("by_data_source").GetProperty("simulated").GetInt64());
    }

    [Fact]
    public async Task An_unknown_version_is_404()
    {
        var response = await (await AdminAsync()).GetAsync($"{Url}/999999999/usage");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains(ErrorCodes.ArtifactVersionNotFound, await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("engineer", "SEED_ENGINEER_PASSWORD")]
    [InlineData("agency", "SEED_AGENCY_PASSWORD")]
    public async Task Only_the_system_admin_reaches_the_registry(string username, string passwordVariable)
    {
        var client = await fixture.SeededClientAsync(username, passwordVariable);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(Url)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(Url, new { component = "node_firmware", version = "1" })).StatusCode);
    }

    private Task<HttpClient> AdminAsync() => fixture.SeededClientAsync("admin", "SEED_ADMIN_PASSWORD");

    private Task<long> IdAsync(string component, string version) => fixture.QueryAsync(db =>
        db.Set<ArtifactVersion>().Where(v => v.Component == component && v.Version == version).Select(v => v.VersionId).SingleAsync());

    private Task Heartbeat(string node, string firmware) => fixture.Services.GetRequiredService<MqttLightingHandler>().HandleAsync(
        $"luxmap/v1/nodes/{node}/heartbeat",
        new ReadOnlySequence<byte>(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { schema_version = 1, uptime_s = 1, firmware_version = firmware }))),
        CancellationToken.None);

    private Task PlantFaultsAsync(params string[] versions) => fixture.QueryAsync(async db =>
    {
        using var system = db.EnterUnscopedSystemWriteBackdoor();
        db.AddRange(versions.Select(v => new Fault
        {
            CommuneId = fixture.CommuneId, Lat = 16, Lng = 108, FaultStatus = FaultStatus.Detected, Severity = Severity.Medium,
            FaultType = FaultType.LampOut, SourceChannel = SourceChannel.Cv, DataSource = DataSource.Simulated,
            DetectedAt = DateTime.UtcNow, DetectionModelVersion = v,
        }));
        return await db.SaveChangesAsync();
    });
}
