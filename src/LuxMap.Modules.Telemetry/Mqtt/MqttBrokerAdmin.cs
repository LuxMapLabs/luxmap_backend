using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Logging;

namespace LuxMap.Modules.Telemetry.Mqtt;

/// <summary>
/// LC-12 M-15: closes a device's open MQTT session after its secret is rotated or the device is deleted — the broker checks a
/// password only at connect, so without this the old session lives on until it reconnects.
/// </summary>
/// <remarks>BEST-EFFORT by decision: a failure is logged, never thrown — the write that triggered it is already committed.</remarks>
public interface IMqttBrokerAdmin
{
    Task DisconnectAsync(string nodeId, CancellationToken ct);
}

/// <summary>No management API configured (HTTP channel, or a broker without one): nothing to close.</summary>
public sealed class NoMqttBrokerAdmin : IMqttBrokerAdmin
{
    public Task DisconnectAsync(string nodeId, CancellationToken ct) => Task.CompletedTask;
}

/// <summary>EMQX 5 management API: <c>DELETE /api/v5/clients/{clientid}</c> with an API key (basic auth).</summary>
public sealed class EmqxBrokerAdmin(HttpClient http, MqttOptions mqtt, ILogger<EmqxBrokerAdmin> log) : IMqttBrokerAdmin
{
    public async Task DisconnectAsync(string nodeId, CancellationToken ct)
    {
        if (!MqttTopics.IsSafeNodeId(nodeId))
        {
            return;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Delete,
                new Uri(new Uri(mqtt.AdminUrl!), $"api/v5/clients/{Uri.EscapeDataString(nodeId)}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic",
                Convert.ToBase64String(Encoding.UTF8.GetBytes($"{mqtt.AdminApiKey}:{mqtt.AdminApiSecret}")));
            using var response = await http.SendAsync(request, ct);

            // 404 = not connected: nothing to close, which is fine.
            if (!response.IsSuccessStatusCode && response.StatusCode != System.Net.HttpStatusCode.NotFound)
            {
                log.LogWarning("Could not close the MQTT session of {NodeId}: broker answered {Status}.", nodeId, (int)response.StatusCode);
            }
        }
        catch (Exception failure) when (failure is HttpRequestException or TaskCanceledException)
        {
            log.LogWarning(failure, "Could not close the MQTT session of {NodeId}; it ends at its next reconnect.", nodeId);
        }
    }
}
