namespace LuxMap.Shared.Authorization;

/// <summary>
/// How the MQTT broker (EMQX) proves it is the broker when it calls back to authenticate a client (LIGHT-CTRL LC-12, M-2).
/// </summary>
/// <remarks>
/// Header <c>Authorization: Broker &lt;key&gt;</c>, the key shared through configuration (<c>Mqtt:CallbackKey</c>). Not the default
/// scheme; only endpoints naming <see cref="Policy"/> accept it — never <c>[AllowAnonymous]</c>, even for a machine caller.
/// No key configured ⇒ the scheme never authenticates anyone.
/// </remarks>
public static class BrokerAuth
{
    public const string Scheme = "Broker";

    public const string Policy = "BrokerOnly";

    public const string Claim = "mqtt_broker";
}
