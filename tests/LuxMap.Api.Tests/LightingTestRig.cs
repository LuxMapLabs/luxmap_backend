using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using LuxMap.Modules.Assets.Entities;
using LuxMap.Modules.Telemetry.Entities;
using LuxMap.Modules.Telemetry.Lighting;
using LuxMap.Persistence;
using LuxMap.Persistence.Audit;
using LuxMap.Shared.Contracts.Enums;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;

namespace LuxMap.Api.Tests;

/// <summary>
/// A testbed lighting rig for LIGHT-CTRL tests (HTTP poll and MQTT alike): a cabinet with one device whose relays 1, 2 switch two
/// feeders, a segment, a pole per feeder — and the helpers that press, poll and read state back.
/// </summary>
public sealed class LightingTestRig(AssetImportFixture fixture)
{
    public const string Commands = "/api/v1/lighting/commands";
    public const string Device = "/api/v1/device/commands";
    private const double Lng = 109.5;
    private const double Lat = 13.5;

    public sealed record Rig(string Cabinet, string Node, string Secret, string[] Feeders, string Segment);

    public sealed record Fetched(string CommandId, long Seq, int RelayNo, string Mode);

    /// <summary>A testbed cabinet with one device (relays 1, 2 → two feeders, both ON), a segment and one pole per feeder.</summary>
    public async Task<Rig> RigAsync(bool remote = true, bool issueSecret = true)
    {
        var manager = await fixture.ManagerClientAsync();
        var cabinet = await NewCabinetAsync();
        var segment = await NewSegmentAsync();
        var feeders = new[] { await NewFeederAsync(cabinet), await NewFeederAsync(cabinet) };
        foreach (var feeder in feeders)
        {
            await NewPoleAsync(segment, feeder);
        }

        var created = await manager.PostAsJsonAsync("/api/v1/assets/iot-nodes",
            new { cabinet_id = cabinet, data_source = "calibration_rig", supports_remote_control = remote });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var node = created.Headers.Location!.OriginalString.Split('/')[^1];
        for (var relay = 1; relay <= feeders.Length; relay++)
        {
            var wired = await manager.PutAsJsonAsync($"/api/v1/assets/iot-nodes/{node}/relays/{relay}", new { feeder_id = feeders[relay - 1] });
            Assert.Equal(HttpStatusCode.NoContent, wired.StatusCode);
        }

        await AsSystemAsync(async db =>
        {
            foreach (var control in await db.Set<FeederControl>().IgnoreQueryFilters().Where(c => c.NodeId == node).ToListAsync())
            {
                control.ControlMode = FeederControlMode.On;
                control.ModeReportedAt = DateTime.UtcNow;
            }

            return await db.SaveChangesAsync();
        });

        var secret = "";
        if (issueSecret)
        {
            var issued = await JsonAsync(await manager.PostAsync($"/api/v1/assets/iot-nodes/{node}/credential", null));
            secret = issued.GetProperty("secret").GetString()!;
        }

        return new Rig(cabinet, node, secret, feeders, segment);
    }

    public HttpClient DeviceClient(Rig rig)
    {
        var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Device", $"{rig.Node}.{rig.Secret}");
        return client;
    }

    public async Task<string> PressAsync(HttpClient manager, string feeder, string mode)
    {
        var response = await manager.PostAsJsonAsync(LightingTestRig.Commands, new { feeder_id = feeder, mode, client_op_id = Guid.NewGuid() });
        Assert.True(response.StatusCode == HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync());
        return Assert.Single((await JsonAsync(response)).GetProperty("commands").EnumerateArray()).GetProperty("command_id").GetString()!;
    }

    public async Task<Fetched[]> PollAsync(HttpClient device)
    {
        var response = await device.GetAsync(Device);
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var body = await JsonAsync(response);
        Assert.Equal(JsonValueKind.String, body.GetProperty("server_time").ValueKind);
        return [.. body.GetProperty("commands").EnumerateArray().Select(c => new Fetched(
            c.GetProperty("command_id").GetString()!, c.GetProperty("seq").GetInt64(), c.GetProperty("relay_no").GetInt32(), c.GetProperty("mode").GetString()!))];
    }

    public (int, string)[] Targets(JsonElement preview)
        => [.. preview.GetProperty("targets").EnumerateArray().Select(t => (t.GetProperty("relay_no").GetInt32(), t.GetProperty("feeder_id").GetString()!))];

