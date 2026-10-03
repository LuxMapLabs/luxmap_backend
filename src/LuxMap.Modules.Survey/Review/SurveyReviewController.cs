using Asp.Versioning;
using LuxMap.Shared.Authorization;
using LuxMap.Shared.Contracts.Paging;
using LuxMap.Shared.Http;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LuxMap.Modules.Survey.Review;

/// <summary>SELF-SIGNED BE-15 P2c; temporary until FW confirmation.</summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/sweeps")]
public sealed class SurveyReviewController(SurveyReviewService service) : ControllerBase
{
    [HttpGet("{id}/results")]
    [Authorize(Policy = LuxMapPolicies.ReadSurveys)]
    public async Task<ActionResult<PagedResult<SurveyResultItem>>> Results(string id,
        [FromQuery(Name = "run_id")] long? runId, PageQuery page, CancellationToken ct)
        => Ok(await service.Results(id, runId, page.ToPageRequest(), ct));

    [HttpPost("{id}/review")]
    [Authorize(Policy = LuxMapPolicies.ReviewSurveys)]
    public async Task<ActionResult<ReviewSweepResponse>> Review(string id, ReviewSweepRequest request, CancellationToken ct)
        => Ok(await service.Review(id, request, ct));
}

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/frames")]
public sealed class SurveyFramesController(SurveyReviewService service) : ControllerBase
{
    [HttpGet("{frame_id}/thumbnail")]
    [Authorize(Policy = LuxMapPolicies.ReadSurveys)]
    [Produces("image/jpeg")]
    public async Task<IActionResult> Thumbnail(string frame_id, CancellationToken ct)
        => File(await service.Thumbnail(frame_id, ct), "image/jpeg");
}
