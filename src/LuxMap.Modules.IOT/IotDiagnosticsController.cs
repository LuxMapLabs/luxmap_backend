using Asp.Versioning;
using LuxMap.Shared.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LuxMap.Modules.IOT;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/iot/nodes/{nodeId}/diagnostics")]
[Authorize(Policy = LuxMapPolicies.ReadNetwork)]
public sealed class IotDiagnosticsController(IotDiagnosticsService diagnostics) : ControllerBase
{
    /// <summary>Read the latest testbed telemetry and evaluate it against the node's configured limits.</summary>
    [HttpGet]
    public Task<IotDiagnosticResult> GetAsync(string nodeId, CancellationToken ct)
        => diagnostics.GetAsync(nodeId, ct);
}
