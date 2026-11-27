using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LuxMap.Modules.Assets.Entities;
using LuxMap.Modules.Faults.Entities;
using LuxMap.Shared.Contracts.Enums;
using Microsoft.EntityFrameworkCore;

namespace LuxMap.Api.Tests;

public sealed partial class IotDiagnosticsTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Feeder_repair_uses_the_linked_fault_or_pole_and_marks_electrical_history(bool usePole)
    {
        var bridge = new ReportedPowerBridge {
            DiagnosticDevice = DiagnosticTelemetry([60d, 60d, 60d, 60d, 60d]),
            FeederHistory = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) {
                Content = JsonContent.Create(new { total = 0, items = Array.Empty<object>() }),
            }),
        };
        return WithSharedCabinetAsync(async (manager, rig, _, admin) =>
        {
            using var engineer = await fixture.FieldEngineerClientAsync();
            var me = await engineer.GetFromJsonAsync<JsonElement>("/api/v1/auth/me");
            using var configured = await admin.PutAsJsonAsync($"/api/v1/iot/nodes/{rig.First}", SourceBody(rig));
            Assert.Equal(HttpStatusCode.NoContent, configured.StatusCode);
            var feederRoute = $"/api/v1/iot/feeders/{rig.Feeder}";
            var diagnosticRoute = feederRoute + "/diagnostics";
            var historyRoute = feederRoute + "/electrical/history";
            Assert.Equal("anomaly", (await manager.GetFromJsonAsync<JsonElement>(diagnosticRoute)).GetProperty("status").GetString());

            var faultId = await fixture.QueryAsync(async db =>
            {
                using var system = db.EnterUnscopedSystemWriteBackdoor();
                var pole = await db.Set<Pole>().IgnoreQueryFilters().SingleAsync(x => x.PoleId == rig.Pole);
                var location = pole.Geom;
                Assert.NotNull(location);
                var fault = new Fault {
                    CommuneId = pole.CommuneId, FeederId = usePole ? null : rig.Feeder,
                    PoleId = usePole ? pole.PoleId : null, SegmentId = pole.SegmentId,
                    Lat = location.Y, Lng = location.X, FaultStatus = FaultStatus.Confirmed,
                    FaultType = FaultType.LampOut, Severity = Severity.Medium, SourceChannel = SourceChannel.Cv,
                    DataSource = DataSource.Simulated, DetectedAt = DateTime.UtcNow,
                };
                db.Add(fault);
                await db.SaveChangesAsync();
                return fault.FaultId;
            });
            using var created = await manager.PostAsJsonAsync("/api/v1/work-orders", new {
                task_kind = "repair", title = "Feeder repair", fault_ids = new[] { faultId },
                assigned_to = me.GetProperty("user_id").GetString(),
            });
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            var order = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("work_order_id").GetString();
            var orderRoute = $"/api/v1/work-orders/{order}";
            var repairRoute = orderRoute + "/repair";
            var start = new { action = "start" };
            var finish = new { action = "finish" };

            using var notStarted = await engineer.PostAsJsonAsync(repairRoute, start);
            Assert.Equal(HttpStatusCode.Conflict, notStarted.StatusCode);
            using var orderStarted = await engineer.PostAsJsonAsync(orderRoute + "/start", new { });
            Assert.Equal(HttpStatusCode.OK, orderStarted.StatusCode);
            using var denied = await manager.PostAsJsonAsync(repairRoute, start);
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

            var period = await Repair(start);
            Assert.Equal(rig.Feeder, period.GetProperty("feeder_id").GetString());
            Assert.Equal("repairing", period.GetProperty("status").GetString());
            Assert.Equal(period.GetProperty("id").GetInt64(), (await Repair(start)).GetProperty("id").GetInt64());
            var inventory = await manager.GetFromJsonAsync<JsonElement>($"/api/v1/assets/feeders/{rig.Feeder}");
            Assert.Equal("repairing", inventory.GetProperty("feeder").GetProperty("power_state").GetString());
            var diagnosis = await manager.GetFromJsonAsync<JsonElement>(diagnosticRoute);
            Assert.Equal("repairing", diagnosis.GetProperty("status").GetString());
            Assert.Empty(diagnosis.GetProperty("anomalies").EnumerateArray());
            var electrical = await manager.GetFromJsonAsync<JsonElement>(feederRoute + "/electrical");
            Assert.Equal("repairing", electrical.GetProperty("status").GetString());
            Assert.Equal(JsonValueKind.Null, (await History()).GetProperty("ended_at").ValueKind);

            var closed = await Repair(finish);
            Assert.Equal("finished", closed.GetProperty("status").GetString());
            var ended = closed.GetProperty("ended_at").GetDateTime();
            Assert.Equal(ended, (await Repair(finish)).GetProperty("ended_at").GetDateTime());
            Assert.Equal(ended, (await History()).GetProperty("ended_at").GetDateTime());
            Assert.Equal("no_data", (await manager.GetFromJsonAsync<JsonElement>(diagnosticRoute)).GetProperty("status").GetString());

            await Repair(start);
            using var cancelled = await manager.PostAsJsonAsync(orderRoute + "/cancel", new { note = "Close test repair" });
            Assert.Equal(HttpStatusCode.OK, cancelled.StatusCode);
            var history = await manager.GetFromJsonAsync<JsonElement>(historyRoute);
            Assert.Equal(2, history.GetProperty("repair_periods").GetArrayLength());
            Assert.All(history.GetProperty("repair_periods").EnumerateArray(),
                item => Assert.Equal(JsonValueKind.String, item.GetProperty("ended_at").ValueKind));

            async Task<JsonElement> Repair(object request)
            {
                using var response = await engineer.PostAsJsonAsync(repairRoute, request);
                Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
                return await response.Content.ReadFromJsonAsync<JsonElement>();
            }
            async Task<JsonElement> History()
            {
                var result = await manager.GetFromJsonAsync<JsonElement>(historyRoute);
                return Assert.Single(result.GetProperty("repair_periods").EnumerateArray());
            }
        }, bridge);
    }
}
