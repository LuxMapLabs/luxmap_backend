using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;
using LuxMap.Modules.Assets.Entities;
using LuxMap.Modules.Survey.Entities;
using LuxMap.Modules.WorkOrders.Entities;
using LuxMap.Persistence;
using LuxMap.Shared.Authorization;
using Microsoft.EntityFrameworkCore;

namespace LuxMap.Modules.Survey.Review;

/// <summary>
/// Who may see a survey sweep and the images cut from it. ONE definition, used by the review screen,
/// the thumbnail endpoint and the pole detail's <c>recent_frames</c>, so a frame is never LISTED to a
/// caller who would then get a 404 when opening it.
/// </summary>
/// <remarks>
/// A frame shows a stretch of road, so seeing it needs more than the parent work order: every commune
/// the run touched — observed poles, expected poles of the snapshot and their CURRENT communes — must
/// be in the caller's scope. Work-order assignee scope comes from the query filter on
/// <c>WorkOrder</c>, applied inside <see cref="VisibleSweeps"/>.
/// </remarks>
public sealed class SurveyMediaAccess(LuxMapDbContext db, ICommuneScopeAccessor scope)
{
    private sealed class CommuneRow { [Column("commune_id")] public string CommuneId { get; set; } = null!; }

    /// <summary>Sweeps whose work order, and every segment on it, the caller can see.</summary>
    public IQueryable<SurveySweep> VisibleSweeps() => db.Set<SurveySweep>().Where(s => db.Set<WorkOrder>().Any(w =>
        w.WorkOrderId == s.WorkOrderId && !db.Set<WorkOrderSegment>().Any(link => link.WorkOrderId == w.WorkOrderId
            && !db.Set<RoadSegment>().Any(r => r.SegmentId == link.SegmentId))));

    /// <summary>
    /// True when the caller's scope covers every commune of the run. Only scope identifiers of an already
    /// authorized parent are read: a filtered observation query alone would silently publish half a
    /// cross-commune run, and no out-of-scope payload is returned.
    /// </summary>
    public async Task<bool> CoversWholeRun(SurveyProcessingRun run, CancellationToken ct)
    {
        var communes = await db.Database.SqlQuery<CommuneRow>($"SELECT DISTINCT commune_id FROM pole_observation WHERE run_id = {run.RunId}").ToArrayAsync(ct);
        var expected = SurveyPublicationRules.ExpectedPoles(run.GisSnapshot);
        var ids = expected.Select(p => p.PoleId).Distinct().ToArray();
        var current = await db.Database.SqlQuery<CommuneRow>($"SELECT DISTINCT commune_id FROM pole WHERE pole_id = ANY({ids})").ToArrayAsync(ct);
        return SurveyPublicationRules.RequiredCommunes(run.GisSnapshot, communes.Select(x => x.CommuneId), current.Select(x => x.CommuneId))
            .All(scope.Scope.Allows);
    }

    /// <summary>True when the caller may open the images of this (already visible) sweep.</summary>
    public async Task<bool> CanReadMedia(string sweepId, CancellationToken ct)
    {
        var runs = await db.Set<SurveyProcessingRun>().Where(r => r.SweepId == sweepId).ToArrayAsync(ct);
        foreach (var run in runs)
        {
            if (!await CoversWholeRun(run, ct)) return false;
            using var snapshot = JsonDocument.Parse(run.GisSnapshot);
            if (snapshot.RootElement.TryGetProperty("communes", out var communes)
                && communes.EnumerateArray().Any(x => !scope.Scope.Allows(x.GetString()!)))
                return false;
        }
        return true;
    }
}
