using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Asp.Versioning;
using LuxMap.Modules.Telemetry.Entities;
using LuxMap.Persistence;
using LuxMap.Shared.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LuxMap.Modules.Telemetry.Mqtt;

/// <summary>What EMQX posts when a client connects (HTTP authentication, LC-12 M-2).</summary>
public sealed record BrokerAuthRequest(string? Clientid, string? Username, string? Password);

/// <summary>One ACL rule EMQX applies to THIS client for the session (EMQX ≥ 5.8). Unmatched ⇒ deny (broker <c>no_match</c>).</summary>
public sealed record BrokerAclRule(
    string Permission,
    string Action,
    string Topic,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int[]? Qos = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? Retain = null);

/// <summary>EMQX's expected answer: <c>allow</c> with the client's ACL, or <c>deny</c>. Always 200 — a 4xx/5xx means "ignore".</summary>
public sealed record BrokerAuthResult(
    string Result,
    bool IsSuperuser,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<BrokerAclRule>? Acl)
{
    public static BrokerAuthResult Deny { get; } = new("deny", false, null);
}

/// <summary>
/// The broker's callback: is this MQTT client who it says it is, and what may it touch? (LC-12 M-2, M-3)
/// </summary>
/// <remarks>
/// <para>
/// 🔴 The DATABASE is the only source of device secrets: the device's password is checked with the same
/// <see cref="DeviceSecret.Matches"/> as the HTTP <c>Device</c> scheme, against <c>iot_node.credential_hash</c>. Rotating a secret
/// (LC-2) therefore refuses the old one at the next connect; the open session is closed by <see cref="IMqttBrokerAdmin"/>.
/// </para>
/// <para>
/// The ACL travels in the answer, so it is written — and tested — here: a device may subscribe only to its own command filter
/// and reply topic, and publish only its own receipt / ack / heartbeat / telemetry (never retained) and status. Everything else
/// falls to the broker's <c>no_match = deny</c>.
/// </para>
/// <para>
/// Hidden from the OpenAPI spec: only the broker calls it. Authenticated by <see cref="BrokerAuth.Policy"/>, NEVER anonymous.
/// </para>
/// </remarks>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/internal/mqtt")]
[ApiExplorerSettings(IgnoreApi = true)]
[Authorize(Policy = BrokerAuth.Policy)]
public sealed class BrokerAuthController(LuxMapDbContext db, MqttOptions mqtt) : ControllerBase
{
    [HttpPost("auth")]
    public async Task<BrokerAuthResult> AuthenticateAsync([FromBody] BrokerAuthRequest request, CancellationToken ct)
    {
        if (request.Username is not { } username || request.Password is not { Length: > 0 and <= 128 } password
            || !string.Equals(request.Clientid, username, StringComparison.Ordinal))
        {
            // client_id = username = node_id: a client cannot borrow another's identity for its session.
            return BrokerAuthResult.Deny;
        }

        if (string.Equals(username, mqtt.BackendUsername, StringComparison.Ordinal))
        {
            return Same(password, mqtt.BackendPassword) ? new BrokerAuthResult("allow", false, BackendAcl()) : BrokerAuthResult.Deny;
        }

        if (!MqttTopics.IsSafeNodeId(username))
        {
            return BrokerAuthResult.Deny;
        }

        // Unfiltered, only to compare the secret — the same lookup as DeviceAuthenticationHandler.
        var hash = await db.Set<IotNode>().IgnoreQueryFilters().AsNoTracking()
            .Where(node => node.NodeId == username)
            .Select(node => node.CredentialHash)
            .FirstOrDefaultAsync(ct);

        return DeviceSecret.Matches(password, hash) && hash is not null
            ? new BrokerAuthResult("allow", false, DeviceAcl(username))
            : BrokerAuthResult.Deny;
    }

    /// <summary>M-3: exactly the device's own topics. Publishes are QoS 1 and not retained, except its LWT status.</summary>
    public static IReadOnlyList<BrokerAclRule> DeviceAcl(string nodeId) =>
    [
        new("allow", "subscribe", "eq " + MqttTopics.CommandFilter(nodeId)),
        new("allow", "subscribe", "eq " + MqttTopics.Of(nodeId, MqttTopics.Reply)),
        new("allow", "publish", MqttTopics.Of(nodeId, MqttTopics.Receipt), Retain: false),
        new("allow", "publish", MqttTopics.Of(nodeId, MqttTopics.Ack), Retain: false),
        new("allow", "publish", MqttTopics.Of(nodeId, MqttTopics.Heartbeat), Retain: false),
        new("allow", "publish", MqttTopics.Of(nodeId, MqttTopics.Telemetry), Retain: false),
        new("allow", "publish", MqttTopics.Of(nodeId, MqttTopics.Status)),
    ];

    /// <summary>The backend's business client: publishes commands and replies, listens to everything devices send. Not a superuser.</summary>
    public static IReadOnlyList<BrokerAclRule> BackendAcl() =>
    [
        new("allow", "publish", $"{MqttTopics.Root}/+/relays/+/command", Retain: false),
        new("allow", "publish", $"{MqttTopics.Root}/+/{MqttTopics.Reply}", Retain: false),
        new("allow", "subscribe", "eq " + $"{MqttTopics.Root}/+/{MqttTopics.Receipt}"),
        new("allow", "subscribe", "eq " + $"{MqttTopics.Root}/+/{MqttTopics.Ack}"),
        new("allow", "subscribe", "eq " + $"{MqttTopics.Root}/+/{MqttTopics.Heartbeat}"),
        new("allow", "subscribe", "eq " + $"{MqttTopics.Root}/+/{MqttTopics.Status}"),
        new("allow", "subscribe", "eq " + $"{MqttTopics.Root}/+/{MqttTopics.Telemetry}"),
    ];

    private static bool Same(string given, string? expected)
        => expected is not null && CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(given)), SHA256.HashData(Encoding.UTF8.GetBytes(expected)));
}
