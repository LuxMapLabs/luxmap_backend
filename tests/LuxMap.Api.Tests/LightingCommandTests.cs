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
using Microsoft.Extensions.DependencyInjection;
using NetTopologySuite.Geometries;

namespace LuxMap.Api.Tests;

/// <summary>
/// LIGHT-CTRL 2b — ON / OFF / AUTO on testbed devices: the press, delivery by poll, acknowledgement, expiry, supersession and
/// the execution order <c>seq</c> (drift LC-1…LC-11).
/// </summary>
/// <remarks>⚠️ SELF-SIGNED. Expectations are LITERALS.</remarks>
[Collection(nameof(AssetDatabaseCollection))]
public sealed class LightingCommandTests(AssetImportFixture fixture)
{
    private LightingTestRig Rigs { get; } = new(fixture);

    private const string Commands = "/api/v1/lighting/commands";
    private const string Preview = "/api/v1/lighting/preview";
    private const string Device = "/api/v1/device/commands";
    
    // ── The press ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_press_on_a_feeder_is_accepted_as_one_pending_command_and_audited_once()
    {
        var rig = await Rigs.RigAsync();
        var manager = await fixture.ManagerClientAsync();

        var response = await manager.PostAsJsonAsync(Commands, new { feeder_id = rig.Feeders[0], mode = "on", client_op_id = Guid.NewGuid() });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var body = await Rigs.JsonAsync(response);
        var command = Assert.Single(body.GetProperty("commands").EnumerateArray());
        Assert.Equal("pending", command.GetProperty("status").GetString());
        Assert.Equal(rig.Node, command.GetProperty("node_id").GetString());
        Assert.Equal(1, command.GetProperty("relay_no").GetInt32());
        Assert.Equal("on", command.GetProperty("requested_mode").GetString());
        Assert.Equal("calibration_rig", command.GetProperty("data_source").GetString());
        Assert.StartsWith("CMD-", command.GetProperty("command_id").GetString(), StringComparison.Ordinal);
        var created = command.GetProperty("created_at").GetDateTime();
        Assert.Equal(TimeSpan.FromSeconds(60), command.GetProperty("expires_at").GetDateTime() - created);

        var requestId = body.GetProperty("request_id").GetString()!;
        Assert.Equal(["requested"], await Rigs.AuditActionsAsync("lighting_request", requestId));
        Assert.Equal("on", await Rigs.ModeAsync(rig.Feeders[0]) ?? "<none>"); // the rig starts ON
    }

