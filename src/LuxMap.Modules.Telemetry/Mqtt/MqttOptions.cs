namespace LuxMap.Modules.Telemetry.Mqtt;

/// <summary>Configuration section <c>Mqtt</c> — the broker side of LC-12. Read only when <c>Lighting:Channel = mqtt</c>.</summary>
public sealed record MqttOptions
{
    public const string SectionName = "Mqtt";

    public string? Host { get; init; }

    public int Port { get; init; } = 1883;

    /// <summary>M-11: TLS (8883) in any deployment; plain 1883 only for a broker bound to loopback in development.</summary>
    public bool UseTls { get; init; }

    /// <summary>Also the backend's MQTT client id: the broker callback demands <c>client_id = username</c> for every client.</summary>
    public string BackendUsername { get; init; } = "luxmap-backend";

    public string? BackendPassword { get; init; }

    /// <summary>The key the broker sends in <c>Authorization: Broker &lt;key&gt;</c> when it calls back (M-2).</summary>
    public string? CallbackKey { get; init; }

    /// <summary>M-4: how often open commands are published again until they close.</summary>
    public TimeSpan ResendInterval { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>M-15: EMQX management API, to disconnect a device whose secret was rotated or which was deleted. Optional.</summary>
    public string? AdminUrl { get; init; }

    public string? AdminApiKey { get; init; }

    public string? AdminApiSecret { get; init; }

    public void Validate()
    {
        // The same limits the broker callback applies — a configuration the callback would refuse must not start (Codex review).
        if (string.IsNullOrWhiteSpace(Host) || Port is < 1 or > 65535 || BackendPassword is not { Length: > 0 and <= 128 }
            || string.IsNullOrWhiteSpace(CallbackKey) || CallbackKey.Length < 32 || ResendInterval <= TimeSpan.Zero
            || string.IsNullOrWhiteSpace(BackendUsername) || MqttTopics.IsSafeNodeId(BackendUsername) is false)
        {
            throw new InvalidOperationException(
                $"Lighting:Channel = mqtt needs {SectionName}:Host, a :BackendPassword of 1–128 characters, a :BackendUsername without "
                + "'/', '+', '#' or spaces, and a :CallbackKey of at least 32 characters.");
        }

        if ((AdminUrl is null) != (AdminApiKey is null) || (AdminApiKey is null) != (AdminApiSecret is null))
        {
            throw new InvalidOperationException($"{SectionName}:AdminUrl, :AdminApiKey and :AdminApiSecret go together.");
        }
    }
}
