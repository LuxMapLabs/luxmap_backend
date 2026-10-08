namespace LuxMap.Modules.Telemetry.Lighting;

/// <summary>Configuration section <c>Lighting</c> (LIGHT-CTRL D-4).</summary>
public sealed record LightingOptions
{
    public const string SectionName = "Lighting";

    /// <summary>How long a command may wait for its device. Past this it is never delivered and never retried.</summary>
    public TimeSpan CommandTtl { get; init; } = TimeSpan.FromSeconds(60);

    public void Validate()
    {
        if (CommandTtl <= TimeSpan.Zero)
        {
            throw new InvalidOperationException($"{SectionName}:CommandTtl must be greater than 0; got {CommandTtl}.");
        }
    }
}
