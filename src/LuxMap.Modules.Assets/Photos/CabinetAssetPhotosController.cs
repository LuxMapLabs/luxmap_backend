using System.ComponentModel.DataAnnotations;
using Asp.Versioning;
using LuxMap.Modules.Assets.Entities;
using LuxMap.Shared.Authorization;
using LuxMap.Shared.Http;
using LuxMap.Shared.Contracts.Errors;
using LuxMap.Shared.Contracts.Paging;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace LuxMap.Modules.Assets.Photos;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/assets/cabinets/{cabinet_id}/photos")]
public sealed class CabinetAssetPhotosController(AssetPhotoService photos) : ControllerBase
{
    [HttpPost]
    [Authorize(Policy = LuxMapPolicies.UploadAssetPhotos)]
    [Consumes("multipart/form-data")]
    [UploadBodyLimit(AssetPhotoService.MaxRequestBytes)]
    [RequestSizeLimit(AssetPhotoService.MaxRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = AssetPhotoService.MaxRequestBytes)]
    [ProducesResponseType(typeof(CabinetAssetPhotoItem), 201)]
    [ProducesResponseType(typeof(CabinetAssetPhotoItem), 200)]
    [ProducesResponseType(typeof(ApiErrorResponse), 400)]
    [ProducesResponseType(typeof(ApiErrorResponse), 404)]
    [ProducesResponseType(typeof(ApiErrorResponse), 409)]
    [ProducesResponseType(typeof(ApiErrorResponse), 413)]
    [ProducesResponseType(typeof(ApiErrorResponse), 415)]
    [ProducesResponseType(typeof(ApiErrorResponse), 503)]
    public async Task<ActionResult<CabinetAssetPhotoItem>> Upload([FromRoute(Name = "cabinet_id")] string id,
        [Required] IFormFile photo, [Required, FromForm(Name = "client_op_id")] string clientOpId, CancellationToken ct)
    {
        var (item, created) = await photos.UploadAsync<CabinetAssetPhoto, CabinetAssetPhotoItem>(id, Request.Form, ct);
        return created ? Created($"/api/v1/assets/cabinets/{id}/photos", item) : Ok(item);
    }

    [HttpGet]
    [Authorize(Policy = LuxMapPolicies.ReadNetwork)]
    [ProducesResponseType(typeof(PagedResult<CabinetAssetPhotoItem>), 200)]
    [ProducesResponseType(typeof(ApiErrorResponse), 400)]
    [ProducesResponseType(typeof(ApiErrorResponse), 404)]
    public Task<PagedResult<CabinetAssetPhotoItem>> List([FromRoute(Name = "cabinet_id")] string id, PageQuery page, CancellationToken ct)
        => photos.ListAsync<CabinetAssetPhoto, CabinetAssetPhotoItem>(id, page.ToPageRequest(), ct);

    [HttpGet("{photo_id}/original")]
    [Authorize(Policy = LuxMapPolicies.ReadNetwork)]
    [ProducesResponseType(typeof(FileResult), 200, "image/jpeg")]
    [ProducesResponseType(typeof(ApiErrorResponse), 404)]
    [ProducesResponseType(typeof(ApiErrorResponse), 503)]
    public Task<IActionResult> Original([FromRoute(Name = "cabinet_id")] string id, [FromRoute(Name = "photo_id")] string photoId, CancellationToken ct)
        => Media(id, photoId, false, ct);

    [HttpGet("{photo_id}/thumbnail")]
    [Authorize(Policy = LuxMapPolicies.ReadNetwork)]
    [ProducesResponseType(typeof(FileResult), 200, "image/jpeg")]
    [ProducesResponseType(typeof(ApiErrorResponse), 404)]
    [ProducesResponseType(typeof(ApiErrorResponse), 503)]
    public Task<IActionResult> Thumbnail([FromRoute(Name = "cabinet_id")] string id, [FromRoute(Name = "photo_id")] string photoId, CancellationToken ct)
        => Media(id, photoId, true, ct);

    private async Task<IActionResult> Media(string id, string photoId, bool thumbnail, CancellationToken ct)
    {
        var stream = await photos.OpenAsync<CabinetAssetPhoto>(id, photoId, thumbnail, ct);
        Response.Headers.CacheControl = "no-store";
        return File(stream, "image/jpeg");
    }

    [HttpDelete("{photo_id}")]
    [Authorize(Policy = LuxMapPolicies.ManageAssets)]
    [ProducesResponseType(204)]
    [ProducesResponseType(typeof(ApiErrorResponse), 404)]
    public async Task<IActionResult> Hide([FromRoute(Name = "cabinet_id")] string id, [FromRoute(Name = "photo_id")] string photoId, CancellationToken ct)
    {
        await photos.HideAsync<CabinetAssetPhoto>(id, photoId, ct);
        return NoContent();
    }
}
