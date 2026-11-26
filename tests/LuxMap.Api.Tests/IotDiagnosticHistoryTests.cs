using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LuxMap.Modules.IOT;
using LuxMap.Modules.IOT.Entities;
using LuxMap.Persistence.Jobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LuxMap.Api.Tests;

[Collection(nameof(AssetDatabaseCollection))]
public sealed class IotDiagnosticHistoryTests(AssetImportFixture fixture)
{
    [Fact]
    public async Task Five_second_episodes_merge_changing_drop_recover_and_keep_history_after_detach()
    {
        var rig = await new LightingTestRig(fixture).RigAsync(issueSecret: false);
        var contexts = fixture.Services.GetRequiredService<JobContextFactory>();
        var origin = new DateTime(2026, 11, 19, 0, 1, 0, DateTimeKind.Utc);
        var profile = new NodeProfile {
            DeviceId = "P001", FeederId = rig.Feeders[0], NominalVoltage = 5,
            NominalCurrentMa = 100, NominalPowerMw = 500,
        };
        async Task Pass(int seconds, double? current, bool complete = true)
        {
            await using var db = contexts.Open(fixture.CommuneId);
            var state = await db.Set<IotDiagnosticState>().SingleOrDefaultAsync(s => s.NodeId == rig.Node);
            if (state is null) {
                state = new() { NodeId = rig.Node, CommuneId = fixture.CommuneId };
                db.Add(state);
            }
            var now = origin.AddSeconds(seconds);
            IotDiagnosticResult? result = current is null ? null : new(rig.Node, profile.FeederId,
                !complete ? "insufficient_samples" : current < 95 ? "anomaly" : "normal", new(now, "ON", 5, current.Value, current.Value * 5),
                current < 95 ? [new("current_drop", current, 100, current - 100, 5)] : []) {
                    ConnectionStatus = "online", ElectricalStatus = "fresh",
                    ElectricalWindow = complete
                        ? new("evaluated", 4, 4, 5, current, current * 5) { CurrentEvaluationAvailable = true }
                        : new("insufficient_samples", 3, 4, null, null, null),
                };
            await IotDiagnosticHistory.ObserveAsync(db, state, profile, result, now, default);
            await db.SaveChangesAsync();
        }
        await Pass(0, 90);
        await Pass(4, 80); // same five-second bucket: no second write
        await using (var check = contexts.Open(fixture.CommuneId)) {
            var row = await check.Set<IotDiagnosticEpisode>().SingleAsync(e => e.NodeId == rig.Node);
            Assert.Equal(origin, row.EndedAt);
            Assert.Contains("90", row.Anomaly);
        }
        await Pass(5, 80);
        await using (var check = contexts.Open(fixture.CommuneId)) {
            var row = await check.Set<IotDiagnosticEpisode>().SingleAsync(e => e.NodeId == rig.Node);
            Assert.Equal(origin.AddSeconds(5), row.EndedAt);
            Assert.Contains("80", row.Anomaly);
        }
        await Pass(60, 85);
        await Pass(120, 80);
        await using (var db = contexts.Open(fixture.CommuneId)) {
            var row = Assert.Single(await db.Set<IotDiagnosticEpisode>().Where(e => e.NodeId == rig.Node).ToListAsync());
            Assert.Equal(origin, row.StartedAt);
            Assert.Equal(origin.AddMinutes(2), row.EndedAt);
            Assert.Equal("active", row.Status);
            Assert.Contains("80", row.Anomaly);
        }
        await Pass(125, 100, complete: false);
        await Pass(130, 100, complete: false);
        await using (var check = contexts.Open(fixture.CommuneId)) {
            var active = await check.Set<IotDiagnosticEpisode>().SingleAsync(e => e.NodeId == rig.Node);
            Assert.Equal("active", active.Status);
            Assert.Null(active.ClosedAt);
        }
        await Pass(180, 100);
        await Pass(240, 90);
        await Pass(300, null); // upstream unavailable must not mark recovery
        await Pass(420, 85); // unknown gap: new episode, not continuous evidence
        await using (var db = contexts.Open(fixture.CommuneId)) {
            var rows = await db.Set<IotDiagnosticEpisode>().Where(e => e.NodeId == rig.Node).OrderBy(e => e.Id).ToListAsync();
            Assert.Equal(3, rows.Count);
            Assert.Equal("resolved", rows[0].Status);
            Assert.Equal(origin.AddMinutes(3), rows[0].ClosedAt);
            Assert.Equal("interrupted", rows[1].Status);
            Assert.Equal("active", rows[2].Status);
        }
        using var manager = await fixture.ManagerClientAsync();
        Assert.Equal(HttpStatusCode.NoContent, (await manager.DeleteAsync($"/api/v1/iot/nodes/{rig.Node}")).StatusCode);
        var path = $"/api/v1/iot/feeders/{rig.Feeders[0]}/diagnostics/history";
        var page = await manager.GetFromJsonAsync<JsonElement>(path + "?page=1&page_size=1");
        Assert.Equal(3, page.GetProperty("total").GetInt32());
        Assert.Equal("interrupted", Assert.Single(page.GetProperty("items").EnumerateArray()).GetProperty("status").GetString());
        var overlap = await manager.GetFromJsonAsync<JsonElement>(path + "?from=2026-11-19T00:02:00Z&to=2026-11-19T00:04:00Z");
        Assert.Equal(1, overlap.GetProperty("total").GetInt32());
        Assert.Equal("resolved", Assert.Single(overlap.GetProperty("items").EnumerateArray()).GetProperty("status").GetString());
        var empty = await manager.GetFromJsonAsync<JsonElement>(path + "?page=2147483647&page_size=200");
        Assert.Empty(empty.GetProperty("items").EnumerateArray());
        Assert.Equal(HttpStatusCode.BadRequest, (await manager.GetAsync(path + "?from=2026-11-19&to=2026-11-18")).StatusCode);
        using var foreign = await fixture.ForeignCommuneClientAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await foreign.GetAsync(path)).StatusCode);
        using var anonymous = fixture.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(path)).StatusCode);
    }
}
