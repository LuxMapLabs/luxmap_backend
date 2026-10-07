using Asp.Versioning;
using LuxMap.Shared.Authorization;
using LuxMap.Shared.Contracts.Enums;
using LuxMap.Shared.Contracts.Errors;
using LuxMap.Shared.Contracts.GeoJson;
using LuxMap.Shared.Http;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace LuxMap.Modules.Map.Features;

/// <summary>
/// The map layers — <c>GET /map/poles</c>, <c>GET /map/segments</c>, <c>GET /map/iot-nodes</c> and <c>GET /map/cabinets</c>
/// (BE-14, Contract sections 5.1–5.2; moved under <c>/map</c> by drift MAP-1).
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ <b>Routed under <c>/map</c>, NOT under <c>/assets</c>.</b> The inventory endpoints of BE-12a own
/// <c>/assets/poles</c>: paginated JSON, Manager writes. These are read-only GeoJSON layers keyed by
/// <c>bbox</c>. Two surfaces for two consumers, each with its own prefix, so neither path reads as
/// the generic "poles" resource.
/// </para>
/// <para>
/// No role policy, the same reasoning as every other GET in the system: <c>SetFallbackPolicy</c>
/// already requires a login, and a policy is one EXACT role, so naming one would lock out the other
/// three rather than set a floor (BE-12a, rule 4).
/// </para>
/// </remarks>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/map")]
public sealed class MapController(
    MapQueryService service,
    PoleDetailService poleDetail,
    ICommuneScopeAccessor scopeAccessor) : ControllerBase
{
    /// <summary>
    /// Poles inside a bounding box, as a GeoJSON <c>FeatureCollection</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>bbox</c> is REQUIRED — <c>minLng,minLat,maxLng,maxLat</c> in EPSG:4326. There is no
    /// "fetch everything" variant and there must not be one: CLAUDE.md lists it as an anti-pattern,
    /// because an unbounded query cannot meet the 500 ms budget and degrades without a signal.
    /// </para>
    /// <para>
    /// Past <see cref="MapQueryService.MaxPoles"/> poles the answer is <b>413
    /// <c>BBOX_TOO_LARGE</c></b> carrying the real count, so the front end can say "zoom in to see
    /// detail" rather than guess why a map came back empty.
    /// </para>
    /// <para>
    /// <c>data_source</c> defaults to everything EXCEPT <c>calibration_rig</c> (section 1.6). Pass it
    /// by name to see the calibration rig.
    /// </para>
    /// </remarks>
    [HttpGet("poles")]
    [Authorize(Policy = LuxMapPolicies.ReadNetwork)]
    [ProducesResponseType<FeatureCollection<PoleProperties>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status413PayloadTooLarge)]
    public async Task<ActionResult<FeatureCollection<PoleProperties>>> PolesAsync(
        [FromQuery] string? bbox,
        [FromQuery] string? status,
        [FromQuery(Name = "power_source")] PowerSource? powerSource,
        [FromQuery(Name = "segment_id")] string? segmentId,
        [FromQuery(Name = "commune_id")] string[]? communeId,
        [FromQuery(Name = "has_open_fault")] bool? hasOpenFault,
        [FromQuery(Name = "data_source")] string? dataSource,
        CancellationToken ct)
        => Ok(await service.PolesAsync(
            new PoleMapQuery
            {
                Bbox = BoundingBox.Parse(bbox),
                Statuses = WireEnum.ParseCsv<FixtureStatus>(status, "status"),
                PowerSource = powerSource,
                SegmentId = segmentId,
                CommuneIds = CommuneFilter.Narrow(scopeAccessor.Scope, communeId),
                HasOpenFault = hasOpenFault,
                DataSource = WireEnum.ParseCsv<DataSource>(dataSource, "data_source"),
            },
            ct));

    /// <summary>One pole with its lamp, status, baselines, history, open faults and recent frames (BE-20).</summary>
    /// <remarks>
    /// Everything the pole screen needs in ONE request (Contract 5.1) — the front end must not stitch
    /// the screen together from several calls. <b>SELF-SIGNED, provisional until FW</b>: the shape differs
    /// from the Contract in places listed in the BE-20 drift entry. A pole outside the caller's commune
    /// scope is 404, never 403 (Contract section 7).
    /// </remarks>
    [HttpGet("poles/{pole_id}")]
    [Authorize(Policy = LuxMapPolicies.ReadNetwork)]
    [ProducesResponseType<PoleMapDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PoleMapDetail>> PoleDetailAsync(
        [FromRoute(Name = "pole_id")] string poleId, CancellationToken ct)
        => Ok(await poleDetail.GetAsync(poleId, ct));

    /// <summary>Road segments inside a bounding box, as a <c>FeatureCollection</c> of LineStrings.</summary>
    /// <remarks>
    /// No size limit here, unlike poles: a commune has tens of roads where it has thousands of
    /// lamps, so the cap that protects the pole layer would only ever add a failure mode to this one.
    /// </remarks>
    [HttpGet("segments")]
    [Authorize(Policy = LuxMapPolicies.ReadNetwork)]
    [ProducesResponseType<FeatureCollection<SegmentProperties>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<FeatureCollection<SegmentProperties>>> SegmentsAsync(
        [FromQuery] string? bbox,
        [FromQuery(Name = "commune_id")] string[]? communeId,
        [FromQuery(Name = "data_source")] string? dataSource,
        CancellationToken ct)
        => Ok(await service.SegmentsAsync(
            new SegmentMapQuery
            {
                Bbox = BoundingBox.Parse(bbox),
                CommuneIds = CommuneFilter.Narrow(scopeAccessor.Scope, communeId),
                DataSource = WireEnum.ParseCsv<DataSource>(dataSource, "data_source"),
            },
            ct));

    /// <summary>IoT devices inside a bounding box, as a <c>FeatureCollection</c> of points (BE-14b).</summary>
    /// <remarks>
    /// Shape per drift "BE-14 / IoT" rather than Contract section 5.6: no <c>battery_pct</c>,
    /// <c>segment_ids</c> and <c>feeder_ids</c> as derived lists, <c>pole_id</c> always null.
    /// <c>data_source</c> follows the same default as poles and segments — the testbed
    /// (<c>calibration_rig</c>) is shown only when asked for by name.
    /// </remarks>
    [HttpGet("iot-nodes")]
    [Authorize(Policy = LuxMapPolicies.ReadNetwork)]
    [ProducesResponseType<FeatureCollection<IotNodeProperties>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<FeatureCollection<IotNodeProperties>>> IotNodesAsync(
        [FromQuery] string? bbox,
        [FromQuery(Name = "commune_id")] string[]? communeId,
        [FromQuery(Name = "data_source")] string? dataSource,
        CancellationToken ct)
        => Ok(await service.IotNodesAsync(
            new IotNodeMapQuery
            {
                Bbox = BoundingBox.Parse(bbox),
                CommuneIds = CommuneFilter.Narrow(scopeAccessor.Scope, communeId),
                DataSource = WireEnum.ParseCsv<DataSource>(dataSource, "data_source"),
            },
            ct));

    /// <summary>Electrical cabinets inside a bounding box, as a <c>FeatureCollection</c> of points (CAB-7).</summary>
    /// <remarks>
    /// ⚠️ SELF-SIGNED (drift CABINET), not yet in the Contract. Every cabinet, with or without a device — the
    /// device layer stays <c>GET /map/iot-nodes</c>. Same <c>bbox</c>, <c>commune_id</c> and <c>data_source</c>
    /// rules as the other layers: the testbed cabinet appears only when <c>calibration_rig</c> is asked for.
    /// </remarks>
    [HttpGet("cabinets")]
    [Authorize(Policy = LuxMapPolicies.ReadNetwork)]
    [ProducesResponseType<FeatureCollection<CabinetProperties>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<FeatureCollection<CabinetProperties>>> CabinetsAsync(
        [FromQuery] string? bbox,
        [FromQuery(Name = "commune_id")] string[]? communeId,
        [FromQuery(Name = "data_source")] string? dataSource,
        CancellationToken ct)
        => Ok(await service.CabinetsAsync(
            new CabinetMapQuery
            {
                Bbox = BoundingBox.Parse(bbox),
                CommuneIds = CommuneFilter.Narrow(scopeAccessor.Scope, communeId),
                DataSource = WireEnum.ParseCsv<DataSource>(dataSource, "data_source"),
            },
            ct));
}
