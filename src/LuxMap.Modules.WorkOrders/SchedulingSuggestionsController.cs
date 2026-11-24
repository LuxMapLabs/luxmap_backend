using System.ComponentModel.DataAnnotations;
using Asp.Versioning;
using LuxMap.Shared.Authorization;
using LuxMap.Shared.Contracts.Paging;
using LuxMap.Shared.Http;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LuxMap.Modules.WorkOrders;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/work-orders/suggestions")]
public sealed class SchedulingSuggestionsController(SchedulingSuggestionService suggestions) : ControllerBase
{
    /// <summary>Manager's warning counts across the entire backlog, independent of the calendar month. Default data_source=field.</summary>
    [HttpGet]
    [Authorize(Policy = LuxMapPolicies.ManageWorkOrders)]
    [ProducesResponseType(typeof(SchedulingSuggestionSummary), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    [ProducesResponseType(403)]
    public Task<SchedulingSuggestionSummary> SuggestionSummary([FromQuery(Name = "commune_id")] string[]? communes,
        [FromQuery(Name = "segment_id")] string? segment, [FromQuery(Name = "data_source")] string? sources, CancellationToken ct)
        => suggestions.Summary(communes, segment, Sources(sources), ct);

    /// <summary>Paginated warnings, with existing work orders and prerequisites. Read-only: creating an order still revalidates its targets.</summary>
    [HttpGet("items")]
    [Authorize(Policy = LuxMapPolicies.ManageWorkOrders)]
    [ProducesResponseType(typeof(PagedResult<SchedulingSuggestionItem>), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    [ProducesResponseType(403)]
    public Task<PagedResult<SchedulingSuggestionItem>> SuggestionDetails([Required, FromQuery(Name = "kind")] string kind,
        [FromQuery(Name = "commune_id")] string[]? communes, [FromQuery(Name = "segment_id")] string? segment,
        [FromQuery(Name = "data_source")] string? sources, [FromQuery(Name = "state")] string? state,
        PageQuery page, CancellationToken ct)
        => suggestions.List(kind, communes, segment, Sources(sources),
            state ?? (Request.Query.ContainsKey("state") ? "" : null), page.ToPageRequest(), ct);

    private string? Sources(string? sources) => sources ?? (Request.Query.ContainsKey("data_source") ? "" : null);
}
