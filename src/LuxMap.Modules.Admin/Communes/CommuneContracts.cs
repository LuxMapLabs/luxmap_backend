using System.Text.Json.Serialization;
using LuxMap.Shared.Contracts.Communes;

namespace LuxMap.Modules.Admin.Communes;

/// <summary><c>POST /admin/communes</c>: a new commune; <c>bbox</c> may be left out and set later.</summary>
public sealed record CreateCommuneRequest
{
    public string? Name { get; init; }

    public CommuneBbox? Bbox { get; init; }
}

/// <summary>
/// <c>PUT /admin/communes/{id}</c>: a full replacement. BOTH keys are required — <c>"bbox": null</c> clears the frame, while a
/// body without the key is a 400, so a form that predates the frame cannot clear it by leaving it out.
/// </summary>
public sealed record UpdateCommuneRequest
{
    [JsonRequired]
    public string? Name { get; init; }

    [JsonRequired]
    public CommuneBbox? Bbox { get; init; }
}

/// <summary>One commune as the administrator manages it. <c>center</c> is derived from <c>bbox</c>, never stored.</summary>
public sealed record CommuneItem(
    string CommuneId, string Name, CommuneBbox? Bbox, MapCenter? Center, DateTime CreatedAt, DateTime UpdatedAt);