    /// <summary>
    /// A segment press switches what it can and LISTS the rest (D-8). Its feeder also lights another segment, which the press
    /// must name (I-14); poles without a feeder or on an excluded feeder are counted as uncontrollable.
    /// </summary>
    [Fact]
    public async Task A_segment_press_switches_what_it_can_names_the_rest_and_the_other_segments_it_lights()
    {
        var rig = await Rigs.RigAsync();
        var manager = await fixture.ManagerClientAsync();
        var unwired = await Rigs.NewFeederAsync(rig.Cabinet);
        var other = await Rigs.NewSegmentAsync();
        await Rigs.NewPoleAsync(rig.Segment, unwired);
        await Rigs.NewPoleAsync(rig.Segment, null);
        await Rigs.NewPoleAsync(other, rig.Feeders[0]);

        var preview = await Rigs.JsonAsync(await manager.GetAsync($"{Preview}?segment_id={rig.Segment}"));
        Assert.Equal("segment", preview.GetProperty("target_kind").GetString());
        Assert.Equal(rig.Segment, preview.GetProperty("target_id").GetString());
        Assert.Equal([(1, rig.Feeders[0]), (2, rig.Feeders[1])], Rigs.Targets(preview));
        var excluded = Assert.Single(preview.GetProperty("excluded").EnumerateArray());
        Assert.Equal(unwired, excluded.GetProperty("feeder_id").GetString());
        Assert.Equal("not_wired", excluded.GetProperty("reason").GetString());
        Assert.Equal(2, preview.GetProperty("uncontrollable_pole_count").GetInt32());
        Assert.Equal(Rigs.Ordered(rig.Segment, other), preview.GetProperty("affected_segment_ids").EnumerateArray().Select(s => s.GetString()!).ToArray());

        var press = await manager.PostAsJsonAsync(Commands, new { segment_id = rig.Segment, mode = "off", client_op_id = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.Accepted, press.StatusCode);
        var body = await Rigs.JsonAsync(press);
        Assert.Equal(2, body.GetProperty("commands").GetArrayLength());
        Assert.Equal("not_wired", Assert.Single(body.GetProperty("excluded").EnumerateArray()).GetProperty("reason").GetString());
        Assert.Equal(2, body.GetProperty("uncontrollable_pole_count").GetInt32());
    }

    [Fact]
    public async Task Nothing_switchable_is_a_409_that_says_why_and_writes_nothing()
    {
        var manager = await fixture.ManagerClientAsync();
        var noSecret = await Rigs.RigAsync(issueSecret: false);
        var notRemote = await Rigs.RigAsync(remote: false);

        foreach (var (rig, reason) in new[] { (noSecret, "no_credential"), (notRemote, "remote_control_unsupported") })
        {
            var response = await manager.PostAsJsonAsync(Commands, new { feeder_id = rig.Feeders[0], mode = "on", client_op_id = Guid.NewGuid() });
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            var error = (await Rigs.JsonAsync(response)).GetProperty("error");
            Assert.Equal("NO_CONTROLLABLE_RELAY", error.GetProperty("code").GetString());
            Assert.Equal(reason, error.GetProperty("details").GetProperty("excluded")[0].GetProperty("reason").GetString());
            Assert.Equal(0, await fixture.QueryAsync(db => db.Set<LightingCommand>().IgnoreQueryFilters().CountAsync(c => c.NodeId == rig.Node)));
        }
    }

    [Fact]
    public async Task The_same_key_replays_the_same_request_and_a_different_body_under_it_is_a_409()
    {
        var rig = await Rigs.RigAsync();
        var manager = await fixture.ManagerClientAsync();
        var key = Guid.NewGuid();

        var first = await manager.PostAsJsonAsync(Commands, new { feeder_id = rig.Feeders[0], mode = "on", client_op_id = key });
        var again = await manager.PostAsJsonAsync(Commands, new { feeder_id = rig.Feeders[0], mode = "on", client_op_id = key });
        var other = await manager.PostAsJsonAsync(Commands, new { feeder_id = rig.Feeders[0], mode = "off", client_op_id = key });

        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Equal((await Rigs.JsonAsync(first)).GetProperty("request_id").GetString(), (await Rigs.JsonAsync(again)).GetProperty("request_id").GetString());
        Assert.Equal(HttpStatusCode.Conflict, other.StatusCode);
        Assert.Equal("IDEMPOTENCY_CONFLICT", (await Rigs.JsonAsync(other)).GetProperty("error").GetProperty("code").GetString());
        Assert.Equal(1, await fixture.QueryAsync(db => db.Set<LightingCommand>().IgnoreQueryFilters().CountAsync(c => c.NodeId == rig.Node)));
    }

    [Fact]
    public async Task Only_a_manager_presses_and_every_role_reads_the_history()
    {
        var rig = await Rigs.RigAsync();
        var engineer = await fixture.FieldEngineerClientAsync();

        var press = await engineer.PostAsJsonAsync(Commands, new { feeder_id = rig.Feeders[0], mode = "on", client_op_id = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.Forbidden, press.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await engineer.GetAsync($"{Preview}?feeder_id={rig.Feeders[0]}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await engineer.GetAsync($"{Commands}?feeder_id={rig.Feeders[0]}")).StatusCode);
    }

    [Theory]
    [InlineData("""{ "mode": "on", "client_op_id": "8f0e3b8e-1f2a-4c55-9d3b-2b1f0d6a7c11" }""")]
    [InlineData("""{ "feeder_id": "FDR-001", "segment_id": "SEG-001", "mode": "on", "client_op_id": "8f0e3b8e-1f2a-4c55-9d3b-2b1f0d6a7c11" }""")]
    [InlineData("""{ "feeder_id": "FDR-001", "mode": "dim", "client_op_id": "8f0e3b8e-1f2a-4c55-9d3b-2b1f0d6a7c11" }""")]
    [InlineData("""{ "feeder_id": "FDR-001", "mode": "on" }""")]
    public async Task A_press_needs_exactly_one_target_a_real_mode_and_a_key(string body)
    {
        var manager = await fixture.ManagerClientAsync();
        var response = await manager.PostAsync(Commands, new StringContent(body, System.Text.Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ── The device loop ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Fetch → execute → acknowledge. A fetched command is fetched AGAIN until acknowledged (a lost response loses nothing) but
    /// only the first fetch is audited; the relay's mode is what the device REPORTS; a repeat of the same report changes
    /// nothing, a different one is 409.
    /// </summary>
    [Fact]
    public async Task The_device_fetches_executes_and_acknowledges_and_only_its_report_sets_the_mode()
    {
        var rig = await Rigs.RigAsync();
        var manager = await fixture.ManagerClientAsync();
        var commandId = await Rigs.PressAsync(manager, rig.Feeders[0], "off");
        var device = Rigs.DeviceClient(rig);

        var first = await Rigs.PollAsync(device);
        var second = await Rigs.PollAsync(device);
        var fetched = Assert.Single(first);
        Assert.Equal(commandId, fetched.CommandId);
        Assert.Equal("off", fetched.Mode);
        Assert.Equal(1, fetched.RelayNo);
        Assert.Equal([commandId], second.Select(c => c.CommandId).ToArray());
        Assert.Equal(["delivered"], await Rigs.AuditActionsAsync("lighting_command", commandId));
        Assert.Equal("on", await Rigs.ModeAsync(rig.Feeders[0]));

        var ack = new { seq = fetched.Seq, result = "applied", reported_mode = "off" };
        var applied = await device.PostAsJsonAsync($"{Device}/{commandId}/ack", ack);
        Assert.Equal(HttpStatusCode.OK, applied.StatusCode);
        Assert.Equal("applied", (await Rigs.JsonAsync(applied)).GetProperty("status").GetString());
        Assert.Equal("off", await Rigs.ModeAsync(rig.Feeders[0]));
        Assert.Equal(fetched.Seq, await Rigs.ModeSeqAsync(rig.Feeders[0]));

        Assert.Equal(HttpStatusCode.OK, (await device.PostAsJsonAsync($"{Device}/{commandId}/ack", ack)).StatusCode);
        var changed = await device.PostAsJsonAsync($"{Device}/{commandId}/ack", new { seq = fetched.Seq, result = "failed", error = "relay stuck" });
        Assert.Equal(HttpStatusCode.Conflict, changed.StatusCode);
        Assert.Equal(["delivered", "applied"], await Rigs.AuditActionsAsync("lighting_command", commandId));
        Assert.Empty(await Rigs.PollAsync(device));

        var listed = await Rigs.JsonAsync(await manager.GetAsync($"{Commands}?feeder_id={rig.Feeders[0]}"));
        Assert.Equal("applied", listed.GetProperty("items")[0].GetProperty("status").GetString());
        Assert.Equal("off", listed.GetProperty("items")[0].GetProperty("reported_mode").GetString());
    }

    [Fact]
    public async Task A_failed_report_keeps_the_mode_it_reports_and_never_the_requested_one()
    {
        var rig = await Rigs.RigAsync();
        var commandId = await Rigs.PressAsync(await fixture.ManagerClientAsync(), rig.Feeders[0], "off");
        var device = Rigs.DeviceClient(rig);
        var fetched = Assert.Single(await Rigs.PollAsync(device));

        var failed = await device.PostAsJsonAsync($"{Device}/{commandId}/ack",
            new { seq = fetched.Seq, result = "failed", reported_mode = "on", error = "contactor did not open" });

        Assert.Equal("failed", (await Rigs.JsonAsync(failed)).GetProperty("status").GetString());
        Assert.Equal("on", await Rigs.ModeAsync(rig.Feeders[0]));
        Assert.Equal(fetched.Seq, await Rigs.ModeSeqAsync(rig.Feeders[0]));
    }

    /// <summary>
    /// D-10: a newer press supersedes the open one on the same relay; the device is only handed the newest; a LATE report of
    /// the old command is kept (409 <c>COMMAND_CLOSED</c>) and sets the mode only while its seq is still the newest.
    /// </summary>
    [Fact]
    public async Task A_newer_press_supersedes_and_a_late_report_never_overwrites_a_newer_mode()
    {
        var rig = await Rigs.RigAsync();
        var manager = await fixture.ManagerClientAsync();
        var device = Rigs.DeviceClient(rig);

        var off = await Rigs.PressAsync(manager, rig.Feeders[0], "off");
        var offSeq = Assert.Single(await Rigs.PollAsync(device)).Seq;
        var auto = await Rigs.PressAsync(manager, rig.Feeders[0], "auto");
        Assert.Equal("superseded", await Rigs.StatusAsync(off));

        // The device executed `off` before it heard of `auto`: its report is still the truth about the relay.
        var late = await device.PostAsJsonAsync($"{Device}/{off}/ack", new { seq = offSeq, result = "applied", reported_mode = "off" });
        Assert.Equal(HttpStatusCode.Conflict, late.StatusCode);
        var error = (await Rigs.JsonAsync(late)).GetProperty("error");
        Assert.Equal("COMMAND_CLOSED", error.GetProperty("code").GetString());
        Assert.True(error.GetProperty("details").GetProperty("mode_recorded").GetBoolean());
        Assert.Equal("off", await Rigs.ModeAsync(rig.Feeders[0]));

        var fetched = Assert.Single(await Rigs.PollAsync(device));
        Assert.Equal(auto, fetched.CommandId);
        Assert.True(fetched.Seq > offSeq);
        await device.PostAsJsonAsync($"{Device}/{auto}/ack", new { seq = fetched.Seq, result = "applied", reported_mode = "auto" });
        Assert.Equal("auto", await Rigs.ModeAsync(rig.Feeders[0]));

        // The same late report again: kept, but seq is now older than the mode's — the mode stays `auto`.
        var again = await device.PostAsJsonAsync($"{Device}/{off}/ack", new { seq = offSeq, result = "applied", reported_mode = "off" });
        Assert.False((await Rigs.JsonAsync(again)).GetProperty("error").GetProperty("details").GetProperty("mode_recorded").GetBoolean());
        Assert.Equal("auto", await Rigs.ModeAsync(rig.Feeders[0]));
        // 3.5: superseding by a press is part of THAT press's one event (its snapshot lists what it replaced), not an event of
        // the old command.
        Assert.Equal(["delivered", "reported", "reported"], await Rigs.AuditActionsAsync("lighting_command", off));
        var autoRequest = await fixture.QueryAsync(db => db.Set<LightingCommand>().IgnoreQueryFilters()
            .Where(c => c.CommandId == auto).Select(c => c.RequestId.ToString()).SingleAsync());
        var press = await fixture.QueryAsync(db => db.Set<AuditEvent>().IgnoreQueryFilters().AsNoTracking()
            .Where(e => e.EntityType == AuditEntityType.LightingRequest && e.EntityId == autoRequest).Select(e => e.AfterState!).SingleAsync());
        Assert.Equal([off], JsonDocument.Parse(press).RootElement.GetProperty("superseded").EnumerateArray().Select(id => id.GetString()!).ToArray());
    }

    /// <summary>
    /// Past <c>expires_at</c> a command reads <c>expired</c> before any write stores it; the next locked write (here the poll)
    /// stores it as actor <c>system</c>; it is never delivered and never retried.
    /// </summary>
    [Fact]
    public async Task An_expired_command_reads_expired_is_stored_by_the_next_poll_and_is_never_delivered()
    {
        var rig = await Rigs.RigAsync();
        var manager = await fixture.ManagerClientAsync();
        var commandId = await Rigs.PressAsync(manager, rig.Feeders[0], "off");
        await Rigs.AgeAsync(commandId);

        var listed = await Rigs.JsonAsync(await manager.GetAsync($"{Commands}?status=expired&feeder_id={rig.Feeders[0]}"));
        Assert.Equal(commandId, Assert.Single(listed.GetProperty("items").EnumerateArray()).GetProperty("command_id").GetString());
        Assert.Equal("pending", await Rigs.StatusAsync(commandId));
        Assert.Equal(0, (await Rigs.JsonAsync(await manager.GetAsync($"{Commands}?status=pending&feeder_id={rig.Feeders[0]}"))).GetProperty("total").GetInt32());

        var device = Rigs.DeviceClient(rig);
        Assert.Empty(await Rigs.PollAsync(device));
        Assert.Equal("expired", await Rigs.StatusAsync(commandId));
        var expiry = await fixture.QueryAsync(db => db.Set<AuditEvent>().IgnoreQueryFilters()
            .SingleAsync(e => e.EntityId == commandId && e.Action == AuditAction.Expired));
        Assert.Equal(AuditActorKind.System, expiry.ActorKind);
        Assert.Null(expiry.ActorUserId);
    }

    /// <summary>Same commune is not the same device: every device query is narrowed to the authenticated node (3.2).</summary>
    [Fact]
    public async Task A_device_never_sees_or_acknowledges_another_devices_command_in_the_same_commune()
    {
        var mine = await Rigs.RigAsync();
        var theirs = await Rigs.RigAsync();
        var commandId = await Rigs.PressAsync(await fixture.ManagerClientAsync(), mine.Feeders[0], "off");
        var seq = Assert.Single(await Rigs.PollAsync(Rigs.DeviceClient(mine))).Seq;

        var other = Rigs.DeviceClient(theirs);
        Assert.Empty(await Rigs.PollAsync(other));
        var ack = await other.PostAsJsonAsync($"{Device}/{commandId}/ack", new { seq, result = "applied", reported_mode = "off" });
        Assert.Equal(HttpStatusCode.NotFound, ack.StatusCode);
        Assert.Equal("COMMAND_NOT_FOUND", (await Rigs.JsonAsync(ack)).GetProperty("error").GetProperty("code").GetString());
        Assert.Equal("delivered", await Rigs.StatusAsync(commandId));
    }

    [Fact]
    public async Task A_bearer_token_cannot_poll_and_a_poll_marks_the_channel_alive()
    {
        var rig = await Rigs.RigAsync();
        var manager = await fixture.ManagerClientAsync();
        Assert.Equal(HttpStatusCode.Unauthorized, (await manager.GetAsync(Device)).StatusCode);

        Assert.Null(await fixture.QueryAsync(db => db.Set<IotNode>().IgnoreQueryFilters().Where(n => n.NodeId == rig.Node).Select(n => n.LastReportAt).SingleAsync()));
        await Rigs.PollAsync(Rigs.DeviceClient(rig));
        Assert.NotNull(await fixture.QueryAsync(db => db.Set<IotNode>().IgnoreQueryFilters().Where(n => n.NodeId == rig.Node).Select(n => n.LastReportAt).SingleAsync()));
    }

    [Theory]
    [InlineData("applied", null, null, false)]   // applied without the mode it set
    [InlineData("applied", "on", null, false)]   // applied, but not the requested mode
    [InlineData("failed", "on", null, false)]    // failed without a reason
    [InlineData("applied", "off", null, true)]   // wrong seq
    public async Task A_report_that_does_not_add_up_is_a_400(string result, string? mode, string? error, bool wrongSeq)
    {
        var rig = await Rigs.RigAsync();
        var commandId = await Rigs.PressAsync(await fixture.ManagerClientAsync(), rig.Feeders[0], "off");
        var device = Rigs.DeviceClient(rig);
        var seq = Assert.Single(await Rigs.PollAsync(device)).Seq;

        var response = await device.PostAsJsonAsync($"{Device}/{commandId}/ack",
            new { seq = wrongSeq ? seq + 1 : seq, result, reported_mode = mode, error });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("delivered", await Rigs.StatusAsync(commandId));
    }

    [Fact]
    public async Task A_report_before_the_command_was_fetched_is_a_409()
    {
        var rig = await Rigs.RigAsync();
        var commandId = await Rigs.PressAsync(await fixture.ManagerClientAsync(), rig.Feeders[0], "off");
        var seq = await fixture.QueryAsync(db => db.Set<LightingCommand>().IgnoreQueryFilters().Where(c => c.CommandId == commandId).Select(c => c.Seq).SingleAsync());

        var response = await Rigs.DeviceClient(rig).PostAsJsonAsync($"{Device}/{commandId}/ack", new { seq, result = "applied", reported_mode = "off" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("COMMAND_NOT_DELIVERED", (await Rigs.JsonAsync(response)).GetProperty("error").GetProperty("code").GetString());
    }

    // ── With the registry ──────────────────────────────────────────────────────────────────────

    /// <summary>3.8: unwiring a relay supersedes its open command, as the Manager doing it — it must never switch a feeder later.</summary>
    [Fact]
    public async Task Unwiring_a_relay_supersedes_its_open_command_and_a_device_with_history_is_not_deleted()
    {
        var rig = await Rigs.RigAsync();
        var manager = await fixture.ManagerClientAsync();
        var commandId = await Rigs.PressAsync(manager, rig.Feeders[0], "off");
        Assert.Single(await Rigs.PollAsync(Rigs.DeviceClient(rig)));

        var unwire = await manager.PutAsJsonAsync($"/api/v1/assets/iot-nodes/{rig.Node}/relays/1", new { feeder_id = (string?)null });
        Assert.Equal(HttpStatusCode.NoContent, unwire.StatusCode);
        Assert.Equal("superseded", await Rigs.StatusAsync(commandId));
        var by = await fixture.QueryAsync(db => db.Set<AuditEvent>().IgnoreQueryFilters()
            .SingleAsync(e => e.EntityId == commandId && e.Action == AuditAction.Superseded));
        Assert.Equal(AuditActorKind.User, by.ActorKind);
        Assert.Empty(await Rigs.PollAsync(Rigs.DeviceClient(rig)));

        await manager.PutAsJsonAsync($"/api/v1/assets/iot-nodes/{rig.Node}/relays/2", new { feeder_id = (string?)null });
        var delete = await manager.DeleteAsync($"/api/v1/assets/iot-nodes/{rig.Node}");
        Assert.Equal(HttpStatusCode.Conflict, delete.StatusCode);
        Assert.Equal("ASSET_IN_USE", (await Rigs.JsonAsync(delete)).GetProperty("error").GetProperty("code").GetString());
    }

    /// <summary>
    /// Codex review P2: <c>mode_seq</c> lives on the wiring row, which unwiring deletes. Unwire + rewire the same feeder, and a
    /// late report of an OLD command must still not overwrite the newer mode — the command history remembers what came after.
    /// </summary>
    [Fact]
    public async Task Rewiring_a_relay_does_not_let_a_late_report_of_an_old_command_set_the_mode()
    {
        var rig = await Rigs.RigAsync();
        var manager = await fixture.ManagerClientAsync();
        var device = Rigs.DeviceClient(rig);
        var off = await Rigs.PressAsync(manager, rig.Feeders[0], "off");
        var offSeq = Assert.Single(await Rigs.PollAsync(device)).Seq;
        var auto = await Rigs.PressAsync(manager, rig.Feeders[0], "auto");
        var autoSeq = Assert.Single(await Rigs.PollAsync(device)).Seq;
        await device.PostAsJsonAsync($"{Device}/{auto}/ack", new { seq = autoSeq, result = "applied", reported_mode = "auto" });

        var relay = $"/api/v1/assets/iot-nodes/{rig.Node}/relays/1";
        Assert.Equal(HttpStatusCode.NoContent, (await manager.PutAsJsonAsync(relay, new { feeder_id = (string?)null })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await manager.PutAsJsonAsync(relay, new { feeder_id = rig.Feeders[0] })).StatusCode);
        Assert.Null(await Rigs.ModeSeqAsync(rig.Feeders[0]));

        var late = await device.PostAsJsonAsync($"{Device}/{off}/ack", new { seq = offSeq, result = "applied", reported_mode = "off" });

        Assert.Equal(HttpStatusCode.Conflict, late.StatusCode);
        Assert.False((await Rigs.JsonAsync(late)).GetProperty("error").GetProperty("details").GetProperty("mode_recorded").GetBoolean());
        Assert.Null(await Rigs.ModeAsync(rig.Feeders[0]));
    }

    /// <summary>
    /// Codex review P2 (D-R7): a device switched off for remote control is never handed a command — whether the flag went
    /// through the registry (which supersedes as the Manager) or changed some other way (the poll supersedes as system).
    /// </summary>
    [Fact]
    public async Task A_device_no_longer_remote_controlled_is_never_handed_a_command()
    {
        var rig = await Rigs.RigAsync();
        var manager = await fixture.ManagerClientAsync();
        var viaRegistry = await Rigs.PressAsync(manager, rig.Feeders[0], "off");

        var update = await manager.PutAsJsonAsync($"/api/v1/assets/iot-nodes/{rig.Node}",
            new { data_source = "calibration_rig", supports_remote_control = false });
        Assert.Equal(HttpStatusCode.NoContent, update.StatusCode);
        Assert.Equal("superseded", await Rigs.StatusAsync(viaRegistry));
        Assert.Equal(AuditActorKind.User, await ActorOfAsync(viaRegistry, AuditAction.Superseded));
        Assert.Empty(await Rigs.PollAsync(Rigs.DeviceClient(rig)));

        // Remote again, press, then the flag flips WITHOUT the registry: the poll itself refuses to deliver.
        await manager.PutAsJsonAsync($"/api/v1/assets/iot-nodes/{rig.Node}", new { data_source = "calibration_rig", supports_remote_control = true });
        var direct = await Rigs.PressAsync(manager, rig.Feeders[1], "off");
        await Rigs.AsSystemAsync(db => db.Database.ExecuteSqlAsync($"UPDATE iot_node SET supports_remote_control = false WHERE node_id = {rig.Node}"));

        Assert.Empty(await Rigs.PollAsync(Rigs.DeviceClient(rig)));
        Assert.Equal("superseded", await Rigs.StatusAsync(direct));
        Assert.Equal(AuditActorKind.System, await ActorOfAsync(direct, AuditAction.Superseded));
    }

    /// <summary>Codex review P2: the database itself refuses an `applied` command without the mode it reports (NULL = x is NULL).</summary>
    [Fact]
    public async Task The_database_refuses_an_applied_command_without_a_reported_mode()
    {
        var rig = await Rigs.RigAsync();
        var commandId = await Rigs.PressAsync(await fixture.ManagerClientAsync(), rig.Feeders[0], "off");

        var refused = await Assert.ThrowsAsync<Npgsql.PostgresException>(() => Rigs.AsSystemAsync(db => db.Database.ExecuteSqlAsync(
            $"UPDATE lighting_command SET status = 'applied', delivered_at = created_at, completed_at = created_at WHERE command_id = {commandId}")));

        Assert.Equal("23514", refused.SqlState);
        Assert.Equal("ck_lighting_command_status_columns", refused.ConstraintName);
    }

    // ── Concurrency ────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Codex review P3: the press and the poll TAKE the device lock — proven deterministically by holding that lock from another
    /// connection and watching each request wait, instead of hoping concurrent requests happen to overlap.
    /// </summary>
    [Fact]
    public async Task A_press_and_a_poll_wait_for_the_device_lock()
    {
        var rig = await Rigs.RigAsync();
        var manager = await fixture.ManagerClientAsync();

        await HoldingTheDeviceLockAsync(rig.Node, () => manager.PostAsJsonAsync(Commands,
            new { feeder_id = rig.Feeders[0], mode = "off", client_op_id = Guid.NewGuid() }), HttpStatusCode.Accepted);

        // A poll also UPDATEs last_report_at, which would wait on its own; a report time in the future makes it skip that, so
        // only the explicit lock is left to make it wait.
        await Rigs.AsSystemAsync(db => db.Database.ExecuteSqlAsync(
            $"UPDATE iot_node SET last_report_at = now() + interval '1 day' WHERE node_id = {rig.Node}"));
        await HoldingTheDeviceLockAsync(rig.Node, () => Rigs.DeviceClient(rig).GetAsync(Device), HttpStatusCode.OK);
    }

    /// <summary>Two presses on one relay at once: the device lock serialises them, so exactly one stays open — the later seq.</summary>
    [Fact]
    public async Task Two_presses_at_once_leave_exactly_one_open_command_the_newest()
    {
        var rig = await Rigs.RigAsync();
        var manager = await fixture.ManagerClientAsync();

        var responses = await Task.WhenAll(
            Enumerable.Range(0, 4).Select(_ => manager.PostAsJsonAsync(Commands,
                new { feeder_id = rig.Feeders[0], mode = "off", client_op_id = Guid.NewGuid() })));

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.Accepted, response.StatusCode));
        var rows = await fixture.QueryAsync(db => db.Set<LightingCommand>().IgnoreQueryFilters()
            .Where(c => c.NodeId == rig.Node).OrderBy(c => c.Seq).Select(c => c.Status).ToListAsync());
        Assert.Equal(4, rows.Count);
        Assert.Equal(LightingCommandStatus.Pending, rows[^1]);
        Assert.All(rows[..^1], status => Assert.Equal(LightingCommandStatus.Superseded, status));
    }

    [Fact]
    public async Task Two_polls_at_once_deliver_the_command_once()
    {
        var rig = await Rigs.RigAsync();
        var commandId = await Rigs.PressAsync(await fixture.ManagerClientAsync(), rig.Feeders[0], "off");
        var device = Rigs.DeviceClient(rig);

        var batches = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Rigs.PollAsync(device)));

        Assert.All(batches, batch => Assert.Equal([commandId], batch.Select(c => c.CommandId).ToArray()));
        Assert.Equal(["delivered"], await Rigs.AuditActionsAsync("lighting_command", commandId));
    }

    // ── Helpers ────────────────────────────────────────────────────────────────────────────────

    /// <summary>Holds the device row lock from a separate transaction; the request must still be waiting 500 ms in, then finish.</summary>
    /// <remarks>
    /// <c>FOR NO KEY UPDATE</c>, not <c>FOR UPDATE</c>: inserting a command checks its foreign key to <c>iot_node</c> with a
    /// KEY SHARE lock, which <c>FOR UPDATE</c> would block too — the test would then pass with the service's own lock removed
    /// (found by sabotage). <c>FOR NO KEY UPDATE</c> blocks the service's <c>FOR UPDATE</c> and nothing else.
    /// </remarks>
    private async Task HoldingTheDeviceLockAsync(string node, Func<Task<HttpResponseMessage>> send, HttpStatusCode expected)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LuxMapDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlAsync($"SELECT 1 FROM iot_node WHERE node_id = {node} FOR NO KEY UPDATE");

        var request = send();
        await Task.WhenAny(request, Task.Delay(TimeSpan.FromMilliseconds(500)));
        Assert.False(request.IsCompleted, "the request finished while the device row was locked — it never took the lock");

        await transaction.RollbackAsync();
        var response = await request;
        Assert.True(response.StatusCode == expected, await response.Content.ReadAsStringAsync());
    }

    private Task<AuditActorKind> ActorOfAsync(string commandId, AuditAction action)
        => fixture.QueryAsync(db => db.Set<AuditEvent>().IgnoreQueryFilters()
            .Where(e => e.EntityId == commandId && e.Action == action).Select(e => e.ActorKind).SingleAsync());

}
