using System.Globalization;
using System.Net;
using Asp.Versioning;
using LuxMap.Shared.Authorization;
using LuxMap.Shared.Contracts.Errors;
using LuxMap.Shared.Http;
using LuxMap.Shared.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace LuxMap.Modules.Identity.Profiles;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}")]
public sealed class AvatarController(AvatarService avatars) : ControllerBase
{
    [HttpPost("auth/me/avatar")]
    [Authorize(Policy = LuxMapPolicies.ManageOwnProfile)]
    [ProducesResponseType<ApiErrorResponse>(400)]
    [ProducesResponseType<ApiErrorResponse>(401)]
    [ProducesResponseType<ApiErrorResponse>(403)]
    [ProducesResponseType<ApiErrorResponse>(404)]
    [ProducesResponseType<ApiErrorResponse>(413)]
    [ProducesResponseType<ApiErrorResponse>(415)]
    [ProducesResponseType<ApiErrorResponse>(503)]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(AvatarLimits.MaxRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = AvatarLimits.MaxRequestBytes)]
    [ProducesResponseType<AvatarResponse>(200)]
    public async Task<ActionResult<AvatarResponse>> Upload(IFormFile? file, CancellationToken ct)
    {
        if (file is null)
            throw new LuxMapException(ErrorCodes.ValidationFailed, HttpStatusCode.BadRequest, "A file is required.",
                new Dictionary<string, object?> { ["field"] = "file" });
        await using var content = file.OpenReadStream();
        return Ok(await avatars.UploadAsync(content, ct));
    }

    [HttpGet("auth/me/avatar")]
    [Authorize(Policy = LuxMapPolicies.ManageOwnProfile)]
    [ProducesResponseType<ApiErrorResponse>(400)]
    [ProducesResponseType<ApiErrorResponse>(401)]
    [ProducesResponseType<ApiErrorResponse>(403)]
    [ProducesResponseType<ApiErrorResponse>(404)]
    [ProducesResponseType<ApiErrorResponse>(503)]
    [ProducesResponseType(typeof(FileResult), 200, "image/jpeg")]
    [ProducesResponseType(304)]
    public Task<IActionResult> Own(CancellationToken ct) => ReadAsync(avatars.OwnUserId, ct);

    [HttpGet("admin/users/{id}/avatar")]
    [Authorize(Policy = LuxMapPolicies.ManageUsers)]
    [ProducesResponseType<ApiErrorResponse>(400)]
    [ProducesResponseType<ApiErrorResponse>(401)]
    [ProducesResponseType<ApiErrorResponse>(403)]
    [ProducesResponseType<ApiErrorResponse>(404)]
    [ProducesResponseType<ApiErrorResponse>(503)]
    [ProducesResponseType(typeof(FileResult), 200, "image/jpeg")]
    [ProducesResponseType(304)]
    public Task<IActionResult> Admin(string id, CancellationToken ct) => ReadAsync(id, ct);

    [HttpDelete("auth/me/avatar")]
    [Authorize(Policy = LuxMapPolicies.ManageOwnProfile)]
    [ProducesResponseType<ApiErrorResponse>(401)]
    [ProducesResponseType<ApiErrorResponse>(403)]
    [ProducesResponseType(204)]
    public async Task<IActionResult> Remove(CancellationToken ct)
    {
        await avatars.RemoveAsync(ct);
        return NoContent();
    }

    private async Task<IActionResult> ReadAsync(string id, CancellationToken ct)
    {
        var user = await avatars.FindAsync(id, ct);
        var etag = new EntityTagHeaderValue("\"" + user.AvatarUpdatedAt!.Value.Ticks.ToString(CultureInfo.InvariantCulture) + "\"");
        var matches = Request.GetTypedHeaders().IfNoneMatch?.Any(tag => tag.Tag == "*" || tag.Compare(etag, useStrongComparison: false)) == true;
        // Open before setting success headers: storage failure is an ordinary JSON error.
        var content = matches ? null : await avatars.OpenAsync(user, ct);
        Response.Headers.CacheControl = "private, max-age=86400";
        Response.Headers.ETag = etag.ToString();
        Response.Headers.XContentTypeOptions = "nosniff";
        return matches ? StatusCode(304) : File(content!, StorageKeys.JpegContentType);
    }
}
