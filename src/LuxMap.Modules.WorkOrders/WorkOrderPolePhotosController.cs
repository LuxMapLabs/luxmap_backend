using System.ComponentModel.DataAnnotations;
using Asp.Versioning;
using LuxMap.Shared.Authorization;
using LuxMap.Shared.Contracts.Errors;
using LuxMap.Shared.Contracts.Paging;
using LuxMap.Shared.Http;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace LuxMap.Modules.WorkOrders;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/work-orders/{id}/pole-photos")]
public sealed class WorkOrderPolePhotosController(WorkOrderPolePhotoService photos, IAuthorizationService authorization,
    ICorrelationIdAccessor correlation) : ControllerBase
{
    /// <summary>Upload one pole photo; optional AI, no status change until a Manager confirms.</summary>
    [HttpPost]
    [Authorize(Policy = LuxMapPolicies.ExecuteWorkOrders)]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(WorkOrderPolePhotoService.MaxRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = WorkOrderPolePhotoService.MaxRequestBytes)]
    [PolePhotoBodyLimit]
    [ProducesResponseType(typeof(PolePhotoItem), 201)]
    [ProducesResponseType(typeof(PolePhotoItem), 200)]
    [ProducesResponseType(typeof(LuxMap.Shared.Contracts.Errors.ApiErrorResponse), 400)]
    [ProducesResponseType(typeof(LuxMap.Shared.Contracts.Errors.ApiErrorResponse), 404)]
    [ProducesResponseType(typeof(LuxMap.Shared.Contracts.Errors.ApiErrorResponse), 409)]
    [ProducesResponseType(typeof(LuxMap.Shared.Contracts.Errors.ApiErrorResponse), 413)]
    [ProducesResponseType(typeof(LuxMap.Shared.Contracts.Errors.ApiErrorResponse), 415)]
    [ProducesResponseType(typeof(LuxMap.Shared.Contracts.Errors.ApiErrorResponse), 503)]
    public async Task<ActionResult<PolePhotoItem>> Upload(string id,
        [Required, FromForm(Name = "pole_id")] string? poleId, [Required] IFormFile? photo,
        [Required, FromForm(Name = "captured_at")] string? capturedAt, [Required, FromForm(Name = "lat")] string? lat,
        [Required, FromForm(Name = "lng")] string? lng, [Required, FromForm(Name = "client_op_id")] string? clientOpId, CancellationToken ct)
    {
        string[] allowed = ["pole_id", "captured_at", "lat", "lng", "client_op_id"];
        if (Request.Form.Keys.Any(k => !allowed.Contains(k, StringComparer.Ordinal))
            || Request.Form.Any(k => k.Value.Count != 1) || Request.Form.Files.Count != 1 || Request.Form.Files[0].Name != "photo")
            throw OptionalJson.Invalid("form");
        var (item, created) = await photos.UploadAsync(id, poleId, photo, capturedAt, lat, lng, clientOpId, ct);
        return created ? Created($"/api/v1/work-orders/{id}/pole-photos/{item.PhotoId}", item) : Ok(item);
    }

    [HttpGet]
    [Authorize(Policy = LuxMapPolicies.ReadWorkOrders)]
    public Task<PagedResult<PolePhotoItem>> List(string id, [FromQuery(Name = "pole_id")] string? poleId,
        PageQuery page, CancellationToken ct) => photos.ListAsync(id, poleId, page.ToPageRequest(), ct);

    [HttpGet("{photo_id}")]
    [Authorize(Policy = LuxMapPolicies.ReadWorkOrders)]
    public Task<PolePhotoItem> Detail(string id, [FromRoute(Name = "photo_id")] string photoId, CancellationToken ct)
        => photos.DetailAsync(id, photoId, ct);

    [HttpGet("{photo_id}/thumbnail")]
    [Authorize(Policy = LuxMapPolicies.ReadWorkOrders)]
    [Produces("image/jpeg")]
    public Task<IActionResult> Thumbnail(string id, [FromRoute(Name = "photo_id")] string photoId, CancellationToken ct)
        => Media(id, photoId, true, ct);

    [HttpGet("{photo_id}/original")]
    [Authorize(Policy = LuxMapPolicies.ReadWorkOrders)]
    [Produces("image/jpeg")]
    public Task<IActionResult> Original(string id, [FromRoute(Name = "photo_id")] string photoId, CancellationToken ct)
        => Media(id, photoId, false, ct);

    private async Task<IActionResult> Media(string id, string photoId, bool thumbnail, CancellationToken ct)
    {
        var stream = await photos.OpenAsync(id, photoId, thumbnail, ct);
        Response.Headers.CacheControl = "no-store";
        return File(stream, "image/jpeg");
    }

    /// <summary>
    /// The clip behind a pole photo, with HTTP ranges so a player can seek (POLE-VIDEO D-9). Through the API like every other byte:
    /// commune scope applies, nothing is cached. A Manager opening a clip that is not decided yet is recorded (D-13).
    /// </summary>
    [HttpGet("{photo_id}/video")]
    [Authorize(Policy = LuxMapPolicies.ReadWorkOrders)]
    [ProducesResponseType(typeof(FileResult), 200, "video/mp4")]
    [ProducesResponseType(typeof(FileResult), 206, "video/mp4")]
    [ProducesResponseType(typeof(ApiErrorResponse), 404)]
    [ProducesResponseType(typeof(ApiErrorResponse), 416)]
    [ProducesResponseType(typeof(ApiErrorResponse), 503)]
    public async Task<IActionResult> Video(string id, [FromRoute(Name = "photo_id")] string photoId, CancellationToken ct)
    {
        var recordView = (await authorization.AuthorizeAsync(User, LuxMapPolicies.ReviewSurveys)).Succeeded;
        var video = await photos.OpenVideoAsync(id, photoId, HttpContext, recordView, ct);
        Response.Headers.CacheControl = "no-store";
        Response.Headers.AcceptRanges = "bytes";
        if (!video.Unsatisfiable) return new PoleVideoResult(video);
        // Written here, not thrown: the exception middleware clears the response, Content-Range included.
        Response.Headers.ContentRange = $"bytes */{video.Total}";
        // No [Produces] on this action: it replaces the content type of an ObjectResult and would turn this JSON body into a 406.
        return new ObjectResult(ApiErrorResponse.Create("RANGE_NOT_SATISFIABLE", "The requested range lies outside the clip.",
            new Dictionary<string, object?> { ["correlation_id"] = correlation.CorrelationId, ["size"] = video.Total }))
        { StatusCode = StatusCodes.Status416RangeNotSatisfiable };
    }

    /// <summary>Assignee reviews ON/OFF before a Manager decides; changed labels require a note.</summary>
    [HttpPatch("{photo_id}/review")]
    [Authorize(Policy = LuxMapPolicies.ExecuteWorkOrders)]
    [ProducesResponseType(typeof(PolePhotoItem), 200)]
    [ProducesResponseType(typeof(LuxMap.Shared.Contracts.Errors.ApiErrorResponse), 400)]
    [ProducesResponseType(typeof(LuxMap.Shared.Contracts.Errors.ApiErrorResponse), 401)]
    [ProducesResponseType(typeof(LuxMap.Shared.Contracts.Errors.ApiErrorResponse), 403)]
    [ProducesResponseType(typeof(LuxMap.Shared.Contracts.Errors.ApiErrorResponse), 404)]
    [ProducesResponseType(typeof(LuxMap.Shared.Contracts.Errors.ApiErrorResponse), 409)]
    public Task<PolePhotoItem> Review(string id, [FromRoute(Name = "photo_id")] string photoId,
        PolePhotoReviewRequest request, CancellationToken ct) => photos.ReviewAsync(id, photoId, request, ct);

    /// <summary>One Manager decision; an ON photo cannot disprove dim, and older photos cannot overwrite current status.</summary>
    [HttpPost("{photo_id}/decision")]
    [Authorize(Policy = LuxMapPolicies.ReviewSurveys)]
    public Task<PolePhotoItem> Decide(string id, [FromRoute(Name = "photo_id")] string photoId,
        PolePhotoDecisionRequest request, CancellationToken ct) => photos.DecideAsync(id, photoId, request, ct);
}

/// <summary>200 with the whole clip or 206 with the asked range; the store already holds exactly those bytes, so this only copies.</summary>
internal sealed class PoleVideoResult(PoleVideoRead video) : IActionResult
{
    public async Task ExecuteResultAsync(ActionContext context)
    {
        var response = context.HttpContext.Response;
        await using var body = video.Body!;
        response.StatusCode = video.Partial ? StatusCodes.Status206PartialContent : StatusCodes.Status200OK;
        response.ContentType = "video/mp4";
        response.ContentLength = video.To - video.From + 1;
        if (video.Partial) response.Headers.ContentRange = $"bytes {video.From}-{video.To}/{video.Total}";
        await body.CopyToAsync(response.Body, context.HttpContext.RequestAborted);
    }
}

