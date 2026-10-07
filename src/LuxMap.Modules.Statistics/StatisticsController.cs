using Asp.Versioning;
using LuxMap.Shared.Authorization;
using LuxMap.Shared.Contracts.Enums;
using LuxMap.Shared.Contracts.Errors;
using LuxMap.Shared.Http;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace LuxMap.Modules.Statistics;

/// <summary>Dashboard figures for the Superior and the Manager (BE-28). <b>SELF-SIGNED</b> — drift "BE-28", provisional until FW.</summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/statistics")]
public sealed class StatisticsController(StatisticsService service, ICommuneScopeAccessor scopeAccessor) : ControllerBase
{
    /// <summary>Poles by fixture status, one row per group; <c>data_source</c> is always a group.</summary>
    /// <remarks>
    /// <c>group_by</c> adds <c>commune</c> and/or <c>segment</c>. <c>data_source</c> defaults to everything except
    /// <c>calibration_rig</c>, as on the map. A pole no sweep has covered counts as <c>unknown</c> AND in
    /// <c>never_surveyed</c>. Only groups that have poles come back.
    /// </remarks>
    [HttpGet("fixture-status")]
    [Authorize(Policy = LuxMapPolicies.ReadStatistics)]
    [ProducesResponseType<FixtureStatusStatistics>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status403Forbidden)]
    public Task<FixtureStatusStatistics> FixtureStatusAsync(
        [FromQuery(Name = "group_by")] string? groupBy,
        [FromQuery(Name = "commune_id")] string[]? communeId,
        [FromQuery(Name = "segment_id")] string? segmentId,
        [FromQuery(Name = "data_source")] string? dataSource,
        CancellationToken ct)
        => service.FixtureStatusAsync(
            new FixtureStatusQuery(
                WireEnum.ParseCsv<StatisticsDimension>(groupBy, "group_by") ?? [],
                CommuneFilter.Narrow(scopeAccessor.Scope, communeId),
                string.IsNullOrWhiteSpace(segmentId) ? null : segmentId.Trim(),
                WireEnum.ParseCsv<DataSource>(dataSource, "data_source")),
            ct);

    /// <summary>Repair work orders finished in the nights <c>from</c>..<c>to</c>, on time or late, plus those overdue now.</summary>
    /// <remarks>
    /// Nights, not calendar days: a repair finished at 01:00 belongs to the night before, like the agenda. Both
    /// bounds are optional — the last 30 nights by default. <c>group_by=commune</c> splits by commune.
    /// </remarks>
    [HttpGet("repair-timeliness")]
    [Authorize(Policy = LuxMapPolicies.ReadStatistics)]
    [ProducesResponseType<RepairTimelinessStatistics>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status403Forbidden)]
    public Task<RepairTimelinessStatistics> RepairTimelinessAsync(
        [FromQuery(Name = "group_by")] string? groupBy,
        [FromQuery(Name = "commune_id")] string[]? communeId,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        CancellationToken ct)
        => service.RepairTimelinessAsync(
            new RepairTimelinessQuery(
                WireEnum.ParseCsv<StatisticsDimension>(groupBy, "group_by") ?? [],
                CommuneFilter.Narrow(scopeAccessor.Scope, communeId),
                from,
                to),
            ct);
}
