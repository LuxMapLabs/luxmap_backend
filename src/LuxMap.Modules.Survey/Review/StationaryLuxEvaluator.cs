using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LuxMap.Modules.Assets.Entities;
using LuxMap.Modules.Survey.Entities;
using LuxMap.Modules.WorkOrders;
using LuxMap.Modules.WorkOrders.Entities;
using LuxMap.Persistence;
using LuxMap.Persistence.Settings;
using LuxMap.Persistence.Versions;
using LuxMap.Shared.Contracts.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LuxMap.Modules.Survey.Review;

/// <summary>
/// LUX-BASELINE D-8 / D-9: judges the protocol lux reading of a photo the Manager is confirming against the pole's own stationary
/// baseline, and folds an eligible reading into the next baseline. Stages rows only; the caller holds the work order → pole →
/// photo locks and saves with one audit.
/// </summary>
public sealed class StationaryLuxEvaluator(LuxMapDbContext db, IOptions<LuxProtocolOptions> protocol, IOptions<SurveyReviewOptions> review)
{
    public const string Evaluated = "brightness_evaluated";
    public const string BaselineMissing = "baseline_missing";
    public const string BaselineUnstable = "baseline_unstable";
    public const string CvOff = "cv_off";
    public const string OpticalFilter = "optical_filter";

    private static readonly Lazy<byte[]> AssemblyBytes = new(() => File.ReadAllBytes(typeof(StationaryLuxEvaluator).Assembly.Location));

    /// <returns>The staged verdict, or null when there is nothing to judge: not a survey order (D-2, D-16), no linked reading, or a
    /// reference reading without a protocol (D-3).</returns>
    public async Task<LuxEvaluation?> EvaluateAsync(PolePhotoPublication input, Pole pole, string actorId, CancellationToken ct)
    {
        var kind = await db.Set<WorkOrder>().Where(w => w.WorkOrderId == input.WorkOrderId)
            .Select(w => (TaskKind?)w.TaskKind).SingleOrDefaultAsync(ct);
        if (kind != TaskKind.Survey) return null;
        var reading = await db.Set<LuxReading>().AsNoTracking().SingleOrDefaultAsync(r => r.PhotoId == input.PhotoId, ct);
        if (reading is null || !reading.HasProtocol) return null;
        if (await db.Set<LuxEvaluation>().AnyAsync(e => e.LuxId == reading.LuxId, ct)) return null;

        var o = protocol.Value;
        var threshold = (await SystemSettingValues.ReadAsync(db, ct)).DimThresholdRatio;
        // The same anchor the pole detail and the sweep baseline use: the newest lamp in service, so replacing it restarts (D-9).
        var fixture = await db.Set<Fixture>().AsNoTracking()
            .Where(f => f.PoleId == pole.PoleId && f.RemovedDate == null).NewestFirst().FirstOrDefaultAsync(ct);
        var fixtureId = fixture?.FixtureId;
        var fingerprint = StationaryLuxRules.Fingerprint(reading.ModuleId!, o.SensorHeightM);
        var night = StationaryLuxRules.Night(reading.MeasuredAt);
        var algorithm = await AlgorithmAsync(o, ct);

        // The newest baseline of this lamp and protocol whose every member was measured on an EARLIER night: a reading is never
        // judged against a median that contains itself or its own night.
        var baselines = await db.Set<StationaryBaseline>().AsNoTracking()
            .Where(b => b.PoleId == pole.PoleId && b.FixtureId == fixtureId && b.ProtocolFingerprint == fingerprint
                && b.DataSource == reading.DataSource)
            .OrderByDescending(b => b.Version).ToListAsync(ct);
        var baselineIds = baselines.Select(b => b.BaselineId).ToArray();
        var memberTimes = (await (from m in db.Set<StationaryBaselineMember>()
                                  join r in db.Set<LuxReading>() on m.LuxId equals r.LuxId
                                  where baselineIds.Contains(m.BaselineId)
                                  select new { m.BaselineId, r.MeasuredAt }).ToListAsync(ct))
            .ToLookup(x => x.BaselineId, x => x.MeasuredAt);
        var usable = baselines.FirstOrDefault(b => memberTimes[b.BaselineId].All(t => StationaryLuxRules.Night(t) < night));

        var filter = reading.OpticalFilterTransmittance;
        FixtureStatus status;
        double? ratio = null;
        var dimEligible = false;
        var reasons = new List<string>();
        if (input.EffectiveCvState == "off")
        {
            status = FixtureStatus.Out;
            reasons.Add(CvOff);
            usable = null;
        }
        else if (usable is null)
        {
            status = FixtureStatus.Normal;
            reasons.Add(BaselineMissing);
        }
        else
        {
            ratio = reading.LuxValue / usable.Value;
            if (usable.MemberCv > o.MaxBaselineCv)
            {
                status = FixtureStatus.Normal;
                reasons.Add(BaselineUnstable);
            }
            else
            {
                dimEligible = true;
                status = ratio < threshold ? FixtureStatus.Dim : FixtureStatus.Normal;
                reasons.Add(Evaluated);
            }
        }
        if (filter is not null) reasons.Add(OpticalFilter);

        DateTime? installed = fixture is null ? null : fixture.InstallDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var evaluation = new LuxEvaluation
        {
            LuxId = reading.LuxId, PoleId = pole.PoleId, CommuneId = pole.CommuneId, PhotoId = input.PhotoId, WorkOrderId = input.WorkOrderId,
            DataSource = reading.DataSource, FixtureId = fixtureId, ProtocolFingerprint = fingerprint, MeasuredAt = reading.MeasuredAt,
            EvaluatedAt = input.Now, EvaluatedBy = actorId, CvState = input.EffectiveCvState, ClassifiedAs = status, LuxValue = reading.LuxValue,
            BaselineId = usable?.BaselineId, BaselineValue = usable?.Value, BaselineRatio = ratio, DimThresholdRatio = threshold,
            DimEvaluationEligible = dimEligible, ExpectedRatio = filter,
            // Sources stay apart because every baseline is keyed by the READING's data_source, not because the reading must match
            // the pole's: a pole inventoried from public imagery is still measured in the field (E2E 26/11).
            BaselineEligible = status == FixtureStatus.Normal && filter is null && (installed is null || reading.MeasuredAt >= installed),
            ReasonCodes = JsonSerializer.Serialize(reasons), AlgorithmVersionId = algorithm,
        };
        db.Add(evaluation);
        if (evaluation.BaselineEligible) await RebuildAsync(evaluation, baselines, algorithm, actorId, ct);
        return evaluation;
    }

