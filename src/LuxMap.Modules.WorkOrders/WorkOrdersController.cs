using System.Text.Json;
using Asp.Versioning;
using LuxMap.Modules.WorkOrders.Entities;
using LuxMap.Shared.Authorization;
using LuxMap.Shared.Contracts.Enums;
using LuxMap.Shared.Contracts.Paging;
using LuxMap.Shared.Http;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LuxMap.Modules.WorkOrders;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/work-orders")]
public sealed class WorkOrdersController(WorkOrderService service) : ControllerBase
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

    [HttpGet("{id}")]
    [Authorize(Policy = LuxMapPolicies.ReadWorkOrders)]
    public Task<WorkOrderDetail> Detail(string id, CancellationToken ct) => service.Detail(id, ct);

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
    public Task<WorkOrderDetail> Start(string id, CancellationToken ct) => service.Act(id, "start", null, default, ct);

    [HttpPost("{id}/complete")]
    [Authorize(Policy = LuxMapPolicies.ExecuteWorkOrders)]
    public Task<WorkOrderDetail> Complete(string id, CompleteWorkOrderRequest request, CancellationToken ct)
        => service.Act(id, "complete", request.ReportNote, request.FaultOutcomes, ct, request.MaterialsUsed);

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
