using LuxMap.Modules.Assets.Entities;
using LuxMap.Persistence;
using LuxMap.Shared.Contracts.Enums;
using Microsoft.EntityFrameworkCore;

namespace LuxMap.Modules.Survey.Review;

/// <summary>One current-status writer for accepted sweeps, confirmed operational photos and verified repairs.</summary>
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

    /// <summary>
    /// The first status of a pole the asset import is inserting (IMPORT-INITIAL-STATUS): no sweep and no run behind it, no AI
    /// score, evaluated and seen now. The pole has no id yet, so the row hangs on <see cref="Pole.CurrentStatus"/> and EF
    /// inserts it, keyed by the generated id, in the same save as the pole.
    /// </summary>
    public static void StageForNewPole(Pole pole, FixtureStatus status, DateTime now)
    {
        if (status == FixtureStatus.Unknown)
            throw new ArgumentOutOfRangeException(nameof(status), "Unknown is the absence of a status row.");
        pole.CurrentStatus = new PoleCurrentStatus
        {
            PoleId = pole.PoleId, CommuneId = pole.CommuneId, FixtureStatus = status, StatusConfidence = null,
            LastSeenAt = now, LastEvaluatedAt = now, LastSweepId = null, LastRunId = null, UpdatedAt = now,
        };
    }
}
