using LuxMap.Modules.Survey.Entities;
using LuxMap.Modules.WorkOrders;
using LuxMap.Modules.WorkOrders.Entities;
using LuxMap.Persistence;
using LuxMap.Shared.Contracts.Enums;
using Microsoft.EntityFrameworkCore;

namespace LuxMap.Modules.Survey.Review;

/// <summary>Replays accepted sweep history and confirmed pole photos/clips belonging to the survey order.</summary>
/// <remarks>
/// Results, not faults: an out on a pole that already had an open lamp_out created no new fault.
/// Replay is local to the order, regardless of whether a photo changed the global current status.
/// </remarks>
public sealed class SurveyFindingLookup(LuxMapDbContext db) : ISurveyFindingLookup
{
    public async Task<SurveyFinding[]> FindingsAsync(string workOrderId, string communeId, CancellationToken ct)
    {
        var history = await (from sweep in db.Set<SurveySweep>().AsNoTracking()
                             join row in db.Set<LuminanceHistory>().AsNoTracking() on sweep.SweepId equals row.SweepId
                             where sweep.WorkOrderId == workOrderId && sweep.Status == SweepStatus.Accepted
                                 && sweep.AcceptedRunId == row.RunId && row.CommuneId == communeId
                             select row).ToArrayAsync(ct);
        var photos = await db.Set<WorkOrderPolePhoto>().AsNoTracking()
            .Where(p => p.WorkOrderId == workOrderId && p.CommuneId == communeId && p.Decision == "confirm")
            .ToArrayAsync(ct);
        // LUX-BASELINE D-13: a photo whose protocol reading was judged against a stable baseline carries that verdict.
        var photoIds = photos.Select(p => p.PhotoId).ToArray();
        var judged = await db.Set<LuxEvaluation>().AsNoTracking()
            .Where(e => photoIds.Contains(e.PhotoId) && e.CvState == "on" && e.DimEvaluationEligible && e.ExpectedRatio == null)
            .ToDictionaryAsync(e => e.PhotoId, e => e.ClassifiedAs, ct);
        return Merge(history, photos, judged);
    }

    /// <summary>
    /// Fold observations oldest first; a bare ON preserves a preceding dim classification (WSC-6), but a photo whose lux reading
    /// was judged against its pole's baseline (<paramref name="judgedPhotos"/>, LUX-BASELINE D-13) counts as that verdict.
    /// </summary>
    public static SurveyFinding[] Merge(IEnumerable<LuminanceHistory> history, IEnumerable<WorkOrderPolePhoto> confirmedPhotos,
        IReadOnlyDictionary<string, FixtureStatus>? judgedPhotos = null)
    {
        var observations = history.Select(h => new Observation(h.PoleId, h.DataSource, h.EvaluatedAt,
                h.PublishedAt, h.SweepId, h.ClassifiedAs, null, null))
            .Concat(confirmedPhotos.Select(p => new Observation(p.PoleId, p.DataSource, p.CapturedAt,
                p.DecidedAt!.Value, p.PhotoId, null, p.FinalCvState ?? p.CvState,
                judgedPhotos?.TryGetValue(p.PhotoId, out var judged) == true ? judged : null)));
        var findings = new List<SurveyFinding>();
        foreach (var pole in observations.GroupBy(o => o.PoleId))
        {
            var status = FixtureStatus.Unknown;
            var source = default(DataSource);
            // Sweep last at equal capture time, then the existing sweep tie-break / photo decision tie-break.
            foreach (var observation in pole.OrderBy(o => o.At).ThenBy(o => o.SweepStatus.HasValue)
                .ThenBy(o => o.PublishedAt).ThenBy(o => o.Id.Length).ThenBy(o => o.Id, StringComparer.Ordinal))
            {
                var next = observation.SweepStatus ?? observation.Judged
                    ?? SurveyPublicationRules.PhotoStatus(observation.CvState!, status);
                // A photo that leaves the status as it was (ON on dim, OFF on out) raised no fault of its own: the open
                // fault to carry is the one the earlier observation raised, so keep that observation's data source.
                if (observation.SweepStatus is not null || observation.Judged is not null || next != status) source = observation.DataSource;
                status = next;
            }
            if (SurveyPublicationRules.FaultFor(status) is { } type)
                findings.Add(new(pole.Key, type, source));
        }
        return findings.ToArray();
    }

    private sealed record Observation(string PoleId, DataSource DataSource, DateTime At, DateTime PublishedAt,
        string Id, FixtureStatus? SweepStatus, string? CvState, FixtureStatus? Judged);
}
