using System.Text.Json;

namespace LuxMap.Modules.Admin.Versions;

/// <summary>
/// One row of the version registry. <c>artifact_hash</c> is null only for a firmware nobody registered with its binary;
/// <c>created_by</c> is null for every row the server wrote itself.
/// </summary>
public sealed record ArtifactVersionItem(
    long VersionId, string Component, string Version, string? ArtifactHash, JsonElement Metadata, DateTime CreatedAt, string? CreatedBy);

/// <summary>
/// <c>POST /admin/artifact-versions</c>: register a firmware ahead of the devices or the survey module that will report it — only
/// <c>node_firmware</c> and <c>lux_module_firmware</c>; every other component is written by the server from the code it runs.
/// </summary>
public sealed class RegisterArtifactVersionRequest
{
    public string? Component { get; init; }

    public string? Version { get; init; }

    /// <summary>SHA-256 of the firmware binary, lowercase hex; optional.</summary>
    public string? ArtifactHash { get; init; }

    /// <summary>Free notes as a JSON object (release date, board…); optional, at most 4 KB. Anything but an object is a 400.</summary>
    public Dictionary<string, JsonElement>? Metadata { get; init; }
}

/// <summary>
/// <c>GET /admin/artifact-versions/{id}/usage</c>: every result kind the system records, with how many of its rows this version
/// produced. Zero means "none", not "not counted". <c>by_data_source</c> is present for the kinds that carry a data source.
/// </summary>
public sealed record ArtifactVersionUsage(ArtifactVersionItem Version, IReadOnlyList<ResultCount> Results);
