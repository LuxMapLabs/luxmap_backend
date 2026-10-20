using LuxMap.Modules.Survey.Entities;
using LuxMap.Modules.WorkOrders;
using LuxMap.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LuxMap.Modules.Survey.Review;

/// <summary>Answers <see cref="ISurveyFindingLookup"/> from the published history of each accepted run.</summary>
/// <remarks>
/// History, not observations: it holds the per-pole verdict the run published, after conflicting passes were
/// resolved. History, not faults: an out on a pole that already had an open lamp_out created no new fault.
/// </remarks>
public sealed class SurveyFindingLookup(LuxMapDbContext db) : ISurveyFindingLookup
{
    public async Task<SurveyFinding[]> FindingsAsync(string workOrderId, string communeId, CancellationToken ct)
    {
        var rows = await (from sweep in db.Set<SurveySweep>().AsNoTracking()
                          join history in db.Set<LuminanceHistory>().AsNoTracking() on sweep.SweepId equals history.SweepId
                          where sweep.WorkOrderId == workOrderId && sweep.Status == SweepStatus.Accepted
                              && sweep.AcceptedRunId == history.RunId && history.CommuneId == communeId
                          select new { history.PoleId, history.ClassifiedAs, history.DataSource, history.EvaluatedAt,
                              history.PublishedAt, history.SweepId }).ToArrayAsync(ct);
        // Latest first, THEN map: filtering out/dim first would let an older out outlive a newer normal.
        return rows.GroupBy(r => r.PoleId)
            .Select(g => g.OrderByDescending(r => r.EvaluatedAt).ThenByDescending(r => r.PublishedAt)
                .ThenByDescending(r => r.SweepId.Length).ThenByDescending(r => r.SweepId, StringComparer.Ordinal).First())
            .Select(r => SurveyPublicationRules.FaultFor(r.ClassifiedAs) is { } type ? new SurveyFinding(r.PoleId, type, r.DataSource) : null)
            .OfType<SurveyFinding>().ToArray();
    }
}
