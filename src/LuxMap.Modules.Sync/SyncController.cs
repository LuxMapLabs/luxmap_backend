using System.Net;
using Asp.Versioning;
using LuxMap.Shared.Authorization;
using LuxMap.Shared.Contracts.Errors;
using LuxMap.Shared.Http;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace LuxMap.Modules.Sync;

/// <summary>Offline work for field engineers (BE-43, Contract section 5.8).</summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/sync")]
public sealed class SyncController(SyncBundleService bundles, SyncPushService pushes) : ControllerBase
{
    /// <summary>
    /// Everything needed to work some roads offline: their segments and poles (map layers, poles with their note),
    /// the open faults on them, and the caller's open work orders touching them. Always a FULL snapshot.
    /// </summary>
    /// <remarks>
    /// <c>segment_id</c> repeats for several roads (at most 20); without it, the roads of the caller's open work
    /// orders. A segment out of scope is 404 <c>ASSET_NOT_FOUND</c>. <c>since</c> is refused: there is no delta,
    /// the phone replaces its cache for these roads (D-1).
    /// </remarks>
    [HttpGet("bundle")]
    [Authorize(Policy = LuxMapPolicies.SyncOffline)]
    [ProducesResponseType<SyncBundle>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status404NotFound)]
    public Task<SyncBundle> BundleAsync(
        [FromQuery(Name = "segment_id")] string[]? segmentIds,
        [FromQuery(Name = "since")] string? since,
        CancellationToken ct)
    {
        if (since is not null)
        {
            throw new LuxMapException(ErrorCodes.ValidationFailed, HttpStatusCode.BadRequest,
                "since is not supported: the bundle is always a full snapshot of the roads asked for.",
                new Dictionary<string, object?> { ["field"] = "since" });
        }

        return bundles.BundleAsync(segmentIds, ct);
    }

    /// <summary>
    /// Applies the offline queue in order. 200 with <c>applied[]</c>, <c>conflicts[]</c> (server wins, current
    /// state attached) and <c>rejected[]</c>; 400 only for a malformed envelope. Photos are not part of it.
    /// </summary>
    [HttpPost("push")]
    [Authorize(Policy = LuxMapPolicies.SyncOffline)]
    [ProducesResponseType<SyncPushResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
    public Task<SyncPushResult> PushAsync(SyncPushRequest request, CancellationToken ct)
        => pushes.PushAsync(request, User, ct);
}
