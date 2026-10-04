using Asp.Versioning;
using LuxMap.Shared.Authorization;
using LuxMap.Shared.Contracts.Paging;
using LuxMap.Shared.Http;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace LuxMap.Modules.WorkOrders;

/// <summary>
/// Photos of a reported fault (BE-41, drift EV-2): the report is created first with <c>POST /faults</c>, then its
/// photos follow here — a weak signal loses a photo, never the report. Lives beside work-order photos because it is
/// the same photo store (<c>repair_evidence</c>, one image pipeline, one <c>/evidence/{id}</c> image endpoint).
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/faults")]
public sealed class FaultPhotosController(WorkOrderEvidenceService evidence) : ControllerBase
{
    /// <summary>
    /// The engineer who REPORTED the fault adds a photo while it is open (always <c>observation</c>). JPEG by
    /// magic bytes; <c>client_op_id</c> makes a resend return the same photo (200) instead of a second one (201).
    /// </summary>
    [HttpPost("{id}/photos")]
    [Authorize(Policy = LuxMapPolicies.ReportFaults)]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(WorkOrderEvidenceService.MaxUploadBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = WorkOrderEvidenceService.MaxUploadBytes)]
    [ProducesResponseType(typeof(EvidenceItem), 201)]
    [ProducesResponseType(typeof(EvidenceItem), 200)]
    public async Task<ActionResult<EvidenceItem>> Upload(string id, IFormFile? file,
        [FromForm(Name = "captured_at")] string? capturedAt, [FromForm(Name = "lat")] string? lat,
        [FromForm(Name = "lng")] string? lng, [FromForm(Name = "client_op_id")] string? clientOpId, CancellationToken ct)
    {
        var (item, created) = await evidence.UploadToFaultAsync(id, file, capturedAt, lat, lng, clientOpId, ct);
        return created ? StatusCode(201, item) : Ok(item);
    }

    /// <summary>The fault's photos, oldest capture first, for everyone who may read the fault.</summary>
    [HttpGet("{id}/photos")]
    [Authorize(Policy = LuxMapPolicies.ReadFaults)]
    public Task<PagedResult<EvidenceItem>> Photos(string id, PageQuery page, CancellationToken ct)
        => evidence.ListForFaultAsync(id, page.ToPageRequest(), ct);
}
