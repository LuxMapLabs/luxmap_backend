using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Asp.Versioning;
using LuxMap.Modules.WorkOrders.Entities;
using LuxMap.Shared.Authorization;
using LuxMap.Shared.Contracts.Enums;
using LuxMap.Shared.Contracts.Paging;
using LuxMap.Shared.Http;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace LuxMap.Modules.WorkOrders;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/work-orders")]
public sealed class WorkOrdersController(WorkOrderService service, WorkOrderPoleService poles, WorkOrderEvidenceService evidence,
    WorkOrderAgendaService agenda, AutoDispatchQueries dispatch, WorkOrderAttachmentService attachments) : ControllerBase
{
    /// <summary>Read-only inspection schedule suggestions (WO-AUTODISPATCH-1, SELF-SIGNED).</summary>
    [HttpPost("auto-dispatch/preview")]
    [Authorize(Policy = LuxMapPolicies.ManageWorkOrders)]
    [ProducesResponseType(typeof(AutoDispatchPreview), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    [ProducesResponseType(403)]
    public Task<AutoDispatchPreview> AutoDispatchPreview(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] AutoDispatchRequest? request, CancellationToken ct)
        => dispatch.Preview(request, ct);

    [HttpGet]
    [Authorize(Policy = LuxMapPolicies.ReadWorkOrders)]
    public Task<PagedResult<WorkOrderItem>> List(
        [FromQuery(Name = "wo_status")] string? statuses,
        [FromQuery(Name = "task_kind")] string? kind,
        [FromQuery(Name = "assigned_to")] string? assigned,
        [FromQuery(Name = "segment_id")] string? segment,
        [FromQuery(Name = "commune_id")] string[]? communes,
        [FromQuery(Name = "scheduled_from")] DateOnly? from,
        [FromQuery(Name = "scheduled_to")] DateOnly? to,
        [FromQuery(Name = "case_id")] string? caseId,
        [FromQuery(Name = "cancellation_request_status")] string? cancellation,
        PageQuery page, CancellationToken ct)
        => service.List(statuses?.Split(',').Select(x => Wire<WorkOrderStatus>(x, "wo_status")).ToArray(),
            kind is null ? null : Wire<TaskKind>(kind, "task_kind"), assigned, segment, communes, from, to, page.ToPageRequest(), ct,
            string.IsNullOrWhiteSpace(caseId) ? null : caseId.Trim(),
            cancellation is null && !Request.Query.ContainsKey("cancellation_request_status") ? null : WireEnum.ParseCsv<CancellationRequestStatus>(cancellation, "cancellation_request_status") is [var value]
                ? value : throw OptionalJson.Invalid("cancellation_request_status"));

    /// <summary>
    /// What one field engineer can work on tonight, grouped by road — nearest road first when <c>near=lat,lng</c> is
    /// given (BE-25, SELF-SIGNED). A field engineer always gets their own; anyone else must name one in
    /// <c>assigned_to</c>. <c>night_of</c> defaults to the current night in the communes' time zone.
    /// </summary>
    [HttpGet("agenda")]
    [Authorize(Policy = LuxMapPolicies.ReadWorkOrders)]
    public Task<WorkOrderAgenda> Agenda([FromQuery(Name = "night_of")] DateOnly? nightOf,
        [FromQuery(Name = "near")] string? near, [FromQuery(Name = "assigned_to")] string? assigned, CancellationToken ct)
        => agenda.AgendaAsync(nightOf, near, assigned, ct);

    [HttpPost("batch")]
    [Authorize(Policy = LuxMapPolicies.ManageWorkOrders)]
    [ProducesResponseType(typeof(BatchCreateWorkOrdersResponse), 201)]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    [ProducesResponseType(403)]
    [ProducesResponseType(404)]
    [ProducesResponseType(409)]
    public async Task<ActionResult<BatchCreateWorkOrdersResponse>> CreateBatch(BatchCreateWorkOrdersRequest request, CancellationToken ct)
        => StatusCode(201, await service.CreateBatch(request, ct));

    [HttpGet("{id}")]
    [Authorize(Policy = LuxMapPolicies.ReadWorkOrders)]
    public Task<WorkOrderDetail> Detail(string id, CancellationToken ct) => service.Detail(id, ct);

    /// <summary>
    /// The poles on the order's segments with the status the latest accepted survey left on each lamp, in
    /// order along the road — what the engineer is walking into, known BEFORE the visit (drift WO-12).
    /// </summary>
    [HttpGet("{id}/poles")]
    [Authorize(Policy = LuxMapPolicies.ReadWorkOrders)]
    public Task<PagedResult<WorkOrderPole>> Poles(string id, PageQuery page, CancellationToken ct)
        => poles.PolesAsync(id, page.ToPageRequest(), ct);

    /// <summary>
    /// The assigned engineer adds a photo while the order is in progress (BE-24): <c>before</c>/<c>after</c> on a
    /// repair, <c>observation</c> on every other kind. JPEG decided by magic bytes; <c>client_op_id</c> makes a retry
    /// return the same photo (200) instead of a second one (201).
    /// </summary>
    [HttpPost("{id}/evidence")]
    [Authorize(Policy = LuxMapPolicies.ExecuteWorkOrders)]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(WorkOrderEvidenceService.MaxRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = WorkOrderEvidenceService.MaxRequestBytes)]
    [UploadBodyLimit(WorkOrderEvidenceService.MaxRequestBytes)]
    [ProducesResponseType(typeof(EvidenceItem), 201)]
    [ProducesResponseType(typeof(EvidenceItem), 200)]
    [ProducesResponseType(typeof(LuxMap.Shared.Contracts.Errors.ApiErrorResponse), 400)]
    [ProducesResponseType(typeof(LuxMap.Shared.Contracts.Errors.ApiErrorResponse), 404)]
    [ProducesResponseType(typeof(LuxMap.Shared.Contracts.Errors.ApiErrorResponse), 409)]
    [ProducesResponseType(typeof(LuxMap.Shared.Contracts.Errors.ApiErrorResponse), 413)]
    [ProducesResponseType(typeof(LuxMap.Shared.Contracts.Errors.ApiErrorResponse), 415)]
    public async Task<ActionResult<EvidenceItem>> UploadEvidence(string id, [Required] IFormFile? file,
        [Required, FromForm(Name = "kind")] string? kind, [Required, FromForm(Name = "captured_at")] string? capturedAt,
        [Required, FromForm(Name = "lat")] string? lat, [Required, FromForm(Name = "lng")] string? lng,
        [FromForm(Name = "client_op_id")] string? clientOpId, CancellationToken ct)
    {
        var (item, created) = await evidence.UploadAsync(id, file, kind, capturedAt, lat, lng, clientOpId, ct);
        return created ? StatusCode(201, item) : Ok(item);
    }

    /// <summary>The order's photos, oldest capture first, with API paths to each image (BE-24).</summary>
    [HttpGet("{id}/evidence")]
    [Authorize(Policy = LuxMapPolicies.ReadWorkOrders)]
    public Task<PagedResult<EvidenceItem>> Evidence(string id, PageQuery page, CancellationToken ct)
        => evidence.ListAsync(id, page.ToPageRequest(), ct);

    /// <summary>
    /// The manager attaches one PDF (the QR labels to print) to a pole_gps_update order while it can still be edited — one file per
    /// request (drift WO-POLE-GPS PG-6). PDF decided by magic bytes; at most 20 MiB and 10 active files.
    /// </summary>
    [HttpPost("{id}/attachments")]
    [Authorize(Policy = LuxMapPolicies.ManageWorkOrders)]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(WorkOrderAttachmentService.MaxRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = WorkOrderAttachmentService.MaxRequestBytes)]
    [ProducesResponseType(typeof(WorkOrderAttachmentItem), 201)]
    public async Task<ActionResult<WorkOrderAttachmentItem>> UploadAttachment(string id, IFormFile? file, CancellationToken ct)
    {
        var item = await attachments.UploadAsync(id, file, ct);
        return Created(item.DownloadUrl, item);
    }

    /// <summary>The PDF bytes, visible exactly when the order is (PG-7). Through the API, never presigned.</summary>
    [HttpGet("{id}/attachments/{attachmentId}")]
    [Authorize(Policy = LuxMapPolicies.ReadWorkOrders)]
    [ProducesResponseType(typeof(FileResult), 200, WorkOrderAttachmentService.PdfContentType)]
    public async Task<IActionResult> Attachment(string id, string attachmentId, CancellationToken ct)
    {
        var (content, fileName) = await attachments.OpenAsync(id, attachmentId, ct);
        Response.Headers.XContentTypeOptions = "nosniff";
        return File(content, WorkOrderAttachmentService.PdfContentType, fileName);
    }

    /// <summary>Soft removal while the order can still be edited (PG-8).</summary>
    [HttpDelete("{id}/attachments/{attachmentId}")]
    [Authorize(Policy = LuxMapPolicies.ManageWorkOrders)]
    [ProducesResponseType(204)]
    public async Task<IActionResult> RemoveAttachment(string id, string attachmentId, CancellationToken ct)
    {
        await attachments.RemoveAsync(id, attachmentId, ct);
        return NoContent();
    }

    [HttpGet("assignees")]
    [Authorize(Policy = LuxMapPolicies.ManageWorkOrders)]
    public Task<PagedResult<WorkOrderAssignee>> Assignees([FromQuery(Name = "commune_id")] string[]? communes,
        PageQuery page, CancellationToken ct)
    {
        if (communes is not { Length: 1 } || string.IsNullOrWhiteSpace(communes[0])) throw OptionalJson.Invalid("commune_id");
        return service.Assignees(communes[0], PageRequest.Create(page.Page), ct);
    }

    [HttpPost]
    [ProducesResponseType(typeof(WorkOrderDetail), 201)]
    [Authorize(Policy = LuxMapPolicies.ManageWorkOrders)]
    public async Task<ActionResult<WorkOrderDetail>> Create(CreateWorkOrderRequest request, CancellationToken ct)
    {
        var result = await service.Create(request, ct);
        return Created($"/api/v1/work-orders/{result.WorkOrderId}", result);
    }

    [HttpPatch("{id}")]
    [Authorize(Policy = LuxMapPolicies.ManageWorkOrders)]
    public Task<WorkOrderDetail> Patch(string id, PatchWorkOrderRequest request, CancellationToken ct) => service.Patch(id, request, ct);

    [HttpPut("{id}/assignee")]
    [Authorize(Policy = LuxMapPolicies.ManageWorkOrders)]
    public Task<WorkOrderDetail> Assign(string id, AssignWorkOrderRequest request, CancellationToken ct) => service.Assign(id, request, ct);

    [HttpPost("{id}/request-cancellation")]
    [Authorize(Policy = LuxMapPolicies.ExecuteWorkOrders)]
    public Task<WorkOrderDetail> RequestCancellation(string id, RequestCancellationRequest request, CancellationToken ct)
        => service.RequestCancellation(id, request, ct);

    [HttpPost("{id}/reject-cancellation")]
    [Authorize(Policy = LuxMapPolicies.ManageWorkOrders)]
    public Task<WorkOrderDetail> RejectCancellation(string id, RejectCancellationRequest request, CancellationToken ct)
        => service.RejectCancellation(id, request, ct);

    [HttpPost("{id}/start")]
    [Authorize(Policy = LuxMapPolicies.ExecuteWorkOrders)]
    public Task<WorkOrderDetail> Start(string id,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] StartWorkOrderRequest? request, CancellationToken ct)
        => service.Act(id, "start", null, default, ct, performedAt: request?.PerformedAt);

    [HttpPost("{id}/complete")]
    [Authorize(Policy = LuxMapPolicies.ExecuteWorkOrders)]
    public Task<WorkOrderDetail> Complete(string id, CompleteWorkOrderRequest request, CancellationToken ct)
        => service.Act(id, "complete", request.ReportNote, request.FaultOutcomes, ct, request.MaterialsUsed, request.PerformedAt,
            request.PoleLocations);

    [HttpPost("{id}/verify")]
    [Authorize(Policy = LuxMapPolicies.ManageWorkOrders)]
    public Task<WorkOrderDetail> Verify(string id, ReviewWorkOrderRequest request, CancellationToken ct)
        => service.Act(id, "verify", request.Note, default, ct);

    [HttpPost("{id}/return")]
    [Authorize(Policy = LuxMapPolicies.ManageWorkOrders)]
    public Task<WorkOrderDetail> Return(string id, ReviewWorkOrderRequest request, CancellationToken ct)
        => service.Act(id, "return", request.Note, default, ct);

    [HttpPost("{id}/follow-up")]
    [ProducesResponseType(typeof(WorkOrderDetail), 201)]
    [Authorize(Policy = LuxMapPolicies.ManageWorkOrders)]
    public async Task<ActionResult<WorkOrderDetail>> FollowUp(string id, FollowUpWorkOrderRequest request, CancellationToken ct)
    {
        var result = await service.FollowUp(id, request, ct);
        return Created($"/api/v1/work-orders/{result.WorkOrderId}", result);
    }

    [HttpPost("{id}/cancel")]
    [Authorize(Policy = LuxMapPolicies.ManageWorkOrders)]
    public Task<WorkOrderDetail> Cancel(string id, ReviewWorkOrderRequest request, CancellationToken ct)
        => service.Act(id, "cancel", request.Note, default, ct);

    private static T Wire<T>(string value, string field) where T : struct, Enum
    {
        foreach (var candidate in Enum.GetValues<T>())
            if (JsonNamingPolicy.SnakeCaseLower.ConvertName(candidate.ToString()) == value) return candidate;
        throw OptionalJson.Invalid(field);
    }
}
