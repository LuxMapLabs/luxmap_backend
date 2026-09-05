using System.Net;
using Asp.Versioning;
using LuxMap.Modules.Identity.Auth;
using LuxMap.Shared.Contracts.Enums;
using LuxMap.Shared.Contracts.Errors;
using LuxMap.Shared.Contracts.Paging;
using LuxMap.Shared.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace LuxMap.Modules.Survey.LuxReadings;

/// <summary>
/// Contract section 2.9 — lux readings (BE-42).
/// </summary>
/// <remarks>
/// No role policy is attached. The four policies from BE-08 are still unused in production code
/// because nobody has decided which roles may write which assets; guessing one here would set a
/// precedent by accident. Territorial scope IS enforced — through the pole lookup and the
/// <c>SaveChanges</c> guard.
/// </remarks>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}")]
public sealed class LuxReadingsController(LuxReadingService service) : ControllerBase
{
    /// <summary>
    /// Records a measurement. A repeated <c>client_op_id</c> returns <b>200</b> with the existing
    /// record — Contract section 5.8: retrying is normal offline behaviour, not an error.
    /// </summary>
    [HttpPost("lux-readings")]
    [ProducesResponseType<LuxReadingResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<LuxReadingResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<LuxReadingResponse>> CreateAsync(
        [FromBody] CreateLuxReadingRequest request,
        CancellationToken cancellationToken)
    {
        RejectServerOwnedFields(request);

        var (created, reading) = await service.CreateAsync(request, CurrentUserId(), cancellationToken);

        return created
            ? StatusCode(StatusCodes.Status201Created, reading)
            : Ok(reading);
    }

    /// <summary>One pole's series, oldest first. Paged — see <see cref="LuxReadingService.ForPoleAsync"/>.</summary>
    [HttpGet("poles/{poleId}/lux-readings")]
    [ProducesResponseType<PagedResult<LuxReadingResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PagedResult<LuxReadingResponse>>> ForPoleAsync(
        string poleId,
        PageQuery page,
        CancellationToken cancellationToken)
        => Ok(await service.ForPoleAsync(poleId, page.ToPageRequest(), cancellationToken));

    /// <summary>
    /// The bulk endpoint CV-12 pulls from. Every item carries <c>nearest_luminance</c>, currently
    /// always <c>null</c> — the source table arrives with BE-15/BE-17.
    /// </summary>
    [HttpGet("lux-readings")]
    [ProducesResponseType<PagedResult<LuxReadingWithLuminanceResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<LuxReadingWithLuminanceResponse>>> SearchAsync(
        [FromQuery(Name = "pole_id")] string? poleId,
        [FromQuery(Name = "from")] DateTime? from,
        [FromQuery(Name = "to")] DateTime? to,
        [FromQuery(Name = "data_source")] DataSource? dataSource,
        PageQuery page,
        CancellationToken cancellationToken)
        => Ok(await service.SearchAsync(poleId, from, to, dataSource, page.ToPageRequest(), cancellationToken));

    /// <summary>
    /// Refuses a body that sets something the server owns.
    /// </summary>
    /// <remarks>
    /// Loudly, not silently. A client that sent <c>commune_id</c> and got 201 back would reasonably
    /// believe it chose the commune — and it did not.
    /// </remarks>
    private static void RejectServerOwnedFields(CreateLuxReadingRequest request)
    {
        var offending = new List<string>();

        if (!string.IsNullOrWhiteSpace(request.LuxId))
        {
            offending.Add("lux_id");
        }

        if (!string.IsNullOrWhiteSpace(request.CommuneId))
        {
            offending.Add("commune_id");
        }

        if (offending.Count == 0)
        {
            return;
        }

        throw new LuxMapException(
            ErrorCodes.ServerOwnedField,
            HttpStatusCode.BadRequest,
            "These fields are set by the server and must not be sent: "
            + string.Join(", ", offending)
            + ". The id comes from the database and the commune is taken from the pole.",
            new Dictionary<string, object?> { ["fields"] = offending.ToArray() });
    }

    /// <summary>
    /// The signed-in user, for <c>measured_by</c> — the same shape as <c>reported_by</c> in
    /// Contract section 2.8: the server sets it, the client cannot.
    /// </summary>
    private string CurrentUserId()
        => User.FindFirst(AuthClaims.Subject)?.Value
           ?? throw new LuxMapException(
               ErrorCodes.Unauthenticated,
               HttpStatusCode.Unauthorized,
               "The access token carries no subject claim.");
}
