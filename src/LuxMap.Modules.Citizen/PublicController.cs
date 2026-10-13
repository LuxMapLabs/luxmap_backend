using Asp.Versioning;
using LuxMap.Shared.Contracts.Errors;
using LuxMap.Shared.Http;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace LuxMap.Modules.Citizen;

/// <summary>Body of <c>POST /public/qr/lookup</c>.</summary>
public sealed record PublicQrLookupRequest
{
    public string? Code { get; init; }
}

/// <summary>
/// CITIZEN-QR P2b — the only endpoints a citizen reaches, with no account (D-R1). <b>SELF-SIGNED</b> — drift "CITIZEN-QR P2b".
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <c>[AllowAnonymous]</c> sits on EACH action, never on the class (the <c>/auth/me</c> trap): an endpoint added here later
/// must stay closed unless someone opens it on purpose — and lists it in <c>AnonymousEndpointTests</c>.
/// </para>
/// <para>
/// The code travels in the BODY, never in the path (D-16): the request log records the path, and a code in it would be a working
/// key to the form sitting in every log line. The page URL <c>/r/{code}</c> belongs to the web app, not to this API.
/// </para>
/// </remarks>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/public")]
[ClientSurface(ClientSurface.Web)]
public sealed class PublicController(PublicReportService service) : ControllerBase
{
    /// <summary>Every photo at its default cap, plus room for the text fields. A raised <c>Citizen:</c> limit cannot pass it.</summary>
    public const long MaxRequestBytes = 3 * 8 * 1024 * 1024 + 1024 * 1024;

    /// <summary>What the scanned code points at: the road and the commune, to show the citizen they are at the right pole.</summary>
    /// <remarks>Malformed, unknown and revoked codes are one and the same 404 <c>PUBLIC_QR_NOT_FOUND</c>.</remarks>
    [HttpPost("qr/lookup")]
    [AllowAnonymous]
    [EnableRateLimiting(LuxMapRateLimits.PublicLookup)]
    [ProducesResponseType<PublicQrInfo>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status429TooManyRequests)]
    public Task<PublicQrInfo> LookupAsync(PublicQrLookupRequest request, CancellationToken ct) => service.LookupAsync(request.Code, ct);

    /// <summary>File a report about the pole the code is on. Goes to the Manager's queue — never straight to a fault (D-R1).</summary>
    /// <remarks>
    /// multipart: <c>code</c>, <c>kind</c> (<c>lamp_out | lamp_dim | pole_damaged | other</c>), <c>description?</c> (≤ 1000),
    /// <c>lat?</c> + <c>lng?</c> together, <c>client_op_id</c> (UUID), <c>photos</c> (0–3 JPEG). The same <c>client_op_id</c> with the
    /// same code and content answers the first receipt; under different content it is 409 <c>IDEMPOTENCY_CONFLICT</c>. Photos over
    /// the night's allowance are dropped and the text kept (<c>photos_kept</c> says how many stayed). Too many photos being cleaned
    /// at once → 503 <c>SERVER_BUSY</c>.
    /// </remarks>
    [HttpPost("reports")]
    [AllowAnonymous]
    [EnableRateLimiting(LuxMapRateLimits.PublicReport)]
    [RequestSizeLimit(MaxRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxRequestBytes)]
    [Consumes("multipart/form-data")]
    [ProducesResponseType<PublicReportReceipt>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status415UnsupportedMediaType)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<PublicReportReceipt>> SubmitAsync(
        [FromForm] string? code,
        [FromForm] string? kind,
        [FromForm] string? description,
        [FromForm] double? lat,
        [FromForm] double? lng,
        [FromForm(Name = "client_op_id")] Guid? clientOpId,
        [FromForm] IFormFileCollection? photos,
        CancellationToken ct)
    {
        // The form reader has already buffered the files (to disk past 64 KB); each is opened, hashed and cleaned as a stream.
        var uploads = (photos ?? new FormFileCollection()).Select(file => new PhotoUpload(file.Length, file.OpenReadStream)).ToArray();
        var receipt = await service.SubmitAsync(new PublicReportInput(code, kind, description, lat, lng, clientOpId, uploads), ct);
        return StatusCode(StatusCodes.Status202Accepted, receipt);
    }
}
