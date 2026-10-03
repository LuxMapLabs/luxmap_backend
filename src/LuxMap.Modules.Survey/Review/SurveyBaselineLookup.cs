using LuxMap.Modules.Survey.Entities;
using LuxMap.Modules.Assets.Entities;
using LuxMap.Modules.Survey.Processing.Frames;
using LuxMap.Persistence;
using LuxMap.Shared.Contracts.Enums;
using Microsoft.EntityFrameworkCore;

namespace LuxMap.Modules.Survey.Review;

public sealed record BaselineQuery(string PoleId, string Direction, string SweepId, DateTime Before, DataSource DataSource);
public sealed record BaselineReference(long Id, double Value);

/// <summary>Uses the caller's scoped context, including the worker's finite job scope.</summary>
public sealed class SurveyBaselineLookup(LuxMapDbContext db) : ISurveyBaselineLookup
{
    public async Task<BaselineReference?> FindAsync(BaselineQuery query, CancellationToken ct)
    {
        var fixtureId = await db.Set<Fixture>().Where(f => f.PoleId == query.PoleId && f.RemovedDate == null)
            .Select(f => f.FixtureId).SingleOrDefaultAsync(ct);
        // Inspect every member, not just a cached count or creation date. Late acceptance may create
        // a baseline today from old captures; that is legal, but self/future members are never legal.
        return await ForFixture(db.Set<LuminanceBaseline>().AsNoTracking(), fixtureId)
            .Where(b => b.PoleId == query.PoleId && b.Direction == query.Direction && b.DataSource == query.DataSource
                && db.Set<BaselineMember>().Count(m => m.BaselineId == b.BaselineId) == b.MemberCount
                && !db.Set<BaselineMember>().Where(m => m.BaselineId == b.BaselineId).Any(m =>
                    !db.Set<PoleObservation>().Any(o => o.ObservationId == m.ObservationId
                        && o.PoleId == query.PoleId && o.DataSource == query.DataSource && o.Pass.Direction == query.Direction
                        && o.ObservedAt < query.Before && o.Run.SweepId != query.SweepId
                        && db.Set<SurveySweep>().Any(s => s.SweepId == o.Run.SweepId
                            && s.Status == SweepStatus.Accepted && s.AcceptedRunId == o.RunId))))
            .OrderByDescending(b => b.Version).Select(b => new BaselineReference(b.BaselineId, b.Value)).FirstOrDefaultAsync(ct);
    }
    public static IQueryable<LuminanceBaseline> ForFixture(IQueryable<LuminanceBaseline> baselines, string? fixtureId)
        => baselines.Where(b => b.FixtureId == fixtureId);
}
