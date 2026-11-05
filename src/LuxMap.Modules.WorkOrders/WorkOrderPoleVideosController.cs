using System.Net;
using Asp.Versioning;
using LuxMap.Shared.Authorization;
using LuxMap.Shared.Contracts.Errors;
using LuxMap.Shared.Http;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace LuxMap.Modules.WorkOrders;

/// <summary>
/// POLE-VIDEO PV-c2: a clip of one lamp, taken for a work order. The body is the MP4 itself, streamed to the store (never buffered, never a
/// multipart form); everything else rides on the path, the query and one header. The result is a pole photo — read it back, review it and decide on it
/// under <c>/pole-photos</c> like any other.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/work-orders/{id}/pole-videos")]
public sealed class WorkOrderPoleVideosController(WorkOrderPolePhotoService photos) : ControllerBase
{
    private static readonly string[] Allowed = ["pole_id", "captured_at", "lat", "lng"];

    /// <summary>
    /// Upload one clip; the operation id in the path makes a retry the same request. 201 when stored, 200 when this operation was already stored
    /// (nothing is read, judged or written again); 409 <c>IDEMPOTENCY_CONFLICT</c> when the id was used for different content.
    /// </summary>
    [HttpPut("{client_op_id}")]
    [Authorize(Policy = LuxMapPolicies.ExecuteWorkOrders)]
    [RequestSizeLimit(WorkOrderPolePhotoService.MaxClipBytes)]
    [RawRequestBody("video/mp4")]
    [ProducesResponseType(typeof(PolePhotoItem), 201)]
    [ProducesResponseType(typeof(PolePhotoItem), 200)]
    [ProducesResponseType(typeof(ApiErrorResponse), 400)]
    [ProducesResponseType(typeof(ApiErrorResponse), 404)]
    [ProducesResponseType(typeof(ApiErrorResponse), 409)]
    [ProducesResponseType(typeof(ApiErrorResponse), 413)]
    [ProducesResponseType(typeof(ApiErrorResponse), 415)]
    [ProducesResponseType(typeof(ApiErrorResponse), 503)]
    public async Task<ActionResult<PolePhotoItem>> Upload(string id, [FromRoute(Name = "client_op_id")] string clientOpId,
        [FromQuery(Name = "pole_id")] string? poleId, [FromQuery(Name = "captured_at")] string? capturedAt,
        [FromQuery(Name = "lat")] string? lat, [FromQuery(Name = "lng")] string? lng,
        [FromHeader(Name = "X-Content-SHA256")] string? sha256, CancellationToken ct)
    {
        if (Request.Query.Keys.Any(k => !Allowed.Contains(k, StringComparer.Ordinal)) || Request.Query.Any(q => q.Value.Count != 1))
            throw OptionalJson.Invalid("query");
        try
        {
            var (item, created) = await photos.UploadVideoAsync(id, poleId, capturedAt, lat, lng, clientOpId, sha256, Request.Body, Request.ContentLength, ct);
            return created ? Created($"/api/v1/work-orders/{id}/pole-photos/{item.PhotoId}", item) : Ok(item);
        }
        catch (BadHttpRequestException e) when (e.StatusCode == 413)
        {
            throw new LuxMapException("UPLOAD_TOO_LARGE", HttpStatusCode.RequestEntityTooLarge, "Upload exceeds the endpoint limit.");
        }
    }
}
