using Asp.Versioning;
using LuxMap.Shared.Authorization;
using LuxMap.Shared.Contracts.Errors;
using LuxMap.Shared.Contracts.Paging;
using LuxMap.Shared.Http;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace LuxMap.Modules.Admin.Versions;

/// <summary>
/// The version registry (BE-34): which models, algorithms and firmware produced the system's results, and how many results each one
/// produced. System-wide, for the system admin.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/admin/artifact-versions")]
[ClientSurface(ClientSurface.Web)]
public sealed class AdminArtifactVersionsController(ArtifactVersionService service) : ControllerBase
{
    /// <summary>Every registered version, newest first; <c>component</c> narrows to one kind.</summary>
    [HttpGet]
    [Authorize(Policy = LuxMapPolicies.ManageSettings)]
    [ProducesResponseType<PagedResult<ArtifactVersionItem>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
    public Task<PagedResult<ArtifactVersionItem>> List([FromQuery] string? component, PageQuery page, CancellationToken ct)
        => service.ListAsync(component, page.ToPageRequest(), ct);

    /// <summary>Registers a firmware ahead of the devices that will report it, optionally with its binary's hash.</summary>
    [HttpPost]
    [Authorize(Policy = LuxMapPolicies.ManageSettings)]
    [ProducesResponseType<ArtifactVersionItem>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ArtifactVersionItem>> Register([FromBody] RegisterArtifactVersionRequest request, CancellationToken ct)
        => StatusCode(StatusCodes.Status201Created, await service.RegisterAsync(request, ct));

    /// <summary>How many results of each kind this version produced — the answer to "which results came from version X".</summary>
    [HttpGet("{id:long}/usage")]
    [Authorize(Policy = LuxMapPolicies.ManageSettings)]
    [ProducesResponseType<ArtifactVersionUsage>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status404NotFound)]
    public Task<ArtifactVersionUsage> Usage(long id, CancellationToken ct) => service.UsageAsync(id, ct);
}
