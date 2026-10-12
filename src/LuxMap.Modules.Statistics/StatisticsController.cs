using Asp.Versioning;
using LuxMap.Shared.Authorization;
using LuxMap.Shared.Contracts.Enums;
using LuxMap.Shared.Contracts.Errors;
using LuxMap.Shared.Csv;
using LuxMap.Shared.Http;
using LuxMap.Shared.Serialization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace LuxMap.Modules.Statistics;

/// <summary>Dashboard figures for the Superior and the Manager (BE-28, BE-31). <b>SELF-SIGNED</b> — drift "BE-28" / "BE-31", provisional until FW.</summary>
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
    [ProducesResponseType<FixtureStatusStatistics>(StatusCodes.Status200OK, "application/json")]
    [CsvExport]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status413PayloadTooLarge)]
    public async Task<ActionResult<FixtureStatusStatistics>> FixtureStatusAsync(
        [FromQuery(Name = "group_by")] string? groupBy,
        [FromQuery(Name = "commune_id")] string[]? communeId,
        [FromQuery(Name = "segment_id")] string? segmentId,
        [FromQuery(Name = "data_source")] string? dataSource,
        [FromQuery] string? format,
        CancellationToken ct)
    {
        var csv = CsvExport.IsRequested(format);
        var body = await service.FixtureStatusAsync(
            new FixtureStatusQuery(
                WireEnum.ParseCsv<StatisticsDimension>(groupBy, "group_by") ?? [],
                CommuneFilter.Narrow(scopeAccessor.Scope, communeId),
                string.IsNullOrWhiteSpace(segmentId) ? null : segmentId.Trim(),
                WireEnum.ParseCsv<DataSource>(dataSource, "data_source")),
            ct);
        return csv ? Csv(body.Rows, "fixture-status", body.AsOf) : body;
    }

    /// <summary>Repair work orders finished in the nights <c>from</c>..<c>to</c>, on time or late, plus those overdue now.</summary>
    /// <remarks>
    /// Nights, not calendar days: a repair finished at 01:00 belongs to the night before, like the agenda. Both
    /// bounds are optional — the last 30 nights by default. <c>group_by=commune</c> splits by commune.
    /// </remarks>
    [HttpGet("repair-timeliness")]
    [Authorize(Policy = LuxMapPolicies.ReadStatistics)]
    [ProducesResponseType<RepairTimelinessStatistics>(StatusCodes.Status200OK, "application/json")]
    [CsvExport]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status413PayloadTooLarge)]
    public async Task<ActionResult<RepairTimelinessStatistics>> RepairTimelinessAsync(
        [FromQuery(Name = "group_by")] string? groupBy,
        [FromQuery(Name = "commune_id")] string[]? communeId,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] string? format,
        CancellationToken ct)
    {
        var csv = CsvExport.IsRequested(format);
        var body = await service.RepairTimelinessAsync(
            new RepairTimelinessQuery(
                WireEnum.ParseCsv<StatisticsDimension>(groupBy, "group_by") ?? [],
                CommuneFilter.Narrow(scopeAccessor.Scope, communeId),
                from,
                to),
            ct);
        return csv ? Csv(body.Rows, "repair-timeliness", body.AsOf) : body;
    }

    /// <summary>Poles with the warranty of their lamp in service, soonest expiry first (BE-31, drift W-4).</summary>
    /// <remarks>
    /// <c>warranty_status</c> is worked out against today in the communes' zone: <c>expired</c> before today, <c>expiring</c>
    /// from today through <c>today + within_days</c> (default 90), <c>active</c> after, <c>unknown</c> when no expiry is on
    /// record or no lamp is in service. <c>data_source</c> is the pole's and defaults to everything except
    /// <c>calibration_rig</c>. <c>format=csv</c> returns every matching row (no paging, at most 10 000 — else 413).
    /// </remarks>
    [HttpGet("warranty")]
    [Authorize(Policy = LuxMapPolicies.ReadStatistics)]
    [ProducesResponseType<WarrantyList>(StatusCodes.Status200OK, "application/json")]
    [CsvExport]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status413PayloadTooLarge)]
    public async Task<ActionResult<WarrantyList>> WarrantyAsync(
        [FromQuery] string? status,
        [FromQuery(Name = "commune_id")] string[]? communeId,
        [FromQuery(Name = "segment_id")] string? segmentId,
        [FromQuery(Name = "data_source")] string? dataSource,
        [FromQuery(Name = "within_days")] int? withinDays,
        [FromQuery] string? format,
        PageQuery page,
        CancellationToken ct)
    {
        var csv = CsvExport.IsRequested(format);
        var body = await service.WarrantyAsync(
            new WarrantyQuery(
                WireEnum.ParseCsv<WarrantyStatus>(status, "status"),
                CommuneFilter.Narrow(scopeAccessor.Scope, communeId),
                string.IsNullOrWhiteSpace(segmentId) ? null : segmentId.Trim(),
                WireEnum.ParseCsv<DataSource>(dataSource, "data_source"),
                withinDays),
            csv ? null : page.ToPageRequest(),
            ct);
        return csv ? Csv(body.Items, "warranty", body.Today) : body;
    }

    private FileContentResult Csv<T>(IReadOnlyCollection<T> rows, string name, DateTime asOf)
        => Csv(rows, name, DateOnly.FromDateTime(asOf));

    private FileContentResult Csv<T>(IReadOnlyCollection<T> rows, string name, DateOnly day)
        => File(CsvExport.Write(rows, LuxMapJsonOptions.Default), CsvExport.ContentType, CsvExport.FileName(name, day));
}
