using Asp.Versioning;
using LuxMap.Persistence;
using LuxMap.Shared.Authorization;
using LuxMap.Shared.Contracts.Enums;
using LuxMap.Shared.Contracts.Errors;
using LuxMap.Shared.Contracts.Paging;
using LuxMap.Shared.Http;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace LuxMap.Modules.Faults;

/// <summary>The fault list — <c>GET /faults</c> (BE-40, Contract section 5.4).</summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/faults")]
public sealed class FaultsController(FaultQueryService service, LuxMapDbContext db) : ControllerBase
{
    /// <summary>
    /// Faults in the caller's communes as paginated JSON — NOT GeoJSON — each item with
    /// <c>location{lat,lng}</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Default order <c>-priority_score</c>, unscored faults last. <c>sort</c> also takes
    /// <c>detected_at</c> and <c>updated_at</c>, each with an optional <c>-</c>.
    /// </para>
    /// <para>
    /// <c>status</c>, <c>severity</c>, <c>fault_type</c>, <c>source_channel</c> and
    /// <c>data_source</c> take comma-separated lists. <c>data_source</c> defaults to everything
    /// except <c>calibration_rig</c>, like the map layers. <c>bbox</c> is optional here.
    /// </para>
    /// <para>
    /// <c>pole_id</c> takes one value; an unknown pole and a pole outside the caller's scope both
    /// answer <c>200</c> with an empty page. <c>work_order_id</c> is the work order currently holding
    /// the fault, whether or not the caller may open it.
    /// </para>
    /// </remarks>
    [HttpGet]
    [Authorize(Policy = LuxMapPolicies.ReadFaults)]
    [ProducesResponseType<PagedResult<FaultItem>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status403Forbidden)]
    public Task<PagedResult<FaultItem>> ListAsync(
        [FromQuery] string? bbox,
        [FromQuery] string? status,
        [FromQuery] string? severity,
        [FromQuery(Name = "fault_type")] string? faultType,
        [FromQuery(Name = "source_channel")] string? sourceChannel,
        [FromQuery(Name = "data_source")] string? dataSource,
        [FromQuery(Name = "pole_id")] string? poleId,
        [FromQuery(Name = "segment_id")] string? segmentId,
        [FromQuery(Name = "cluster_id")] string? clusterId,
        [FromQuery(Name = "commune_id")] string[]? communeId,
        [FromQuery] string? sort,
        PageQuery page,
        CancellationToken ct)
        => service.ListAsync(
            new FaultListQuery
            {
                Bbox = string.IsNullOrWhiteSpace(bbox) ? null : BoundingBox.Parse(bbox),
                Statuses = WireEnum.ParseCsv<FaultStatus>(status, "status"),
                Severities = WireEnum.ParseCsv<Severity>(severity, "severity"),
                FaultTypes = WireEnum.ParseCsv<FaultType>(faultType, "fault_type"),
                SourceChannels = WireEnum.ParseCsv<SourceChannel>(sourceChannel, "source_channel"),
                DataSources = WireEnum.ParseCsv<DataSource>(dataSource, "data_source"),
                PoleId = Single(poleId),
                SegmentId = Single(segmentId),
                ClusterId = Single(clusterId),
                CommuneIds = CommuneFilter.Narrow(db.CurrentCommuneScope, communeId),
                Sort = FaultSort.Parse(sort),
            },
            page.ToPageRequest(),
            ct);

    private static string? Single(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