    public string[] Ordered(params string[] ids) => [.. ids.OrderBy(id => id.Length).ThenBy(id => id, StringComparer.Ordinal)];

    public async Task<JsonElement> JsonAsync(HttpResponseMessage response)
        => JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    public Task<string?> ModeAsync(string feeder)
        => fixture.QueryAsync(db => db.Set<FeederControl>().IgnoreQueryFilters().AsNoTracking()
            .Where(c => c.FeederId == feeder).Select(c => c.ControlMode == null ? null : c.ControlMode.ToString()!.ToLowerInvariant()).SingleOrDefaultAsync());

    public Task<long?> ModeSeqAsync(string feeder)
        => fixture.QueryAsync(db => db.Set<FeederControl>().IgnoreQueryFilters().Where(c => c.FeederId == feeder).Select(c => c.ModeSeq).SingleAsync());

    public Task<string> StatusAsync(string commandId)
        => fixture.QueryAsync(async db => (await db.Set<LightingCommand>().IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(c => c.CommandId == commandId)).Status.ToString().ToLowerInvariant());

    public async Task<string[]> AuditActionsAsync(string entityType, string entityId)
    {
        var rows = await fixture.QueryAsync(db => db.Set<AuditEvent>().IgnoreQueryFilters().AsNoTracking()
            .Where(e => e.EntityId == entityId).OrderBy(e => e.AuditId).Select(e => new { e.EntityType, e.Action }).ToListAsync());
        Assert.All(rows, row => Assert.Equal(entityType, row.EntityType == AuditEntityType.LightingRequest ? "lighting_request" : "lighting_command"));
        return [.. rows.Select(row => row.Action.ToString().ToLowerInvariant())];
    }

    /// <summary>Moves a command's whole life into the past, so it is expired without waiting 60 s.</summary>
    public Task AgeAsync(string commandId)
        => AsSystemAsync(db => db.Database.ExecuteSqlAsync(
            $"UPDATE lighting_command SET created_at = created_at - interval '2 minutes', expires_at = expires_at - interval '2 minutes' WHERE command_id = {commandId}"));

    public Task<T> AsSystemAsync<T>(Func<LuxMapDbContext, Task<T>> write)
        => fixture.QueryAsync(async db =>
        {
            using (db.EnterUnscopedSystemWriteBackdoor())
            {
                return await write(db);
            }
        });

    public Task<string> NewCabinetAsync()
        => AsSystemAsync(async db =>
        {
            var cabinet = new ElectricalCabinet
            {
                CabinetName = "lighting rig cabinet",
                CommuneId = fixture.CommuneId,
                Geom = new Point(Lng, Lat) { SRID = 4326 },
                DataSource = DataSource.CalibrationRig,
            };
            db.Add(cabinet);
            await db.SaveChangesAsync();
            return cabinet.CabinetId;
        });

    public Task<string> NewFeederAsync(string cabinet)
        => AsSystemAsync(async db =>
        {
            var feeder = new Feeder
            {
                FeederName = "lighting rig feeder",
                CommuneId = fixture.CommuneId,
                CabinetId = cabinet,
                CabinetSource = TopologySource.Inferred,
            };
            db.Add(feeder);
            await db.SaveChangesAsync();
            return feeder.FeederId;
        });

    public Task<string> NewSegmentAsync()
        => AsSystemAsync(async db =>
        {
            var segment = new RoadSegment
            {
                SegmentName = "lighting rig road",
                RoadClass = RoadClass.InterVillage,
                LengthM = 100,
                Geom = new LineString([new Coordinate(Lng, Lat), new Coordinate(Lng + 0.001, Lat)]) { SRID = 4326 },
                CommuneId = fixture.CommuneId,
                DataSource = DataSource.CalibrationRig,
            };
            db.Add(segment);
            await db.SaveChangesAsync();
            return segment.SegmentId;
        });

    public Task<string> NewPoleAsync(string segment, string? feeder)
        => AsSystemAsync(async db =>
        {
            var pole = new Pole
            {
                SegmentId = segment,
                FeederId = feeder,
                FeederSource = feeder is null ? null : TopologySource.Inferred,
                CommuneId = fixture.CommuneId,
                Geom = new Point(Lng, Lat) { SRID = 4326 },
                DataSource = DataSource.CalibrationRig,
            };
            db.Add(pole);
            await db.SaveChangesAsync();
            return pole.PoleId;
        });
}
