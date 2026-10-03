using System.Security.Cryptography;
using LuxMap.Modules.Survey.Processing.Frames;
using System.Text.Json;
using LuxMap.Modules.Identity.Entities;
using LuxMap.Modules.Survey.Entities;
using LuxMap.Modules.WorkOrders.Entities;
using LuxMap.Persistence;
using LuxMap.Persistence.Audit;
using LuxMap.Shared.Authorization;
using LuxMap.Shared.Contracts.Enums;
using LuxMap.Shared.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace LuxMap.Modules.Survey.Processing;

/// <summary>Creates an isolated context per job, with a finite scope and all write/audit guards active.</summary>
public sealed class SurveyProcessor(NpgsqlDataSource dataSource, ModuleAssemblyCatalog catalog,
    IOptions<SurveyProcessingOptions> options, ILogger<SurveyProcessor> logger, TimeProvider? timeProvider = null, SurveyFramePipeline? frames = null)
{
    private sealed class JobScope : ICommuneScopeAccessor
    {
        public CommuneScope Scope { get; set; } = CommuneScope.Empty;
    }
    private sealed record Claimed(string SweepId, string CommuneId, Guid Token, int Attempt, DateTime Expires, string[] Communes);
    public sealed class QueueRow
    {
        public required string SweepId { get; set; }
        public required string CommuneId { get; set; }
        public required string WorkOrderId { get; set; }
    }

    private LuxMapDbContext Context(JobScope scope)
    {
        var builder = new DbContextOptionsBuilder<LuxMapDbContext>();
        PersistenceServiceCollectionExtensions.Configure(builder, dataSource);
        return new(builder.Options, catalog, scope);
    }
    private DateTime Now() => UtcMicrosecondClock.UtcNow(timeProvider);
    private static string Json(object value) => JsonSerializer.Serialize(value, LuxMapJsonOptions.Default);

    public async Task<bool> ProcessOneAsync(CancellationToken ct = default, string? sweepId = null)
    {
        // Snapshot options once: reconfiguration cannot change thresholds halfway through a run.
        var settings = Json(options.Value);
        var o = JsonSerializer.Deserialize<SurveyProcessingOptions>(settings, LuxMapJsonOptions.Default)!;
        if (!o.IsValid()) throw new InvalidOperationException("Invalid SurveyProcessing configuration.");
        var scope = new JobScope();
        await using var db = Context(scope);
        var job = await Claim(db, scope, o, sweepId, ct);
        if (job is null) return false;
        var sweep = await db.Set<SurveySweep>().SingleAsync(x => x.SweepId == job.SweepId, ct);
        var run = new SurveyProcessingRun
        {
            SweepId = sweep.SweepId, CommuneId = sweep.CommuneId, Attempt = job.Attempt,
            LeaseOwner = job.Token, LeaseExpiresAt = job.Expires, StartedAt = sweep.UpdatedAt,
            InputHash = sweep.SubmissionRequestHash!, SettingsSnapshot = settings,
            ResultState = "succeeded", Stage = "complete"
        };
        FramePipelineResult? media = null;
        var results = new List<(string Segment, double Length, PassResult Pass)>();
        try
        {
            if (job.Attempt > o.MaxAttempts) throw new ProcessingFailure("RETRY_EXHAUSTED", "claim");
            await ValidateAssignee(db, sweep, job.Communes, ct);
            var segments = await db.Set<WorkOrderSegment>().Where(x => x.WorkOrderId == sweep.WorkOrderId)
                .OrderBy(x => x.Position).Select(x => x.SegmentId).ToArrayAsync(ct);
            await Heartbeat(db, sweep, job, o, ct);
            // One PostgreSQL snapshot includes the geometry used by both projections.
            var (gps, poles) = await SurveyChainageQuery.Snapshot(db, sweep.SweepId, segments, job.Communes,
                Json(sweep.DataSource).Trim('"'), o, ct);
            run.GisSnapshot = Json(new { segments, communes = job.Communes, poles, projected_gps = gps });
            await Heartbeat(db, sweep, job, o, ct);
            var raw = await db.Set<SurveyLuxSample>().Where(x => x.SweepId == sweep.SweepId).OrderBy(x => x.SampleNo)
                .Select(x => new ClockSample(x.ModuleEpoch, x.ModuleMs, x.PhoneElapsedNs, x.Seq, x.Lux)).ToArrayAsync(ct);
            await Heartbeat(db, sweep, job, o, ct);
            var fits = SurveyAlgorithms.FitClocks(raw, o);
            run.ClockFit = Json(new { epochs = fits });
            var intervals = SurveyAlgorithms.AlignLux(raw, fits, o);
            foreach (var route in gps.Where(x => x.SegmentId is not null && !x.RouteAmbiguous && x.RouteDistanceM <= o.RouteCorridorM).GroupBy(x => x.SegmentId!))
            {
                await Heartbeat(db, sweep, job, o, ct);
                var expected = poles.Where(x => x.SegmentId == route.Key).Select(x => new PolePosition(x.PoleId, x.CommuneId, x.ChainageM)).ToArray();
                var track = gps.Select(x => new TrackPoint(x.TimeNs, x.ChainageM, x.AccuracyM, x.SpeedMps,
                    x.RouteDistanceM is null || x.RouteDistanceM > o.RouteCorridorM, x.RouteAmbiguous,
                    x.SegmentId != route.Key && !x.RouteAmbiguous && x.RouteDistanceM <= o.RouteCorridorM)).ToArray();
                foreach (var pass in SurveyAlgorithms.ProcessRoute(track, expected, intervals, o))
                    results.Add((route.Key, route.First().LengthM, pass));
            }
            await Heartbeat(db, sweep, job, o, ct);
            if (gps.Length < 2) throw new ProcessingFailure("GPS_INSUFFICIENT", "chainage");
            run.CoveragePct = SurveyAlgorithms.Coverage(poles.Select(x => new PolePosition(x.PoleId, x.CommuneId, x.ChainageM)), results.Select(x => x.Pass));
            run.CoverageReason = poles.Length == 0 ? "no_expected_poles" : null;
            if (frames is null) throw new ProcessingFailure("DETECTOR_NOT_CONFIGURED", "detector");
            media = await frames.ProcessAsync(db, sweep, run, results, poles,
                token => Heartbeat(db, sweep, job, o, token), o.LeaseSeconds, ct);
        }
        catch (ProcessingFailure ex) when (ex.Code == "LEASE_LOST")
        {
            return true; // Heartbeat logged the lost fence. The new owner alone may persist results.
        }
        catch (ProcessingFailure ex)
        {
            run.ResultState = "failed"; run.Stage = ex.Stage; run.ErrorCode = ex.Code;
            results.Clear(); run.CoveragePct = null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Survey {SweepId} attempt {Attempt} failed unexpectedly.", sweep.SweepId, job.Attempt);
            run.ResultState = "failed"; run.Stage = "processing"; run.ErrorCode = "PROCESSING_ERROR";
            results.Clear(); run.CoveragePct = null;
        }
        await Complete(db, scope, sweep, job, run, results, media, o, ct);
        return true;
    }

    private async Task<Claimed?> Claim(LuxMapDbContext db, JobScope scope, SurveyProcessingOptions o, string? sweepId, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        // The only global queue read returns identifiers, never raw survey payload. Lock survives to commit.
        var candidates = await db.Database.SqlQuery<QueueRow>($"""
            SELECT sweep_id AS sweep_id, commune_id AS commune_id, work_order_id AS work_order_id
            FROM survey_sweep
            WHERE ({sweepId}::text IS NULL OR sweep_id = {sweepId})
              AND (processing_status = 'queued'
               OR (processing_status = 'processing' AND processing_lease_expires_at <= now()))
            ORDER BY created_at, length(sweep_id), sweep_id
            LIMIT 1 FOR UPDATE SKIP LOCKED
            """).ToArrayAsync(ct);
        if (candidates.Length == 0) return null;
        var selected = candidates[0];
        // Scope discovery is confined to this work order and its source-compatible target poles.
        var communes = await db.Database.SqlQuery<string>($"""
            SELECT {selected.CommuneId} AS "Value"
            UNION SELECT r.commune_id FROM work_order_segment w JOIN road_segment r USING (segment_id)
              WHERE w.work_order_id = {selected.WorkOrderId}
            UNION SELECT p.commune_id FROM work_order_segment w JOIN pole p USING (segment_id)
              JOIN survey_sweep s ON s.sweep_id = {selected.SweepId} AND p.data_source = s.data_source
              WHERE w.work_order_id = {selected.WorkOrderId}
            """).ToArrayAsync(ct);
        scope.Scope = CommuneScope.ForCommunes(communes);
        var sweep = await db.Set<SurveySweep>().SingleAsync(x => x.SweepId == selected.SweepId, ct);
        var now = Now();
        sweep.ProcessingAttempt++;
        sweep.ProcessingLeaseOwner = Guid.NewGuid(); sweep.ProcessingLeaseExpiresAt = now.AddSeconds(o.LeaseSeconds);
        sweep.Status = SweepStatus.Processing; sweep.ProcessingStatus = SweepProcessingStatus.Processing; sweep.UpdatedAt = now;
        Audit(db, sweep, sweep.CommuneId, AuditAction.Started, new { sweep.ProcessingAttempt, sweep.ProcessingStatus });
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        return new(sweep.SweepId, sweep.CommuneId, sweep.ProcessingLeaseOwner.Value, sweep.ProcessingAttempt, sweep.ProcessingLeaseExpiresAt.Value, communes);
    }

    private async Task Heartbeat(LuxMapDbContext db, SurveySweep sweep, Claimed job, SurveyProcessingOptions o, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM survey_sweep WHERE sweep_id = {job.SweepId} FOR UPDATE", ct);
        await db.Entry(sweep).ReloadAsync(ct);
        var now = Now();
        if (sweep.ProcessingLeaseOwner != job.Token || sweep.ProcessingLeaseExpiresAt <= now)
        {
            logger.LogWarning("Survey {SweepId} attempt {Attempt} lost its lease at a processing checkpoint.", job.SweepId, job.Attempt);
            throw new ProcessingFailure("LEASE_LOST", "lease");
        }
        if (sweep.ProcessingLeaseExpiresAt <= now.AddSeconds(o.LeaseSeconds / 2))
        {
            sweep.ProcessingLeaseExpiresAt = now.AddSeconds(o.LeaseSeconds);
            sweep.UpdatedAt = now;
            Audit(db, sweep, sweep.CommuneId, AuditAction.DetailsChanged,
                new { job.Attempt, lease_expires_at = sweep.ProcessingLeaseExpiresAt, stage = "heartbeat" });
            await db.SaveChangesAsync(ct);
        }
        await tx.CommitAsync(ct);
    }

    private static async Task ValidateAssignee(LuxMapDbContext db, SurveySweep sweep, string[] communes, CancellationToken ct)
    {
        var wo = await db.Set<WorkOrder>().AsNoTracking().SingleAsync(x => x.WorkOrderId == sweep.WorkOrderId, ct);
        var user = await db.Set<AppUser>().AsNoTracking().SingleOrDefaultAsync(x => x.UserId == wo.AssignedTo, ct);
        if (user is null || user.IsLocked || user.Role != UserRole.FieldEngineer || wo.WoStatus == WorkOrderStatus.Cancelled)
            throw new ProcessingFailure("JOB_SCOPE_INVALID", "scope");
        var assigned = await db.Set<AppUserCommune>().Where(x => x.UserId == user.UserId).Select(x => x.CommuneId).ToArrayAsync(ct);
        if (!user.HasSystemWideScope && communes.Except(assigned).Any()) throw new ProcessingFailure("JOB_SCOPE_INVALID", "scope");
    }

    private static async Task<long> Artifact(LuxMapDbContext db, string component, CancellationToken ct)
    {
        // Hash the actual assembly bytes, not a mutable model path or a placeholder label.
        var hash = Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(typeof(SurveyAlgorithms).Assembly.Location, ct)));
        // Idempotent registry bootstrap, no global administration API until BE-33/34.
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO artifact_version (component, version, artifact_hash, metadata, created_at)
            VALUES ({component}, {hash}, {hash}, jsonb_build_object(), now())
            ON CONFLICT (component, version) DO NOTHING
            """, ct);
        return await db.Set<ArtifactVersion>().Where(x => x.Component == component && x.Version == hash).Select(x => x.VersionId).SingleAsync(ct);
    }

    private async Task Complete(LuxMapDbContext db, JobScope scope, SurveySweep sweep, Claimed job,
        SurveyProcessingRun run, List<(string Segment, double Length, PassResult Pass)> results,
        FramePipelineResult? media, SurveyProcessingOptions o, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM work_order WHERE work_order_id = {sweep.WorkOrderId} FOR UPDATE", ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM survey_sweep WHERE sweep_id = {job.SweepId} FOR UPDATE", ct);
        await db.Entry(sweep).ReloadAsync(ct);
        if (sweep.ProcessingLeaseOwner != job.Token || sweep.ProcessingLeaseExpiresAt <= Now())
        {
            logger.LogWarning("Discarding survey {SweepId} attempt {Attempt}: lease expired or changed before commit.", job.SweepId, job.Attempt);
            return;
        }
        run.LeaseExpiresAt = sweep.ProcessingLeaseExpiresAt!.Value;
        if (run.ResultState == "succeeded")
        {
            try { await ValidateAssignee(db, sweep, job.Communes, ct); }
            catch (ProcessingFailure ex)
            {
                run.ResultState = "failed"; run.Stage = ex.Stage; run.ErrorCode = ex.Code;
                run.CoveragePct = null; results.Clear();
            }
        }
        run.AlgorithmVersionId = await Artifact(db, "association_algorithm", ct);
        run.ClockVersionId = await Artifact(db, "clock_algorithm", ct);
        run.ClassificationVersionId = await Artifact(db, "classification_algorithm", ct);
        run.FinishedAt = Now();
        sweep.UpdatedAt = run.FinishedAt;
        bool retry = run.ErrorCode == "PROCESSING_ERROR" && job.Attempt < o.MaxAttempts;
        sweep.Status = run.ResultState == "succeeded" ? SweepStatus.AwaitingReview : retry ? SweepStatus.Queued : SweepStatus.Failed;
        sweep.ProcessingStatus = run.ResultState == "succeeded" ? SweepProcessingStatus.Succeeded : retry ? SweepProcessingStatus.Queued : SweepProcessingStatus.Failed;
        sweep.CoveragePct = run.CoveragePct;
        sweep.ProcessingLeaseOwner = null; sweep.ProcessingLeaseExpiresAt = null;
        if (run.ResultState != "succeeded")
        { media = null; run.DetectionCoveragePct = null; run.DimCoveragePct = null; }
        if (media is not null)
        {
            db.AddRange(media.NewFrames); db.AddRange(media.Detections);
            sweep.FrameCount = media.FrameCount;
        }
        db.Add(run);
        var observations = new List<PoleObservation>();
        int number = 0;
        foreach (var result in results)
        {
            var pass = new SurveyPass
            {
                Run = run, SegmentId = result.Segment, PassNo = number++,
                Direction = result.Pass.Track[^1].ChainageM > result.Pass.Track[0].ChainageM ? "forward" : "reverse",
                FromFraction = result.Pass.Track[0].ChainageM / result.Length, ToFraction = result.Pass.Track[^1].ChainageM / result.Length,
                StartElapsedNs = result.Pass.Track[0].TimeNs, EndElapsedNs = result.Pass.Track[^1].TimeNs,
                QualityFlags = Json(new { gps_offset_seconds = result.Pass.GpsOffsetSeconds, offset_reliable = result.Pass.OffsetReliable, excess_time_ratio = result.Pass.ExcessTimeRatio })
            };
            db.Add(pass);
            observations.AddRange(result.Pass.Observations.Select(p => CreateObservation(pass, run, sweep, p, media)));
        }
        foreach (var commune in new[] { sweep.CommuneId }.Concat(observations.Select(x => x.CommuneId)).Distinct())
        {
            scope.Scope = CommuneScope.ForCommunes([commune]);
            db.AddRange(observations.Where(x => x.CommuneId == commune));
            Audit(db, sweep, commune, AuditAction.Completed, new { attempt = job.Attempt, run.ResultState, run.Stage, run.ErrorCode,
                observation_count = observations.Count(x => x.CommuneId == commune) });
            await db.SaveChangesAsync(ct);
        }
        await tx.CommitAsync(ct);
    }

    public static PoleObservation CreateObservation(SurveyPass pass, SurveyProcessingRun run, SurveySweep sweep,
        Passage p, FramePipelineResult? media)
    {
        var cv = media?.Observations[(pass.PassNo, p.PoleId, p.TimeNs)];
        return new PoleObservation
        {
            Pass = pass, Run = run, PoleId = p.PoleId, CommuneId = p.CommuneId, DataSource = sweep.DataSource,
            ObservedElapsedNs = p.TimeNs,
            ObservedAt = new DateTime((sweep.UtcAnchor.Ticks + (p.TimeNs - sweep.ElapsedAnchorNs) / 100) / 10 * 10, DateTimeKind.Utc),
            CvState = cv?.Association.State,
            CvConfidence = cv?.Association.Confidence,
            RepresentativeFrameId = cv?.Association.RepresentativeFrameId,
            ClassifiedAs = cv?.Classification.Status ?? FixtureStatus.Unknown,
            BaselineRatio = cv?.Classification.BaselineRatio,
            DimEvaluationEligible = cv?.Classification.DimEvaluationEligible ?? false,
            ReasonCodes = Json(cv?.Classification.Reasons ?? []),
            ChainageM = p.ChainageM, PeakAtElapsedNs = p.Peak?.TimeNs, PeakLux = p.Peak?.Lux,
            SpeedMps = p.SpeedMps, AssociationConfidence = p.Confidence, QualityFlags = Json(p.Flags)
        };
    }

    private static void Audit(LuxMapDbContext db, SurveySweep sweep, string commune, AuditAction action, object state)
        => db.Add(new AuditEvent
        {
            OccurredAt = sweep.UpdatedAt, ActorKind = AuditActorKind.Cv, CommuneId = commune,
            EntityType = AuditEntityType.SurveySweep, EntityId = sweep.SweepId, Action = action,
            AfterState = Json(state), CorrelationId = $"survey:{sweep.SweepId}:{sweep.ProcessingAttempt}"
        });
}
