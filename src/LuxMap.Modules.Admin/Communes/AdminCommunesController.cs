using Asp.Versioning;
using LuxMap.Shared.Authorization;
using LuxMap.Shared.Contracts.Errors;
using LuxMap.Shared.Contracts.Paging;
using LuxMap.Shared.Http;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace LuxMap.Modules.Admin.Communes;

/// <summary>
/// Communes and their map frames for the system admin (COMMUNE-BBOX). A user's map opens on the frames of their communes —
/// <c>GET /auth/me</c> → <c>communes[]</c>, <c>map_bounds</c>. Assigning people to communes is <c>/admin/users</c>.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/admin/communes")]
[ClientSurface(ClientSurface.Web)]
public sealed class AdminCommunesController(CommuneAdminService service) : ControllerBase
{
    /// <summary>Every commune, oldest first.</summary>
    [HttpGet]
    [Authorize(Policy = LuxMapPolicies.ManageCommunes)]
    public Task<PagedResult<CommuneItem>> List(PageQuery page, CancellationToken ct) => service.ListAsync(page.ToPageRequest(), ct);

    [HttpGet("{id}")]
    [Authorize(Policy = LuxMapPolicies.ManageCommunes)]
    [ProducesResponseType<CommuneItem>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status404NotFound)]
    public Task<CommuneItem> Detail(string id, CancellationToken ct) => service.GetAsync(id, ct);

    /// <summary>Creates a commune. <c>bbox</c> (EPSG:4326, <c>{min_lng, min_lat, max_lng, max_lat}</c>) is optional.</summary>
    [HttpPost]
    [Authorize(Policy = LuxMapPolicies.ManageCommunes)]
    [ProducesResponseType<CommuneItem>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CommuneItem>> Create([FromBody] CreateCommuneRequest request, CancellationToken ct)
        => StatusCode(StatusCodes.Status201Created, await service.CreateAsync(request, ct));

    /// <summary>
    /// Replaces the name and the frame. Both keys are required; <c>"bbox": null</c> clears the frame. Assets outside the frame
    /// are neither hidden nor refused — the frame only holds and centres the map.
    /// </summary>
    [HttpPut("{id}")]
    [Authorize(Policy = LuxMapPolicies.ManageCommunes)]
    [ProducesResponseType<CommuneItem>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status409Conflict)]
    public Task<CommuneItem> Update(string id, [FromBody] UpdateCommuneRequest request, CancellationToken ct)
        => service.UpdateAsync(id, request, ct);
}
