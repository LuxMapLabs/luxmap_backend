using Asp.Versioning;
using LuxMap.Api.Authorization;
using LuxMap.Persistence;
using LuxMap.Shared.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LuxMap.Api.Tests;

/// <summary>
/// A controller that exists ONLY in the test assembly, loaded through an ApplicationPart. The real
/// application ships no business endpoints in BE-08.
/// <para>
/// Written exactly the way BE-14/BE-20/BE-40 will write theirs, so the tests prove the mechanism
/// against real usage rather than a test-only shortcut.
/// </para>
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/_scope")]
public sealed class ScopeTestController(
    LuxMapDbContext dbContext,
    ICommuneScopeAccessor scopeAccessor) : ControllerBase
{
    /// <summary>Nothing extra to call — the global filter restricts by claim on its own.</summary>
    [HttpGet("probes")]
    public async Task<IActionResult> ListAsync(
        [FromQuery(Name = "commune_id")] string[]? communeId,
        CancellationToken cancellationToken)
    {
        var query = dbContext.Set<ScopeProbe>().AsNoTracking();

        // The ONLY explicit step: validate the client-supplied parameter. Out of scope → 403.
        var narrowed = CommuneFilter.Narrow(scopeAccessor.Scope, communeId);
        if (narrowed is not null)
        {
            query = query.Where(probe => narrowed.Contains(probe.CommuneId));
        }

        var items = await query
            .OrderBy(probe => probe.Id)
            .Select(probe => new { probe.Id, probe.Label, probe.CommuneId })
            .ToListAsync(cancellationToken);

        return Ok(items);
    }

    /// <summary>Lookup by id: the filter lives in the WHERE clause, so out of scope yields null → 404.</summary>
    [HttpGet("probes/{id:long}")]
    public async Task<IActionResult> GetAsync(long id, CancellationToken cancellationToken)
    {
        var probe = await dbContext.Set<ScopeProbe>().AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);

        return probe is null
            ? NotFound()
            : Ok(new { probe.Id, probe.Label, probe.CommuneId });
    }

    /// <summary>Reads the claims back from ClaimsPrincipal to prove MapInboundClaims was handled.</summary>
    [HttpGet("whoami")]
    public IActionResult WhoAmI() => Ok(new
    {
        sub = User.FindFirst("sub")?.Value,
        role = User.FindFirst("role")?.Value,
        commune_ids = User.FindAll("commune_ids").Select(c => c.Value).ToArray(),
        is_system_wide = scopeAccessor.Scope.IsSystemWide,
        scope_commune_ids = scopeAccessor.Scope.CommuneIds,
    });

    [HttpGet("engineer-only")]
    [Authorize(Policy = LuxMapPolicies.MaintenanceEngineer)]
    public IActionResult EngineerOnly() => Ok(new { ok = true });

    [HttpGet("admin-only")]
    [Authorize(Policy = LuxMapPolicies.Administrator)]
    public IActionResult AdminOnly() => Ok(new { ok = true });

    [HttpGet("open")]
    [AllowAnonymous]
    public IActionResult Open() => Ok(new { ok = true });
}
