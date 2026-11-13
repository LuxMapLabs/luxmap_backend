using System.Text.Json;
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

/// <summary>
/// <c>PUT /admin/communes/{id}/boundary</c> (COMMUNE-BOUNDARY): the outline, a GeoJSON <c>Polygon</c> or <c>MultiPolygon</c>
/// in EPSG:4326. The key is required; <c>"boundary": null</c> clears the outline.
/// </summary>
public sealed record UpdateCommuneBoundaryRequest
{
    /// <summary>A GeoJSON <c>Polygon</c> or <c>MultiPolygon</c> geometry object, or <c>null</c> to clear the outline.</summary>
    /// <remarks>A <see cref="JsonElement"/> so JSON <c>null</c> arrives as <c>ValueKind.Null</c> while a missing key is still a 400.</remarks>
    [JsonRequired]
    public JsonElement Boundary { get; init; }
}

/// <summary>
/// One commune as the administrator manages it. <c>center</c> is derived from <c>bbox</c>, never stored. The outline itself
/// is drawn from <c>GET /map/communes</c>; here <c>has_boundary</c> only says whether one is set.
/// </summary>
public sealed record CommuneItem(
    string CommuneId, string Name, CommuneBbox? Bbox, MapCenter? Center, bool HasBoundary, DateTime CreatedAt, DateTime UpdatedAt);
