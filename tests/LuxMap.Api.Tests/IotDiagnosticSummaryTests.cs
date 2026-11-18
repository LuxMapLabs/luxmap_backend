using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LuxMap.Modules.IOT.Entities;
using LuxMap.Persistence.Jobs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;

namespace LuxMap.Api.Tests;

[Collection(nameof(AssetDatabaseCollection))]
public sealed class IotDiagnosticSummaryTests(AssetImportFixture fixture)
{
    [Fact]
    public async Task Summary_is_paged_scoped_and_marks_expired_snapshots_unavailable()
    {
        var rig = await new LightingTestRig(fixture).RigAsync(issueSecret: false);
        var contexts = fixture.Services.GetRequiredService<JobContextFactory>();
        await using (var db = contexts.Open(fixture.CommuneId))
        {
            db.Add(new IotDiagnosticState
            {
                NodeId = rig.Node, CommuneId = fixture.CommuneId, FeederId = rig.Feeders[0],
                DiagnosticStatus = "normal", EvaluatedAt = DateTime.UtcNow.AddMinutes(-3),
                Result = "null", Evidence = "{}",
            });
            await db.SaveChangesAsync();
        }
        using var manager = await fixture.ManagerClientAsync();
        using var response = await manager.GetAsync("/api/v1/iot/diagnostics/summary?page=1&page_size=200");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, json.GetProperty("page").GetInt32());
        Assert.Equal(200, json.GetProperty("page_size").GetInt32());
        var item = Assert.Single(json.GetProperty("items").EnumerateArray(),
            i => i.GetProperty("node_id").GetString() == rig.Node);
        Assert.Equal("source_unavailable", item.GetProperty("diagnostic_status").GetString());
        Assert.Empty(item.GetProperty("anomaly_codes").EnumerateArray());
        Assert.Equal("source_unavailable", item.GetProperty("connection_status").GetString());
        Assert.Equal("source_unavailable", item.GetProperty("electrical_status").GetString());

        await using (var db = contexts.Open(fixture.CommuneId))
        {
            var snapshot = await db.Set<IotDiagnosticState>().SingleAsync(state => state.NodeId == rig.Node);
            var now = DateTime.UtcNow;
            snapshot.DiagnosticStatus = "stale";
            snapshot.EvaluatedAt = now;
            snapshot.Result = JsonSerializer.Serialize(new LuxMap.Modules.IOT.IotDiagnosticResult(
                rig.Node, rig.Feeders[0], "stale", new(now.AddMinutes(-5), "ON", 5, 100, 500),
                [new("telemetry_stale")])
            {
                ConnectionStatus = "online", ElectricalStatus = "stale",
                LastSeenAt = now, LastCommandAckAt = now,
            }, LuxMap.Shared.Serialization.LuxMapJsonOptions.Default);
            await db.SaveChangesAsync();
        }
        var updated = await manager.GetFromJsonAsync<JsonElement>("/api/v1/iot/diagnostics/summary?page_size=200");
        var current = Assert.Single(updated.GetProperty("items").EnumerateArray(), row => row.GetProperty("node_id").GetString() == rig.Node);
        Assert.Equal("online", current.GetProperty("connection_status").GetString());
        Assert.Equal("stale", current.GetProperty("electrical_status").GetString());
        Assert.Equal("stale", current.GetProperty("diagnostic_status").GetString());
        Assert.Equal("telemetry_stale", Assert.Single(current.GetProperty("anomaly_codes").EnumerateArray()).GetString());

        using var foreign = await fixture.ForeignCommuneClientAsync();
        using var otherResponse = await foreign.GetAsync("/api/v1/iot/diagnostics/summary?page=1&page_size=200");
        var other = await otherResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.DoesNotContain(other.GetProperty("items").EnumerateArray(),
            i => i.GetProperty("node_id").GetString() == rig.Node);
        using var anonymous = fixture.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anonymous.GetAsync("/api/v1/iot/diagnostics/summary")).StatusCode);
    }
}
