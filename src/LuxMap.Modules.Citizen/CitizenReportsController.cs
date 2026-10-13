using Asp.Versioning;
using LuxMap.Modules.Citizen.Entities;
using LuxMap.Shared.Authorization;
using LuxMap.Shared.Contracts.Errors;
using LuxMap.Shared.Contracts.Paging;
using LuxMap.Shared.Http;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace LuxMap.Modules.Citizen;

/// <summary>
/// The Manager's queue of citizen reports (CITIZEN-QR P2c). <b>SELF-SIGNED</b> — drift "CITIZEN-QR P2c", provisional until FW.
/// Reading is <c>ReadCitizenReports</c> (Superior, Manager, System Admin); deciding and opening photos is <c>ReviewCitizenReports</c>
/// (Manager). A report outside the caller's communes is 404.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/citizen-reports")]
[ClientSurface(ClientSurface.Web)]
// What a response holds depends on the caller's role (photo links) and communes: never kept by a shared or browser cache.
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class CitizenReportsController(
    CitizenReviewService service, ICommuneScopeAccessor scopeAccessor, IAuthorizationService authorization) : ControllerBase
{
    /// <summary>The queue, newest send first. <c>status</c> is a comma list; absent means <c>pending</c>.</summary>
    [HttpGet]
    [Authorize(Policy = LuxMapPolicies.ReadCitizenReports)]
    [ProducesResponseType<PagedResult<CitizenReportItem>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status403Forbidden)]
    public Task<PagedResult<CitizenReportItem>> ListAsync(
        [FromQuery] string? status,
        [FromQuery(Name = "commune_id")] string[]? communeId,
        [FromQuery(Name = "pole_id")] string? poleId,
        PageQuery page,
        CancellationToken ct)
    {
        var statuses = WireEnum.ParseCsv<CitizenReportStatus>(status, "status") ?? [CitizenReportStatus.Pending];
        return service.ListAsync(CommuneFilter.Narrow(scopeAccessor.Scope, communeId), statuses, poleId, page.ToPageRequest(), ct);
    }

    /// <summary>
    /// One report: the pole, its open faults, every send. Photos (and their AI hints) are listed only for a caller who may open them;
    /// for that caller, photos without a hint are run through the ON / OFF model first — a hint, never a decision.
    /// </summary>
    [HttpGet("{reportId}")]
    [Authorize(Policy = LuxMapPolicies.ReadCitizenReports)]
    [ProducesResponseType<CitizenReportDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status404NotFound)]
    public async Task<CitizenReportDetail> DetailAsync(string reportId, CancellationToken ct)
        => await service.DetailAsync(reportId, await CanReviewAsync(), ct);

    [HttpGet("{reportId}/photos/{photoId}/thumbnail")]
    [Authorize(Policy = LuxMapPolicies.ReviewCitizenReports)]
    [Produces("image/jpeg")]
    public async Task<IActionResult> ThumbnailAsync(string reportId, string photoId, CancellationToken ct)
        => File(await service.OpenPhotoAsync(reportId, photoId, thumbnail: true, ct), "image/jpeg");

    /// <summary>The stored photo — already stripped of EXIF / GPS when it was received.</summary>
    [HttpGet("{reportId}/photos/{photoId}/original")]
    [Authorize(Policy = LuxMapPolicies.ReviewCitizenReports)]
    [Produces("image/jpeg")]
    public async Task<IActionResult> OriginalAsync(string reportId, string photoId, CancellationToken ct)
        => File(await service.OpenPhotoAsync(reportId, photoId, thumbnail: false, ct), "image/jpeg");

    /// <summary>
    /// A new <c>field_report</c> fault, confirmed by the caller. 409 <c>CITIZEN_REPORT_NOT_ACCEPTABLE</c> when no send says the lamp
    /// is out or dim; 409 <c>OPEN_FAULT_EXISTS</c> when the pole already has an open fault of that type (merge instead).
    /// </summary>
    [HttpPost("{reportId}/accept")]
    [Authorize(Policy = LuxMapPolicies.ReviewCitizenReports)]
    [ProducesResponseType<CitizenReportDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status409Conflict)]
    public Task<CitizenReportDetail> AcceptAsync(string reportId, AcceptCitizenReportRequest request, CancellationToken ct)
        => service.AcceptAsync(reportId, request, ct);

    /// <summary>Attach to an open fault on the same pole; 409 <c>FAULT_NOT_MERGEABLE</c> otherwise.</summary>
    [HttpPost("{reportId}/merge")]
    [Authorize(Policy = LuxMapPolicies.ReviewCitizenReports)]
    [ProducesResponseType<CitizenReportDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status409Conflict)]
    public Task<CitizenReportDetail> MergeAsync(string reportId, MergeCitizenReportRequest request, CancellationToken ct)
        => service.MergeAsync(reportId, request, ct);

    /// <summary>Not a fault; <c>reason</c> is required. A decided report is 409 <c>CITIZEN_REPORT_ALREADY_DECIDED</c>.</summary>
    [HttpPost("{reportId}/reject")]
    [Authorize(Policy = LuxMapPolicies.ReviewCitizenReports)]
    [ProducesResponseType<CitizenReportDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status409Conflict)]
    public Task<CitizenReportDetail> RejectAsync(string reportId, RejectCitizenReportRequest request, CancellationToken ct)
        => service.RejectAsync(reportId, request, ct);

    /// <summary>Asked through the same policy the photo and decision endpoints use, so listing and opening never disagree.</summary>
    private async Task<bool> CanReviewAsync()
        => (await authorization.AuthorizeAsync(User, LuxMapPolicies.ReviewCitizenReports)).Succeeded;
}
