using System.Buffers;
using System.Text;
using System.Text.Json;
using LuxMap.Modules.Telemetry.Entities;
using LuxMap.Modules.Telemetry.Lighting;
using LuxMap.Modules.Telemetry.Mqtt;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LuxMap.Api.Tests;

/// <summary>
/// LC-12 — the MQTT adapter's logic (<see cref="MqttLightingHandler"/>) against the real database, no broker: what is published,
/// what a receipt / ack / heartbeat does, what gets a reply, and what is dropped.
/// </summary>
[Collection(nameof(AssetDatabaseCollection))]
public sealed class MqttChannelTests(AssetImportFixture fixture)
{
    private LightingTestRig Rigs { get; } = new(fixture);

    private MqttLightingHandler Handler => fixture.Services.GetRequiredService<MqttLightingHandler>();

    /// <summary>M-4/M-5: an open command is published every round, and publishing marks nothing — only the device's receipt does.</summary>
    [Fact]
    public async Task Open_commands_are_published_every_round_and_publishing_marks_nothing_delivered()
    {
        var rig = await Rigs.RigAsync();
        var commandId = await Rigs.PressAsync(await fixture.ManagerClientAsync(), rig.Feeders[0], "off");

        var first = Mine(await Handler.DispatchAsync(CancellationToken.None), rig.Node);
        var second = Mine(await Handler.DispatchAsync(CancellationToken.None), rig.Node);

        var message = Assert.Single(first);
        Assert.Equal($"luxmap/v1/nodes/{rig.Node}/relays/1/command", message.Topic);
        var body = JsonDocument.Parse(message.Payload).RootElement;
        Assert.Equal(["command_id", "expires_at", "mode", "relay_no", "schema_version", "seq"],
            body.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal).ToArray());
        Assert.Equal((1, commandId, "off", 1), (body.GetProperty("schema_version").GetInt32(), body.GetProperty("command_id").GetString(),
            body.GetProperty("mode").GetString(), body.GetProperty("relay_no").GetInt32()));
        Assert.Equal(first[0].Topic, Assert.Single(second).Topic);
        Assert.Equal("pending", await Rigs.StatusAsync(commandId));
    }

    [Fact]
    public async Task A_receipt_marks_the_command_delivered_once_and_only_from_its_own_device()
    {
        var rig = await Rigs.RigAsync();
        var other = await Rigs.RigAsync();
        var commandId = await Rigs.PressAsync(await fixture.ManagerClientAsync(), rig.Feeders[0], "off");
        var seq = await SeqAsync(commandId);

        await Send(other.Node, "receipt", new { schema_version = 1, command_id = commandId, seq });
        Assert.Equal("pending", await Rigs.StatusAsync(commandId));
        await Send(rig.Node, "receipt", new { schema_version = 1, command_id = commandId, seq = seq + 1 });
        Assert.Equal("pending", await Rigs.StatusAsync(commandId));

        await Send(rig.Node, "receipt", new { schema_version = 1, command_id = commandId, seq });
        await Send(rig.Node, "receipt", new { schema_version = 1, command_id = commandId, seq });

        Assert.Equal("delivered", await Rigs.StatusAsync(commandId));
        Assert.Equal(["delivered"], await Rigs.AuditActionsAsync("lighting_command", commandId));
    }

    /// <summary>M-5/M-7: the receipt can be lost — a valid ack still lands, and always gets a reply so the device stops resending.</summary>
    [Fact]
    public async Task An_ack_without_a_receipt_still_lands_and_is_answered()
    {
        var rig = await Rigs.RigAsync();
        var commandId = await Rigs.PressAsync(await fixture.ManagerClientAsync(), rig.Feeders[0], "off");
        var seq = await SeqAsync(commandId);

        var reply = await Send(rig.Node, "ack", new { schema_version = 1, command_id = commandId, seq, result = "applied", reported_mode = "off" });

        Assert.Equal($"luxmap/v1/nodes/{rig.Node}/reply", reply!.Topic);
        Assert.Equal("""{"schema_version":1,"command_id":"__ID__","status":"applied"}""".Replace("__ID__", commandId), Encoding.UTF8.GetString(reply.Payload));
        Assert.Equal("off", await Rigs.ModeAsync(rig.Feeders[0]));
        Assert.Equal(["delivered", "applied"], await Rigs.AuditActionsAsync("lighting_command", commandId));

        // The same report again (the reply was lost): same answer, nothing new.
        var again = await Send(rig.Node, "ack", new { schema_version = 1, command_id = commandId, seq, result = "applied", reported_mode = "off" });
        Assert.Equal(Encoding.UTF8.GetString(reply.Payload), Encoding.UTF8.GetString(again!.Payload));
    }

    [Fact]
    public async Task A_refused_ack_is_answered_with_its_code_and_a_closed_one_still_records_the_mode()
    {
        var rig = await Rigs.RigAsync();
        var manager = await fixture.ManagerClientAsync();
        var off = await Rigs.PressAsync(manager, rig.Feeders[0], "off");
        var offSeq = await SeqAsync(off);

        var invalid = await Send(rig.Node, "ack", new { schema_version = 1, command_id = off, seq = offSeq, result = "applied" });
        Assert.Equal("VALIDATION_FAILED", Json(invalid!).GetProperty("code").GetString());

        await Rigs.PressAsync(manager, rig.Feeders[0], "auto");
        var closed = Json((await Send(rig.Node, "ack", new { schema_version = 1, command_id = off, seq = offSeq, result = "applied", reported_mode = "off" }))!);

        Assert.Equal("COMMAND_CLOSED", closed.GetProperty("code").GetString());
        Assert.Equal("superseded", closed.GetProperty("status").GetString());
        Assert.True(closed.GetProperty("mode_recorded").GetBoolean());
    }

    [Fact]
    public async Task A_heartbeat_marks_the_channel_alive()
    {
        var rig = await Rigs.RigAsync();
        Assert.Null(await LastReportAsync(rig.Node));

        Assert.Null(await Send(rig.Node, "heartbeat", new { schema_version = 1, uptime_s = 12, firmware_version = "0.1.0" }));

        Assert.NotNull(await LastReportAsync(rig.Node));
    }

    /// <summary>Anything that is not a well-formed v1 message on a device's own topic is dropped, changes nothing and is not answered.</summary>
    [Theory]
    [InlineData("ack", """{"schema_version":2,"command_id":"__ID__","seq":__SEQ__,"result":"applied","reported_mode":"off"}""")]
    [InlineData("ack", """{"schema_version":1,"command_id":"__ID__","seq":__SEQ__,"result":0,"reported_mode":"off"}""")]
    [InlineData("ack", """not json""")]
    [InlineData("receipt", """{"command_id":"__ID__","seq":__SEQ__}""")]
    [InlineData("telemetry", """{"schema_version":1}""")]
    [InlineData("relays/1/command", """{"schema_version":1}""")]
    public async Task Junk_is_dropped_without_a_reply_or_a_change(string kind, string payload)
    {
        var rig = await Rigs.RigAsync();
        var commandId = await Rigs.PressAsync(await fixture.ManagerClientAsync(), rig.Feeders[0], "off");
        var body = payload.Replace("__ID__", commandId).Replace("__SEQ__", (await SeqAsync(commandId)).ToString());

        Assert.Null(await Handler.HandleAsync($"luxmap/v1/nodes/{rig.Node}/{kind}", Bytes(body), CancellationToken.None));
        Assert.Null(await Handler.HandleAsync($"luxmap/v1/nodes/{rig.Node}+/ack", Bytes(body), CancellationToken.None));
        Assert.Null(await Handler.HandleAsync($"luxmap/v1/nodes/{rig.Node}/ack", Bytes(new string(' ', 5000)), CancellationToken.None));

        Assert.Equal("pending", await Rigs.StatusAsync(commandId));
        Assert.Empty(await Rigs.AuditActionsAsync("lighting_command", commandId));
    }

    private async Task<MqttOutbound?> Send(string node, string kind, object body)
        => await Handler.HandleAsync($"luxmap/v1/nodes/{node}/{kind}", Bytes(JsonSerializer.Serialize(body)), CancellationToken.None);

    private static ReadOnlySequence<byte> Bytes(string text) => new(Encoding.UTF8.GetBytes(text));

    private static JsonElement Json(MqttOutbound message) => JsonDocument.Parse(message.Payload).RootElement.Clone();

    private static MqttOutbound[] Mine(IEnumerable<MqttOutbound> messages, string node)
        => [.. messages.Where(m => m.Topic.StartsWith($"luxmap/v1/nodes/{node}/", StringComparison.Ordinal))];

    private Task<long> SeqAsync(string commandId)
        => fixture.QueryAsync(db => db.Set<LightingCommand>().IgnoreQueryFilters().Where(c => c.CommandId == commandId).Select(c => c.Seq).SingleAsync());

    private Task<DateTime?> LastReportAsync(string node)
        => fixture.QueryAsync(db => db.Set<IotNode>().IgnoreQueryFilters().Where(n => n.NodeId == node).Select(n => n.LastReportAt).SingleAsync());
}
