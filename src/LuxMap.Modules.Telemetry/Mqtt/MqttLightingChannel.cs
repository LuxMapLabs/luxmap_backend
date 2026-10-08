using LuxMap.Modules.Telemetry.Lighting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MQTTnet;
using MQTTnet.Formatter;
using MQTTnet.Protocol;

namespace LuxMap.Modules.Telemetry.Mqtt;

/// <summary>
/// The MQTT transport of the lighting channel (LC-12) — runs only when <c>Lighting:Channel = mqtt</c>. It moves bytes and nothing
/// else: every decision is in <see cref="MqttLightingHandler"/> and <see cref="LightingCommandService"/>.
/// </summary>
/// <remarks>
/// <para>
/// One client, id = <c>Mqtt:BackendUsername</c>, MQTT 3.1.1, clean session. On (re)connect it subscribes to every device's
/// receipt / ack / heartbeat (QoS 1) BEFORE it dispatches anything, so a report can never arrive unheard (Codex, M-6).
/// </para>
/// <para>
/// Every <c>Mqtt:ResendInterval</c> it publishes all open commands again (M-4). The database is the queue: a crash between a
/// press and its publish loses nothing — the next round publishes it. No database lock is held while talking to the broker.
/// </para>
/// </remarks>
public sealed class MqttLightingChannel(MqttOptions mqtt, MqttLightingHandler handler, ILogger<MqttLightingChannel> log) : BackgroundService
{
    private static readonly string[] Inbound = [MqttTopics.Receipt, MqttTopics.Ack, MqttTopics.Heartbeat];

    /// <summary>Per operation: a cancellation token alone does not bound connect / subscribe / publish in MQTTnet 5.</summary>
    private static readonly TimeSpan OperationTimeout = TimeSpan.FromSeconds(10);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var client = new MqttClientFactory().CreateMqttClient();
        client.ApplicationMessageReceivedAsync += async received =>
        {
            try
            {
                var reply = await handler.HandleAsync(received.ApplicationMessage.Topic, received.ApplicationMessage.Payload, stoppingToken);
                if (reply is not null)
                {
                    await PublishAsync(client, reply, stoppingToken);
                }
            }
            catch (Exception failure) when (failure is not OperationCanceledException)
            {
                // Never let one message break the client's receive loop; the device resends its ack until it gets a reply.
                log.LogError(failure, "MQTT message on {Topic} failed.", received.ApplicationMessage.Topic);
            }
        };

        var backoff = TimeSpan.FromSeconds(1);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!client.IsConnected)
                {
                    using (var connect = Bounded(stoppingToken))
                    {
                        var connected = await client.ConnectAsync(ClientOptions(), connect.Token);
                        if (connected.ResultCode != MqttClientConnectResultCode.Success)
                        {
                            // MQTTnet 5 returns a refused CONNECT instead of throwing.
                            throw new InvalidOperationException($"The broker refused the backend client: {connected.ResultCode}.");
                        }
                    }

                    var subscribe = new MqttClientSubscribeOptionsBuilder();
                    foreach (var kind in Inbound)
                    {
                        subscribe.WithTopicFilter($"{MqttTopics.Root}/+/{kind}", MqttQualityOfServiceLevel.AtLeastOnce);
                    }

                    using (var subscribing = Bounded(stoppingToken))
                    {
                        var granted = await client.SubscribeAsync(subscribe.Build(), subscribing.Token);

                        // Not listening means reports would vanish: dispatch nothing until every filter is granted (Codex review).
                        if (granted.Items.Any(item => item.ResultCode > MqttClientSubscribeResultCode.GrantedQoS2))
                        {
                            await client.DisconnectAsync(cancellationToken: CancellationToken.None);
                            throw new InvalidOperationException("The broker refused a subscription of the backend client.");
                        }
                    }

                    log.LogInformation("MQTT lighting channel connected to {Host}:{Port}.", mqtt.Host, mqtt.Port);
                    backoff = TimeSpan.FromSeconds(1);
                }

                foreach (var message in await handler.DispatchAsync(stoppingToken))
                {
                    await PublishAsync(client, message, stoppingToken);
                }

                await Task.Delay(mqtt.ResendInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception failure)
            {
                log.LogWarning(failure, "MQTT lighting channel: broker unavailable, retrying in {Delay}.", backoff);
                await Task.Delay(backoff + TimeSpan.FromMilliseconds(Random.Shared.Next(0, 500)), stoppingToken);
                backoff = TimeSpan.FromSeconds(Math.Min(backoff.TotalSeconds * 2, 30));
            }
        }

        if (client.IsConnected)
        {
            await client.DisconnectAsync(cancellationToken: CancellationToken.None);
        }
    }

    private MqttClientOptions ClientOptions()
    {
        var builder = new MqttClientOptionsBuilder()
            .WithTcpServer(mqtt.Host, mqtt.Port)
            .WithClientId(mqtt.BackendUsername)
            .WithCredentials(mqtt.BackendUsername, mqtt.BackendPassword)
            .WithCleanSession(true)
            .WithKeepAlivePeriod(TimeSpan.FromSeconds(30))
            .WithProtocolVersion(MqttProtocolVersion.V311);

        if (mqtt.UseTls)
        {
            builder.WithTlsOptions(tls => tls.UseTls());
        }

        return builder.Build();
    }

    private static async Task PublishAsync(IMqttClient client, MqttOutbound message, CancellationToken ct)
    {
        using var bounded = Bounded(ct);
        await client.PublishAsync(new MqttApplicationMessageBuilder()
            .WithTopic(message.Topic)
            .WithPayload(message.Payload)
            .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
            .WithRetainFlag(false)
            .Build(), bounded.Token);
    }

    private static CancellationTokenSource Bounded(CancellationToken ct)
    {
        var source = CancellationTokenSource.CreateLinkedTokenSource(ct);
        source.CancelAfter(OperationTimeout);
        return source;
    }
}
