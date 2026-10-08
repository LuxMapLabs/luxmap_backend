using Asp.Versioning;
using LuxMap.Modules.Telemetry.Entities;
using LuxMap.Persistence;
using LuxMap.Shared.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LuxMap.Api.Tests;

/// <summary>
/// A device-only endpoint that exists ONLY in the test assembly (LIGHT-CTRL 2a): the device endpoints themselves arrive in 2b, and
/// the scheme must be proven before anything is built on it. Loaded by <see cref="DeviceProbeFactory"/>.
/// </summary>
/// <remarks>
/// Its OWN controller, not <c>TestEndpointsController</c>: that one carries a class-level <c>[AllowAnonymous]</c>, which beats a
/// method-level <c>[Authorize]</c> (CLAUDE.md, ASP0026) and would make this probe prove nothing.
/// </remarks>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/_device-probe")]
public sealed class DeviceProbeController(ICommuneScopeAccessor scope, LuxMapDbContext db) : ControllerBase
{
    /// <summary>Who the device is, what scope it got, and which devices the commune query filter lets it see.</summary>
    [HttpGet]
    [Authorize(Policy = DeviceAuth.Policy)]
    public async Task<IActionResult> WhoAmIAsync()
        => Ok(new
        {
            node_id = User.FindFirst(DeviceAuth.NodeIdClaim)?.Value,
            communes = scope.Scope.CommuneIds,
            system_wide = scope.Scope.IsSystemWide,
            visible_nodes = await db.Set<IotNode>().AsNoTracking().Select(node => node.NodeId).ToListAsync(),
        });
}
