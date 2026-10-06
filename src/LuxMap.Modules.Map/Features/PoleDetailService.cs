using System.Net;
using LuxMap.Modules.Assets.Crud;
using LuxMap.Modules.Assets.Entities;
using LuxMap.Modules.Faults.Entities;
using LuxMap.Modules.Survey.Entities;
using LuxMap.Modules.Survey.Processing.Frames;
using LuxMap.Modules.Survey.Review;
using LuxMap.Persistence;
using LuxMap.Shared.Contracts.Enums;
using LuxMap.Shared.Contracts.Errors;
using LuxMap.Shared.Http;
using LuxMap.Shared.Serialization;
using Microsoft.EntityFrameworkCore;

namespace LuxMap.Modules.Map.Features;

/// <summary>
/// The pole screen behind <c>GET /map/poles/{pole_id}</c> (BE-20). One method, a handful of small
/// queries on indexed keys — no joins across modules in SQL, so each module's query filter applies.
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>Read the pole FIRST.</b> The BE-08 commune filter is in that query's <c>WHERE</c>, so a pole
/// outside the caller's scope is simply not found: 404, not 403 (Contract section 7). Everything after
/// it is keyed on that pole.
/// </para>
/// <para>
/// 🔴 <b>Every list is capped</b> and every ordering is explicit. History is the newest
/// <see cref="HistoryPoints"/> rows by <c>evaluated_at</c> and then flipped to oldest-first, which is the
/// order a chart wants; ties break on the prefixed-ID rule <c>length(id), id</c> — never the bare ID.
/// </para>
/// </remarks>
public sealed class PoleDetailService(
    LuxMapDbContext db, SurveyFrameOptions surveyOptions, SurveyMediaAccess mediaAccess)
{
    public const int HistoryPoints = 30;
    public const int RecentFrameCount = 10;
    public const int OpenFaultCap = 50;
    public const int FrameScanBatch = 30;
    public const int FrameScanLimit = 300;

    private static readonly FaultStatus[] OpenStatuses = [.. FaultStatusSets.Open];

    public async Task<PoleMapDetail> GetAsync(string poleId, CancellationToken ct)
    {
        var pole = await db.Set<Pole>().AsNoTracking()
            .Where(p => p.PoleId == poleId)
            .Select(p => new
            {
                p.PoleId, p.SegmentId, p.CommuneId, p.Geom,
                SegmentName = db.Set<RoadSegment>().Where(s => s.SegmentId == p.SegmentId)
                    .Select(s => s.SegmentName).FirstOrDefault(),
                p.Note,
            })
            .SingleOrDefaultAsync(ct)
            ?? throw new LuxMapException(ErrorCodes.PoleNotFound, HttpStatusCode.NotFound,
                "That pole does not exist, or it is outside your permitted commune scope.");

        var fixture = await db.Set<Fixture>().AsNoTracking()
            .Where(f => f.PoleId == poleId && f.RemovedDate == null)
            .Select(f => new { f.FixtureId, Detail = new PoleMapFixture
            {
                FixtureType = f.FixtureType, PowerSource = f.PowerSource, LampWatt = f.LampWatt,
                InstallDate = f.InstallDate, WarrantyExpiry = f.WarrantyExpiry,
            } })
            .SingleOrDefaultAsync(ct);

        var status = await db.Set<PoleCurrentStatus>().AsNoTracking()
            .Where(s => s.PoleId == poleId).SingleOrDefaultAsync(ct);

        var baselines = await CurrentBaselinesAsync(poleId, fixture?.FixtureId, ct);

        return new PoleMapDetail
        {
            PoleId = pole.PoleId,
            SegmentId = pole.SegmentId,
            SegmentName = pole.SegmentName ?? string.Empty,
            CommuneId = pole.CommuneId,
            Location = new PoleMapLocation(pole.Geom.Y, pole.Geom.X),
            Fixture = fixture?.Detail,
            CurrentStatus = new PoleMapStatus
            {
                // No row = no published sweep ever covered the pole = `unknown` (Contract 3.1).
                FixtureStatus = status?.FixtureStatus ?? FixtureStatus.Unknown,
                StatusConfidence = status?.StatusConfidence,
                DeterminedAt = status?.LastEvaluatedAt ?? status?.LastSeenAt,
                SourceChannel = status is null ? null : SourceChannel.Cv,
            },
            IotNode = null,
            LuminanceBaseline = baselines.OrderByDescending(b => b.ComputedAt).FirstOrDefault(),
            LuminanceBaselines = baselines,
            LuminanceHistory = await HistoryAsync(poleId, ct),
            RuntimeHistory = [],
            OpenFaults = await OpenFaultsAsync(poleId, ct),
            RecentFrames = await RecentFramesAsync(poleId, ct),
            Note = pole.Note,
        };
    }

    /// <summary>
    /// The newest baseline per travel direction for the lamp in use — the same "current lamp" rule the
    /// classifier applies (<see cref="SurveyBaselineLookup.ForFixture"/>): a baseline built on a lamp
    /// that has since been replaced is not shown as the pole's baseline.
    /// </summary>
    private async Task<IReadOnlyList<PoleMapBaseline>> CurrentBaselinesAsync(
        string poleId, string? fixtureId, CancellationToken ct)
    {
        var rows = await SurveyBaselineLookup.ForFixture(db.Set<LuminanceBaseline>().AsNoTracking(), fixtureId)
            .Where(b => b.PoleId == poleId)
            .OrderByDescending(b => b.Version)
            .ToListAsync(ct);

        return [.. rows
            .GroupBy(b => b.Direction)
            .Select(g => g.First())
            .OrderBy(b => b.Direction, StringComparer.Ordinal)
            .Select(b => new PoleMapBaseline
            {
                BaselineValue = b.Value,
                BaselineWindowNights = b.MemberCount,
                DimThresholdRatio = surveyOptions.DimThresholdRatio,
                OutThresholdRatio = null,
                ComputedAt = b.CreatedAt,
                Direction = b.Direction,
            })];
    }

    private async Task<IReadOnlyList<PoleMapHistoryPoint>> HistoryAsync(string poleId, CancellationToken ct)
    {
        var rows = await db.Set<LuminanceHistory>().AsNoTracking()
            .Where(h => h.PoleId == poleId)
            .OrderByDescending(h => h.EvaluatedAt).ThenByDescending(h => h.SweepId.Length)
            .ThenByDescending(h => h.SweepId)
            .Take(HistoryPoints)
            .ToListAsync(ct);

        rows.Reverse();
        return [.. rows.Select(h => new PoleMapHistoryPoint
        {
            ObservedAt = h.EvaluatedAt,
            SweepId = h.SweepId,
            NormalizedLuminance = h.BaselineRatio,
            BaselineRatio = h.BaselineRatio,
            ClassifiedAs = h.ClassifiedAs,
            PeakLux = h.PeakLux,
            ReasonCodes = SurveyPublicationRules.Flags(h.ReasonCodes),
        })];
    }

    private async Task<IReadOnlyList<PoleMapOpenFault>> OpenFaultsAsync(string poleId, CancellationToken ct)
    {
        // 🔴 Severity is stored as TEXT: ordering by the column is alphabetical (critical, high, low,
        // medium). Rank it with an explicit CASE, and push never-scored faults (NULL priority) last.
        var rows = await db.Set<Fault>().AsNoTracking()
            .Where(f => f.PoleId == poleId && OpenStatuses.Contains(f.FaultStatus))
            .OrderByDescending(f => f.Severity == Severity.Critical ? 3
                : f.Severity == Severity.High ? 2 : f.Severity == Severity.Medium ? 1 : 0)
            .ThenBy(f => f.PriorityScore == null)
            .ThenByDescending(f => f.PriorityScore)
            .ThenBy(f => f.CreatedAt).ThenBy(f => f.FaultId.Length).ThenBy(f => f.FaultId)
            .Take(OpenFaultCap)
            .ToListAsync(ct);

        return [.. rows.Select(f => new PoleMapOpenFault
        {
            FaultId = f.FaultId,
            FaultType = f.OverrideFaultType ?? f.FaultType,
            Severity = f.Severity,
            FaultStatus = f.FaultStatus,
            PriorityScore = f.PriorityScore,
        })];
    }

    /// <summary>
    /// Frames come only from PUBLISHED passes — the representative frame of each history row's
    /// observation — and only those the caller could open at <c>/frames/{id}/thumbnail</c>: a URL we
    /// list must not 404 when followed.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>Authorization filters BEFORE the cut to ten.</b> Truncating the newest candidates first would
    /// show nothing for a caller whose newest sweeps belong to someone else, even with older readable ones.
    /// Candidates are scanned newest-first in batches until <see cref="RecentFrameCount"/> readable frames
    /// are found, the candidates run out, or <see cref="FrameScanLimit"/> have been examined — a bound,
    /// because every unseen sweep costs two access queries.
    /// </remarks>
    private async Task<IReadOnlyList<PoleMapFrame>> RecentFramesAsync(string poleId, CancellationToken ct)
    {
        var candidates = (
            from h in db.Set<LuminanceHistory>().AsNoTracking()
            join o in db.Set<PoleObservation>().AsNoTracking() on h.ObservationId equals o.ObservationId
            where h.PoleId == poleId && o.RepresentativeFrameId != null
            orderby h.EvaluatedAt descending, h.SweepId.Length descending, h.SweepId descending
            select new { FrameId = o.RepresentativeFrameId!, h.SweepId });

        // Per sweep, decided once: visible (work order + assignee) AND every commune of its runs in scope.
        var readable = new Dictionary<string, SurveySweep?>();
        var result = new List<PoleMapFrame>();

        for (var skip = 0; skip < FrameScanLimit && result.Count < RecentFrameCount; skip += FrameScanBatch)
        {
            var batch = await candidates.Skip(skip).Take(FrameScanBatch).ToListAsync(ct);
            if (batch.Count == 0) break;

            var unseen = batch.Select(c => c.SweepId).Distinct().Where(id => !readable.ContainsKey(id)).ToArray();
            if (unseen.Length > 0)
            {
                var visible = await mediaAccess.VisibleSweeps().AsNoTracking()
                    .Where(sweep => unseen.Contains(sweep.SweepId)).ToDictionaryAsync(sweep => sweep.SweepId, ct);
                foreach (var id in unseen)
                    readable[id] = visible.TryGetValue(id, out var sweep) && await mediaAccess.CanReadMedia(id, ct) ? sweep : null;
            }

            var frameIds = batch.Where(c => readable[c.SweepId] is not null).Select(c => c.FrameId).ToArray();
            var frames = await db.Set<SurveyFrame>().AsNoTracking()
                .Where(f => frameIds.Contains(f.FrameId)).ToDictionaryAsync(f => f.FrameId, ct);

            foreach (var c in batch)
            {
                if (readable[c.SweepId] is not { } sweep || !frames.TryGetValue(c.FrameId, out var frame)) continue;
                if (sweep.AtElapsed(frame.PhoneElapsedNs) is not { } capturedAt) continue;
                result.Add(new PoleMapFrame
                {
                    FrameId = frame.FrameId,
                    SweepId = c.SweepId,
                    CapturedAt = capturedAt,
                    ThumbnailUrl = $"/api/v1/frames/{frame.FrameId}/thumbnail",
                });
                if (result.Count == RecentFrameCount) break;
            }
        }
        return result;
    }
}
