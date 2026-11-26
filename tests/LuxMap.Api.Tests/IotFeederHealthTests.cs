using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace LuxMap.Api.Tests;

public sealed partial class IotDiagnosticsTests
{
    [Fact]
    public Task Asset_feeders_report_health_separately_from_lamp_state_in_detail_and_list()
    {
        var bridge = new ReportedPowerBridge();
        return WithSharedCabinetAsync(async (client, rig, _) =>
        {
            var path = $"/api/v1/assets/feeders/{rig.Feeder}";
            await Check("unknown", "unknown");
            Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync($"/api/v1/iot/nodes/{rig.First}", SourceBody(rig))).StatusCode);
            foreach (var (current, voltage, expected) in new[] {
                (100.0, 5.0, "normal"), (94.0, 5.0, "warning"), (80.0, 5.0, "incident"),
                (100.0, 5.6, "warning"), (100.0, 6.0, "incident"), (0.0, 0.0, "incident") })
            {
                bridge.DiagnosticDevice = DiagnosticTelemetry([current, current, current, current, current], voltage);
                await Check(expected, "on");
            }
            bridge.DiagnosticDevice = DiagnosticTelemetry([0d], 0, "OFF");
            await Check("normal", "off");
            bridge.DiagnosticDevice = DiagnosticTelemetry([100d]);
            await Check("warning", "on"); // Do not call insufficient samples normal.
            bridge.DiagnosticDevice = DiagnosticTelemetry([100d], oldRtc: true);
            await Check("warning", "unknown"); // Fresh receipt proves contact, not fresh electricity.
            bridge.DiagnosticDevice = DiagnosticTelemetry([100d], oldRtc: true, staleDelivery: true);
            await Check("offline", "unknown");
            var ack = JsonSerializer.SerializeToNode(DiagnosticTelemetry([100d], oldRtc: true, staleDelivery: true))!;
            ack["lastCommandAckAt"] = JsonValue.Create(DateTimeOffset.UtcNow.AddMilliseconds(-20));
            bridge.DiagnosticDevice = ack;
            await Check("warning", "unknown"); // ACK restores online while electricity remains stale.
            bridge.DiagnosticDevice = new { deviceId = "probe", lastSeenAt = DateTimeOffset.UtcNow };
            await Check("warning", "unknown");
            foreach (var (code, expected) in new[] { ("microsd_write_failed", "warning"), ("ina226_unavailable", "incident") })
            {
                bridge.DiagnosticDevice = new { deviceId = "probe", sensorHealth = new {
                    receivedAt = DateTimeOffset.UtcNow, data = new { deviceId = "probe", sessionId = "health",
                        sequenceNumber = 1, firmwareVersion = "health-v1", codes = new[] { code } } } };
                await Check(expected, "unknown");
            }
            Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/v1/iot/nodes/{rig.First}")).StatusCode);
            await Check("unknown", "unknown");

            async Task Check(string expectedHealth, string expectedLamp)
            {
                var detail = (await client.GetFromJsonAsync<JsonElement>(path)).GetProperty("feeder");
                Assert.Equal(expectedHealth, detail.GetProperty("power_state").GetString());
                Assert.Equal(expectedLamp, detail.GetProperty("lamp_state").GetString());
                for (var page = 1; ; page++)
                {
                    var list = await client.GetFromJsonAsync<JsonElement>($"/api/v1/assets/feeders?page={page}&page_size=200");
                    var matches = list.GetProperty("items").EnumerateArray().Where(item => item.GetProperty("feeder_id").GetString() == rig.Feeder).ToArray();
                    if (matches.Length > 0)
                    {
                        Assert.Equal(expectedHealth, Assert.Single(matches).GetProperty("power_state").GetString());
                        Assert.Equal(expectedLamp, matches[0].GetProperty("lamp_state").GetString());
                        break;
                    }
                    Assert.True((long)page * 200 < list.GetProperty("total").GetInt32());
                }
            }
        }, bridge);
    }
}
