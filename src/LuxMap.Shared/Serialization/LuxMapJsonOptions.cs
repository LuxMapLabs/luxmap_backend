using System.Text.Json;
using System.Text.Json.Serialization;

namespace LuxMap.Shared.Serialization;

/// <summary>
/// The single source of truth for the Contract's JSON conventions (sections 0 and 5.5).
/// Every host — minimal API, MVC controllers, Hangfire jobs, tests — must go through
/// <see cref="Configure"/>.
/// </summary>
public static class LuxMapJsonOptions
{
    /// <summary>A ready-made, frozen instance for manual serialization outside the HTTP pipeline.</summary>
    public static JsonSerializerOptions Default { get; } = CreateDefault();

    public static JsonSerializerOptions CreateDefault()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        Configure(options);
        return options;
    }

    public static void Configure(JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        // Section 0: snake_case JSON.
        options.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
        options.DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower;
        options.PropertyNameCaseInsensitive = true;

        // Section 5.5: enums go out as lowercase strings, NEVER numbers — .NET int enums break the
        // front end, and the Contract calls this out by name.
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower));

        // Section 0: ISO 8601 UTC with a Z suffix. DateOnly (install_date, warranty_expiry,
        // night_of) already renders as YYYY-MM-DD by default, so it needs no converter.
        options.Converters.Add(new UtcDateTimeConverter());
        options.Converters.Add(new UtcDateTimeOffsetConverter());
    }
}
