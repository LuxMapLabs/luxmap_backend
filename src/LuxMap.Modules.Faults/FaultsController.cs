using System.Net;
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
public sealed class FaultsController(FaultQueryService service, FaultReviewService review, FaultReportService report, LuxMapDbContext db) : ControllerBase
{
    /// <summary>
    /// Faults in the caller's communes as paginated JSON — NOT GeoJSON — each item with
    /// <c>location{lat,lng}</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Default order <c>-severity</c>, oldest first within a severity (drift P-3). <c>sort</c> also takes
    /// <c>priority_score</c> (unscored faults last),
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

    /// <summary>A field engineer reports a fault seen on site (BE-41, Contract 5.4).</summary>
    /// <remarks>
    /// <para>
    /// Starts <c>detected</c>, <c>source_channel = field_report</c>, <c>reported_by</c> = the caller, for a Manager
    /// to review. 201 with the item and <c>client_op_id</c>; the same <c>client_op_id</c> again answers 200 with
    /// the fault already created (<c>DUPLICATE_OP</c> is a replay, not an error).
    /// </para>
    /// <para>
    /// With <c>pole_id</c> the commune, segment and data source come from the pole (404 <c>POLE_NOT_FOUND</c>
    /// outside scope; sending <c>commune_id</c> too is 400). Without it <c>location</c> is required
    /// (400 <c>LOCATION_REQUIRED</c>). Only <c>lamp_out</c> / <c>lamp_dim</c> (400 <c>FAULT_TYPE_NOT_REPORTABLE</c>).
    /// Photos go separately: <c>POST /faults/{fault_id}/photos</c> (drift EV-2).
    /// </para>
    /// </remarks>
    [HttpPost]
    [Authorize(Policy = LuxMapPolicies.ReportFaults)]
    [ProducesResponseType<ReportedFault>(StatusCodes.Status201Created)]
    [ProducesResponseType<ReportedFault>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ReportedFault>> ReportAsync(ReportFaultRequest request, CancellationToken ct)
    {
        var (fault, created) = await report.ReportAsync(request, ct);
        return created ? StatusCode(StatusCodes.Status201Created, fault) : Ok(fault);
    }

    /// <summary>A Manager reviews one fault (BE-19): confirm, reject, reclassify, set severity, write a note.</summary>
    /// <remarks>
    /// <para>
    /// <c>fault_status</c> is required; sending the current status means "no transition" so severity,
    /// reclassification or the note can change on their own. Only <c>detected → confirmed | rejected</c>
    /// are made here — later statuses belong to repair work orders.
    /// </para>
    /// <para>
    /// 404 <c>FAULT_NOT_FOUND</c> outside scope; 409 <c>FAULT_IN_ACTIVE_REPAIR</c> while a repair holds
    /// the fault, <c>INVALID_STATE_TRANSITION</c> for any other move, <c>CONCURRENT_MODIFICATION</c> on a race.
    /// </para>
    /// </remarks>
    /// <summary>One fault, in the same shape as an item of <c>GET /faults</c> (drift N-6).</summary>
    /// <remarks>
    /// What a <c>fault_reported</c> notification opens. A fault outside the caller's communes and one that does
    /// not exist answer the same 404, so the ID of a foreign fault confirms nothing.
    /// </remarks>
    [HttpGet("{id}")]
    [Authorize(Policy = LuxMapPolicies.ReadFaults)]
    [ProducesResponseType<FaultItem>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status404NotFound)]
    public async Task<FaultItem> GetAsync(string id, CancellationToken ct)
        => await service.ItemAsync(id, ct) ?? throw new LuxMapException("FAULT_NOT_FOUND", HttpStatusCode.NotFound,
            "That fault does not exist, or it is outside your permitted commune scope.", new Dictionary<string, object?> { ["fault_id"] = id });

    [HttpPatch("{id}")]
    [Authorize(Policy = LuxMapPolicies.ReviewFaults)]
    [ProducesResponseType<FaultItem>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status409Conflict)]
    public Task<FaultItem> ReviewAsync(string id, PatchFaultRequest request, CancellationToken ct)
        => review.ReviewAsync(id, request, ct);

    private static string? Single(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