    /// <summary>D-9 / D-6: one member per survey night (the first decided), median once there are enough nights.</summary>
    private async Task RebuildAsync(LuxEvaluation current, List<StationaryBaseline> baselines, long algorithm, string actorId,
        CancellationToken ct)
    {
        var earlier = await db.Set<LuxEvaluation>().AsNoTracking()
            .Where(e => e.PoleId == current.PoleId && e.BaselineEligible && e.ProtocolFingerprint == current.ProtocolFingerprint
                && e.FixtureId == current.FixtureId && e.DataSource == current.DataSource)
            .ToListAsync(ct);
        var members = earlier.Append(current)
            .GroupBy(e => StationaryLuxRules.Night(e.MeasuredAt))
            .Select(night => night.OrderBy(e => e.EvaluatedAt).ThenBy(e => e.LuxId.Length).ThenBy(e => e.LuxId, StringComparer.Ordinal).First())
            .ToList();
        if (members.Count < review.Value.BaselineMinimumMembers) return;

        var latest = baselines.FirstOrDefault();
        if (latest is not null)
        {
            var previous = await db.Set<StationaryBaselineMember>().Where(m => m.BaselineId == latest.BaselineId)
                .Select(m => m.LuxId).ToListAsync(ct);
            if (previous.ToHashSet().SetEquals(members.Select(m => m.LuxId))) return;
        }

        var values = members.Select(m => m.LuxValue).ToArray();
        var median = StationaryLuxRules.Median(values);
        if (median <= 0) return;
        var version = (await db.Set<StationaryBaseline>().Where(b => b.PoleId == current.PoleId)
            .Select(b => (int?)b.Version).MaxAsync(ct) ?? 0) + 1;
        var baseline = new StationaryBaseline
        {
            PoleId = current.PoleId, CommuneId = current.CommuneId, FixtureId = current.FixtureId, DataSource = current.DataSource,
            ProtocolFingerprint = current.ProtocolFingerprint, Version = version, Value = median, MemberCount = members.Count,
            MemberCv = StationaryLuxRules.Cv(values), AlgorithmVersionId = algorithm, CreatedAt = current.EvaluatedAt, CreatedBy = actorId,
        };
        db.Add(baseline);
        db.AddRange(members.Select(m => new StationaryBaselineMember { Baseline = baseline, LuxId = m.LuxId }));
    }

    /// <summary>BE-34: the classifier's version hashes the code AND its thresholds, so a changed threshold is a new version.</summary>
    private async Task<long> AlgorithmAsync(LuxProtocolOptions o, CancellationToken ct)
    {
        var rules = Encoding.UTF8.GetBytes(o.Describe());
        var hash = Convert.ToHexStringLower(SHA256.HashData([.. AssemblyBytes.Value, .. rules]));
        var metadata = JsonSerializer.Serialize(new { kind = "stationary_lux", rules = o.Describe() });
        return (await ArtifactRegistry.EnsureAsync(db, ArtifactComponents.ClassificationAlgorithm, hash, hash, metadata, ct)).VersionId;
    }
}
