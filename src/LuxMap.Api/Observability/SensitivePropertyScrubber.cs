using Serilog.Core;
using Serilog.Events;

namespace LuxMap.Api.Observability;

/// <summary>
/// Hard-blocks sensitive fields at the logging layer instead of trusting every call site to remember
/// not to pass them. Any property whose name matches the list below is replaced with <c>***</c>
/// before any sink sees it.
/// </summary>
public sealed class SensitivePropertyScrubber : ILogEventEnricher
{
    private const string Mask = "***";

    /// <summary>Matched as a case-insensitive substring — catches <c>Authorization</c>,
    /// <c>RequestHeaders.Authorization</c>, <c>DbPassword</c> and so on.</summary>
    private static readonly string[] SensitiveFragments =
    [
        "authorization",
        "token",
        "password",
        "pwd",
        "secret",
        "apikey",
        "api_key",
        "connectionstring",
        "connection_string",
        "cookie",
        "clientsecret",
    ];

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        ArgumentNullException.ThrowIfNull(logEvent);

        // Snapshot the names before mutating, so we never modify the collection we are iterating.
        var offenders = logEvent.Properties.Keys.Where(IsSensitive).ToArray();

        foreach (var name in offenders)
        {
            logEvent.AddOrUpdateProperty(new LogEventProperty(name, new ScalarValue(Mask)));
        }
    }

    public static bool IsSensitive(string propertyName)
        => SensitiveFragments.Any(fragment =>
            propertyName.Contains(fragment, StringComparison.OrdinalIgnoreCase));
}
