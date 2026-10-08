namespace LuxMap.Modules.Telemetry.Lighting;

/// <summary>Configuration section <c>Lighting</c> (LIGHT-CTRL D-4).</summary>
public sealed record LightingOptions
{
    public const string SectionName = "Lighting";

    /// <summary>How long a command may wait for its device. Past this it is never delivered and never retried.</summary>
    public TimeSpan CommandTtl { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// LC-12, M-10: the ONE channel devices use — <c>http</c> (poll, LC-11) or <c>mqtt</c>. Never both: with <c>mqtt</c> the HTTP
    /// device endpoints answer 404.
    /// </summary>
    public string Channel { get; init; } = Http;

    public const string Http = "http";

    public const string Mqtt = "mqtt";

    public void Validate()
    {
        if (Channel is not (Http or Mqtt))
        {
            throw new InvalidOperationException($"{SectionName}:Channel must be '{Http}' or '{Mqtt}'; got '{Channel}'.");
        }

        if (CommandTtl <= TimeSpan.Zero)
        {
            throw new InvalidOperationException($"{SectionName}:CommandTtl must be greater than 0; got {CommandTtl}.");
        }
    }
}
