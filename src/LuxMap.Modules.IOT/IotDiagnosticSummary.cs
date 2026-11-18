using System.Text.Json;
using LuxMap.Modules.Faults.Entities;
using LuxMap.Modules.IOT.Entities;
using LuxMap.Persistence;
using LuxMap.Shared.Contracts.Enums;
using LuxMap.Shared.Contracts.Paging;
using LuxMap.Shared.Serialization;
using Microsoft.EntityFrameworkCore;

namespace LuxMap.Modules.IOT;

public sealed record IotDiagnosticSummaryItem(
    string NodeId, string? FeederId, string DiagnosticStatus, string[] AnomalyCodes,
    int? EstimatedLit, int? EstimatedMissing, string[] FaultIds, DateTime? EvaluatedAt);

public sealed class IotDiagnosticSummary(LuxMapDbContext db, TimeProvider clock)
{
    public async Task<PagedResult<IotDiagnosticSummaryItem>> ListAsync(PageRequest page, CancellationToken ct)
    {
        var nodes = db.Set<IotNode>().AsNoTracking().Where(n => n.CabinetId != null);
        var total = await nodes.CountAsync(ct);
        var ids = await nodes.OrderBy(n => n.CreatedAt).ThenBy(n => n.NodeId.Length).ThenBy(n => n.NodeId)
            .Skip(page.Skip).Take(page.PageSize).Select(n => n.NodeId).ToListAsync(ct);
        var states = await db.Set<IotDiagnosticState>().AsNoTracking()
            .Where(s => ids.Contains(s.NodeId)).ToDictionaryAsync(s => s.NodeId, ct);
        var faults = await db.Set<Fault>().AsNoTracking()
            .Where(f => f.OriginNodeId != null && ids.Contains(f.OriginNodeId) && f.IotAnomalyCode != null &&
                (f.FaultStatus == FaultStatus.Detected || f.FaultStatus == FaultStatus.Confirmed ||
                 f.FaultStatus == FaultStatus.InProgress || f.FaultStatus == FaultStatus.Resolved))
            .OrderBy(f => f.CreatedAt).ThenBy(f => f.FaultId.Length).ThenBy(f => f.FaultId)
            .Select(f => new { f.FaultId, f.OriginNodeId }).ToListAsync(ct);
        var now = clock.GetUtcNow().UtcDateTime;
        var items = ids.Select(id =>
        {
            states.TryGetValue(id, out var state);
            var recent = state is not null && state.EvaluatedAt <= now && now - state.EvaluatedAt <= TimeSpan.FromSeconds(90);
            var result = recent ? JsonSerializer.Deserialize<IotDiagnosticResult>(state!.Result, LuxMapJsonOptions.Default) : null;
            return new IotDiagnosticSummaryItem(id, state?.FeederId,
                state is null ? "unconfigured" : recent ? state.DiagnosticStatus : "source_unavailable",
                result?.Anomalies.Select(a => a.Code).Distinct().ToArray() ?? [],
                result?.LampCount?.EstimatedLit, result?.LampCount?.EstimatedMissing,
                faults.Where(f => f.OriginNodeId == id).Select(f => f.FaultId).ToArray(), state?.EvaluatedAt);
        }).ToArray();
        return PagedResult<IotDiagnosticSummaryItem>.From(page, total, items);
    }
}
