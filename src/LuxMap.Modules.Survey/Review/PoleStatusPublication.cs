using LuxMap.Modules.Assets.Entities;
using LuxMap.Persistence;
using LuxMap.Shared.Contracts.Enums;
using Microsoft.EntityFrameworkCore;

namespace LuxMap.Modules.Survey.Review;

/// <summary>One current-status writer for accepted sweeps and confirmed operational photos.</summary>
internal static class PoleStatusPublication
{
    public static async Task<bool> StageAsync(LuxMapDbContext db, string poleId, string communeId,
        FixtureStatus status, double? confidence, DateTime evaluatedAt, DateTime now,
        string? sweepId, long? runId, CancellationToken ct)
    {
        if (status == FixtureStatus.Unknown && confidence is not null
            || confidence is { } c && (!double.IsFinite(c) || c is < 0 or > 1))
            throw new InvalidOperationException("Invalid published confidence.");
        var current = await db.Set<PoleCurrentStatus>().SingleOrDefaultAsync(p => p.PoleId == poleId, ct);
        if (!SurveyPublicationRules.IsNewer(evaluatedAt, current?.LastEvaluatedAt ?? current?.LastSeenAt)) return false;
        if (current is null) { current = new() { PoleId = poleId, CommuneId = communeId }; db.Add(current); }
        current.FixtureStatus = status; current.StatusConfidence = confidence;
        current.LastSeenAt = status == FixtureStatus.Unknown ? null : evaluatedAt;
        current.LastEvaluatedAt = evaluatedAt; current.LastRunId = runId;
        current.LastSweepId = sweepId; current.UpdatedAt = now;
        return true;
    }
}
