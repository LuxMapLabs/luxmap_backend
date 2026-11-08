using Microsoft.Extensions.Configuration;

namespace LuxMap.Modules.Identity.Auth;

public sealed record GoogleLoginOptions
{
    public const string SectionName = "Google";
    public const string ClientIdsEnvironmentVariable = "GOOGLE_CLIENT_IDS";

    public string[] ClientIds { get; init; } = [];

    public static GoogleLoginOptions FromConfiguration(IConfiguration configuration)
    {
        // The CSV is a deployment convenience for the usual Google__ClientIds__N array keys.
        // An explicitly empty CSV disables the feature, even if indexed settings also exist.
        var csv = configuration[ClientIdsEnvironmentVariable];
        if (csv is not null)
        {
            var ids = csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            configuration = new ConfigurationBuilder().AddInMemoryCollection(ids.Select((id, index) =>
                new KeyValuePair<string, string?>($"{SectionName}:ClientIds:{index}", id))).Build();
        }

        var options = configuration.GetSection(SectionName).Get<GoogleLoginOptions>() ?? new();
        return options with
        {
            ClientIds = options.ClientIds.Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(id => id.Trim()).ToArray(),
        };
    }
}
