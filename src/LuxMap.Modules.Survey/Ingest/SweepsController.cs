using System.ComponentModel.DataAnnotations;
using System.Net;
using Asp.Versioning;
using Microsoft.AspNetCore.Http;
using LuxMap.Modules.Survey.Entities;
using LuxMap.Shared.Authorization;
using LuxMap.Shared.Contracts.Enums;
using LuxMap.Shared.Contracts.Paging;
using LuxMap.Shared.Http;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LuxMap.Modules.Survey.Ingest;

/// <summary>SELF-SIGNED BE-15 P2a API, temporary until FW confirms. Uploads never publish observations.</summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/sweeps")]
public sealed class SweepsController(SurveyIngestService service) : ControllerBase
{
    private static LuxMapException TooLarge() => new("UPLOAD_TOO_LARGE", HttpStatusCode.RequestEntityTooLarge, "Upload exceeds the endpoint limit.");

    [HttpPost]
    [Authorize(Policy = LuxMapPolicies.SubmitSurveys)]
    public async Task<ActionResult<SweepResponse>> Create(CreateSweepRequest request, CancellationToken ct)
    {
        var result = await service.Create(request, ct);
        return StatusCode(result.Created ? 201 : 200, result.Response);
    }

    [HttpPut("{id}/clips/{clipNo:int}")]
    [Authorize(Policy = LuxMapPolicies.SubmitSurveys)]
    [RequestSizeLimit(SurveyIngestService.ClipLimit)]
    [RawRequestBody("video/mp4")]
    public async Task<ActionResult<SurveyClipResponse>> Clip(string id, int clipNo,
        [FromHeader(Name = "X-Content-SHA256"), Required] string sha256, CancellationToken ct)
    {
        try { return Ok(await service.Clip(id, clipNo, Request.Body, Request.ContentLength, sha256, ct)); }
        catch (BadHttpRequestException e) when (e.StatusCode == 413) { throw TooLarge(); }
    }

    [HttpPut("{id}/raw/{kind}")]
    [Authorize(Policy = LuxMapPolicies.SubmitSurveys)]
    [RequestSizeLimit(SurveyIngestService.RawLimit)]
    [RawRequestBody("application/x-ndjson", "application/json")]
    public async Task<ActionResult<SurveyRawResponse>> Raw(string id, string kind, CancellationToken ct)
    {
        var parsed = kind switch { "gps_track" => SurveyRawKind.GpsTrack, "lux_log" => SurveyRawKind.LuxLog,
            "capture_config" => SurveyRawKind.CaptureConfig, _ => throw SurveyRawParser.Invalid(0, "kind") };
        if (Request.ContentLength > SurveyIngestService.RawLimit) throw TooLarge();
        try { return Ok(await service.Raw(id, parsed, Request.Body, ct)); }
        catch (BadHttpRequestException e) when (e.StatusCode == 413) { throw TooLarge(); }
    }

    [HttpPost("{id}/submit")]
    [Authorize(Policy = LuxMapPolicies.SubmitSurveys)]
    public async Task<ActionResult<SweepResponse>> Submit(string id, SubmitSweepRequest request, CancellationToken ct)
    {
        var result = await service.Submit(id, request, ct);
        return StatusCode(result.Queued ? 202 : 200, result.Response);
    }

    [HttpGet("{id}")]
    [Authorize(Policy = LuxMapPolicies.ReadSurveys)]
    public async Task<ActionResult<SweepResponse>> Detail(string id, CancellationToken ct) => Ok(await service.Detail(id, ct));

    [HttpGet]
    [Authorize(Policy = LuxMapPolicies.ReadSurveys)]
    public async Task<ActionResult<PagedResult<SweepResponse>>> List(
        [FromQuery(Name = "work_order_id")] string? workOrder,
        [FromQuery(Name = "segment_id")] string? segment,
        [FromQuery(Name = "processing_status")] string? processing,
        [FromQuery(Name = "data_source")] string? source, PageQuery page, CancellationToken ct)
        => Ok(await service.List(workOrder, segment, processing is null ? null : WireEnum.Parse<SweepProcessingStatus>(processing, "processing_status"), source is null ? null : WireEnum.Parse<DataSource>(source, "data_source"), page.ToPageRequest(), ct));
}
