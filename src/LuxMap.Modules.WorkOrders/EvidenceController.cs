using Asp.Versioning;
using LuxMap.Shared.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LuxMap.Modules.WorkOrders;

/// <summary>
/// Work-order photos served through the API, never presigned (BE-11 rule 1, BE-24). Visible exactly when the
/// parent work order is; anything else is 404 <c>EVIDENCE_NOT_FOUND</c>. A missing object is 503.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/evidence")]
public sealed class EvidenceController(WorkOrderEvidenceService evidence) : ControllerBase
{
    [HttpGet("{evidence_id}/thumbnail")]
    [Authorize(Policy = LuxMapPolicies.ReadWorkOrders)]
    [Produces("image/jpeg")]
    public async Task<IActionResult> Thumbnail([FromRoute(Name = "evidence_id")] string evidenceId, CancellationToken ct)
        => File(await evidence.OpenAsync(evidenceId, thumbnail: true, ct), "image/jpeg");

    [HttpGet("{evidence_id}/original")]
    [Authorize(Policy = LuxMapPolicies.ReadWorkOrders)]
    [Produces("image/jpeg")]
    public async Task<IActionResult> Original([FromRoute(Name = "evidence_id")] string evidenceId, CancellationToken ct)
        => File(await evidence.OpenAsync(evidenceId, thumbnail: false, ct), "image/jpeg");
}
