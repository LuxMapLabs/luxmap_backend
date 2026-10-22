using Asp.Versioning;
using LuxMap.Shared.Authorization;
using LuxMap.Shared.Http;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace LuxMap.Modules.Admin.Monitoring;

/// <summary>How the system is doing, for the system admin (BE-35): which jobs fail, how full the disk is, what each bucket holds.</summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/admin/system")]
[ClientSurface(ClientSurface.Web)]
public sealed class AdminSystemController(SystemStatusService service) : ControllerBase
{
    /// <summary>
    /// Background jobs, survey processing, disk, database and buckets in one request. System-wide — never split by commune.
    /// </summary>
    [HttpGet("status")]
    [Authorize(Policy = LuxMapPolicies.MonitorSystem)]
    [ProducesResponseType<SystemStatusResponse>(StatusCodes.Status200OK)]
    public Task<SystemStatusResponse> Status(CancellationToken ct) => service.GetAsync(ct);
}
