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
/// Controller CHỈ tồn tại trong assembly test — nạp vào host qua ApplicationPart. Ứng dụng thật
/// không có endpoint nghiệp vụ nào ở BE-08.
/// <para>
/// Viết đúng như BE-14/BE-20/BE-40 sẽ viết, để test chứng minh được cơ chế hoạt động với cách
/// dùng thật chứ không phải cách dùng riêng cho test.
/// </para>
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/_scope")]
public sealed class ScopeTestController(
    LuxMapDbContext dbContext,
    ICommuneScopeAccessor scopeAccessor) : ControllerBase
{
    /// <summary>Không gọi gì thêm — global filter tự giới hạn theo claim.</summary>
    [HttpGet("probes")]
    public async Task<IActionResult> ListAsync(
        [FromQuery(Name = "commune_id")] string[]? communeId,
        CancellationToken cancellationToken)
    {
        var query = dbContext.Set<ScopeProbe>().AsNoTracking();

        // Bước tường minh DUY NHẤT: kiểm tham số client truyền lên. Ngoài phạm vi → 403.
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

    /// <summary>Lookup theo ID: filter nằm trong WHERE nên ngoài phạm vi ra null → 404.</summary>
    [HttpGet("probes/{id:long}")]
    public async Task<IActionResult> GetAsync(long id, CancellationToken cancellationToken)
    {
        var probe = await dbContext.Set<ScopeProbe>().AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);

        return probe is null
            ? NotFound()
            : Ok(new { probe.Id, probe.Label, probe.CommuneId });
    }

    /// <summary>Đọc lại claim từ ClaimsPrincipal để chứng minh MapInboundClaims đã xử lý.</summary>
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
