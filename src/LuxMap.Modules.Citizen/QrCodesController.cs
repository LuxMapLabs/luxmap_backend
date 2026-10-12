using Asp.Versioning;
using LuxMap.Shared.Authorization;
using LuxMap.Shared.Contracts.Errors;
using LuxMap.Shared.Contracts.Paging;
using LuxMap.Shared.Http;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace LuxMap.Modules.Citizen;

/// <summary>
/// QR codes on poles, for the Manager (CITIZEN-QR P2a). <b>SELF-SIGNED</b> — drift "CITIZEN-QR", provisional until FW.
/// The public side (scanning a code, filing a report) is P2b and does not exist yet.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/qr-codes")]
[Authorize(Policy = LuxMapPolicies.ManageQrCodes)]
[ClientSurface(ClientSurface.Web)]
public sealed class QrCodesController(QrCodeService service, ICommuneScopeAccessor scopeAccessor) : ControllerBase
{
    /// <summary>Issue a code for a pole; the code in use on that pole, if any, is revoked.</summary>
    /// <remarks>The web app draws <c>url</c> as the QR image. Out of scope or unknown pole → 404.</remarks>
    [HttpPost]
    [ProducesResponseType<QrCodeItem>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<QrCodeItem>> IssueAsync(IssueQrCodeRequest request, CancellationToken ct)
    {
        var issued = await service.IssueAsync(request, ct);
        return StatusCode(StatusCodes.Status201Created, issued);
    }

    /// <summary>Take a code out of use. A second revoke is 409 <c>QR_CODE_ALREADY_REVOKED</c>.</summary>
    [HttpPost("{qrId}/revoke")]
    [ProducesResponseType<QrCodeItem>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status409Conflict)]
    public Task<QrCodeItem> RevokeAsync(string qrId, CancellationToken ct) => service.RevokeAsync(qrId, ct);

    /// <summary>Codes in the caller's communes; filter by <c>pole_id</c>, <c>active</c>, <c>commune_id</c>.</summary>
    [HttpGet]
    [ProducesResponseType<PagedResult<QrCodeItem>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status403Forbidden)]
    public Task<PagedResult<QrCodeItem>> ListAsync(
        [FromQuery(Name = "commune_id")] string[]? communeId,
        [FromQuery(Name = "pole_id")] string? poleId,
        [FromQuery] bool? active,
        PageQuery page,
        CancellationToken ct)
        => service.ListAsync(CommuneFilter.Narrow(scopeAccessor.Scope, communeId), poleId, active, page.ToPageRequest(), ct);
}
