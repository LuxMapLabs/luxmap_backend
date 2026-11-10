using Asp.Versioning;
using LuxMap.Shared.Authorization;
using LuxMap.Shared.Contracts.Paging;
using LuxMap.Shared.Http;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LuxMap.Modules.WorkOrders;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/survey-schedules/recurring")]
public sealed class SurveyRecurringController(SurveyRecurringService service) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = LuxMapPolicies.ReadWorkOrders)]
    public Task<PagedResult<SurveyRecurringItem>> List([FromQuery(Name = "commune_id")] string? commune, PageQuery page, CancellationToken ct)
        => service.List(commune, page.ToPageRequest(), ct);

    [HttpPost]
    [Authorize(Policy = LuxMapPolicies.ManageWorkOrders)]
    [ProducesResponseType(typeof(SurveyRecurringItem), 201)]
    public async Task<ActionResult<SurveyRecurringItem>> Create(CreateSurveyRecurringRequest request, CancellationToken ct)
        => StatusCode(201, await service.Create(request, ct));

    [HttpPatch("{id}")]
    [Authorize(Policy = LuxMapPolicies.ManageWorkOrders)]
    public Task<SurveyRecurringItem> Patch(string id, PatchSurveyRecurringRequest request, CancellationToken ct)
        => service.Patch(id, request, ct);

    [HttpDelete("{id}")]
    [Authorize(Policy = LuxMapPolicies.ManageWorkOrders)]
    [ProducesResponseType(204)]
    public async Task<IActionResult> Delete(string id, CancellationToken ct)
    {
        await service.Delete(id, ct);
        return NoContent();
    }
}
