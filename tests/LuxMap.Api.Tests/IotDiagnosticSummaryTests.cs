using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LuxMap.Modules.IOT.Entities;
using LuxMap.Persistence.Jobs;
using Microsoft.Extensions.DependencyInjection;

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
