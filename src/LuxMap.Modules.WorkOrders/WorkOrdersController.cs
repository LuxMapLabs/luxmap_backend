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
    WorkOrderAgendaService agenda) : ControllerBase
{
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
        PageQuery page, CancellationToken ct)
        => service.List(statuses?.Split(',').Select(x => Wire<WorkOrderStatus>(x, "wo_status")).ToArray(),
            kind is null ? null : Wire<TaskKind>(kind, "task_kind"), assigned, segment, communes, from, to, page.ToPageRequest(), ct,
            string.IsNullOrWhiteSpace(caseId) ? null : caseId.Trim());

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
    /// repair, <c>observation</c> on an inspection. JPEG decided by magic bytes; <c>client_op_id</c> makes a retry
    /// return the same photo (200) instead of a second one (201).
    /// </summary>
    [HttpPost("{id}/evidence")]
    [Authorize(Policy = LuxMapPolicies.ExecuteWorkOrders)]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(WorkOrderEvidenceService.MaxUploadBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = WorkOrderEvidenceService.MaxUploadBytes)]
    [ProducesResponseType(typeof(EvidenceItem), 201)]
    [ProducesResponseType(typeof(EvidenceItem), 200)]
    public async Task<ActionResult<EvidenceItem>> UploadEvidence(string id, IFormFile? file,
        [FromForm(Name = "kind")] string? kind, [FromForm(Name = "captured_at")] string? capturedAt,
        [FromForm(Name = "lat")] string? lat, [FromForm(Name = "lng")] string? lng,
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

    [HttpPost("{id}/start")]
    [Authorize(Policy = LuxMapPolicies.ExecuteWorkOrders)]
    public Task<WorkOrderDetail> Start(string id,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] StartWorkOrderRequest? request, CancellationToken ct)
        => service.Act(id, "start", null, default, ct, performedAt: request?.PerformedAt);

    [HttpPost("{id}/complete")]
    [Authorize(Policy = LuxMapPolicies.ExecuteWorkOrders)]
    public Task<WorkOrderDetail> Complete(string id, CompleteWorkOrderRequest request, CancellationToken ct)
        => service.Act(id, "complete", request.ReportNote, request.FaultOutcomes, ct, request.MaterialsUsed, request.PerformedAt);

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
