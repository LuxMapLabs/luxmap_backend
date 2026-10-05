using System.Data.Common;
using LuxMap.Modules.Assets.Entities;
using LuxMap.Modules.Identity.Entities;
using LuxMap.Modules.Notifications.Entities;
using LuxMap.Modules.Survey.Entities;
using LuxMap.Modules.Survey.Processing;
using LuxMap.Modules.WorkOrders.Entities;
using LuxMap.Persistence;
using LuxMap.Persistence.Audit;
using LuxMap.Shared.Authorization;
using LuxMap.Shared.Contracts.Enums;
using LuxMap.Shared.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NetTopologySuite.Geometries;
using Npgsql;

namespace LuxMap.Api.Tests;

/// <summary>Requires the migrated test database. Author compiles only; Claude runs this suite.
/// Own data is removed in a transaction using the audit teardown convention.</summary>
[Collection(nameof(AssetDatabaseCollection))]
public sealed class SurveyProcessingTests(AssetImportFixture factory) : IAsyncLifetime
{
    private readonly SurveyFrameFixture media = new();
    private NpgsqlDataSource source = null!;
    private ModuleAssemblyCatalog catalog = null!;
    private string a = null!, b = null!, road = null!, sweep = null!;
    private string[] poles = [];
    private string? parallelRoad;
    private string userId = null!, orderId = null!;
    /// <summary>BE-27: a manager holding both communes of the job, and one holding only the anchor.</summary>
    private string bothManager = null!, anchorManager = null!;
    private sealed class Scope(string[] communes) : ICommuneScopeAccessor
    {
        public CommuneScope ScopeValue { get; } = CommuneScope.ForCommunes(communes);
        CommuneScope ICommuneScopeAccessor.Scope => ScopeValue;
    }
    private LuxMapDbContext Db(string[]? communes = null, IInterceptor? interceptor = null)
    {
        var options = new DbContextOptionsBuilder<LuxMapDbContext>();
        PersistenceServiceCollectionExtensions.Configure(options, source);
        if (interceptor is not null) options.AddInterceptors(interceptor);
        return new(options.Options, catalog, new Scope(communes ?? [a, b]));
    }
    private SurveyProcessor Processor() => new(source, catalog, Options.Create(new SurveyProcessingOptions()), NullLogger<SurveyProcessor>.Instance, frames: media.Pipeline);

    public async Task InitializeAsync()
    {
        catalog = factory.Services.GetRequiredService<ModuleAssemblyCatalog>();
        source = factory.Services.GetRequiredService<NpgsqlDataSource>();
        await using var db = Db();
        using var seed = db.EnterUnscopedSystemWriteBackdoor();
        var communes = new[] { new AdministrativeUnit { Name = "A" }, new AdministrativeUnit { Name = "B" } };
        db.AddRange(communes); await db.SaveChangesAsync(); a = communes[0].CommuneId; b = communes[1].CommuneId;
        var user = new AppUser { Username = "processing" + Guid.NewGuid().ToString("N"), Email = Guid.NewGuid() + "@example.invalid", FullName = "Test engineer",
            PasswordHash = "unused", PasswordAlgorithm = "pbkdf2-aspnetcore-v3", PasswordSetAt = DateTime.UtcNow, Role = UserRole.FieldEngineer };
        db.Add(user); await db.SaveChangesAsync(); userId = user.UserId;
        db.AddRange(new AppUserCommune { UserId = user.UserId, CommuneId = a }, new AppUserCommune { UserId = user.UserId, CommuneId = b });
        var managers = new[] { "both", "anchor" }.Select(name => new AppUser { Username = "processing" + Guid.NewGuid().ToString("N"),
            Email = Guid.NewGuid() + "@example.invalid", FullName = name, PasswordHash = "unused", PasswordAlgorithm = "pbkdf2-aspnetcore-v3",
            PasswordSetAt = DateTime.UtcNow, Role = UserRole.Manager }).ToArray();
        db.AddRange(managers); await db.SaveChangesAsync(); bothManager = managers[0].UserId; anchorManager = managers[1].UserId;
        db.AddRange(new AppUserCommune { UserId = bothManager, CommuneId = a }, new AppUserCommune { UserId = bothManager, CommuneId = b },
            new AppUserCommune { UserId = anchorManager, CommuneId = a });
        var route = new RoadSegment { CommuneId = a, SegmentName = "Curve", RoadClass = RoadClass.InterVillage,
            DataSource = DataSource.Simulated, LengthM = 999, Geom = new LineString([new(108,16), new(108.0005,16.00002), new(108.001,16)]) { SRID = 4326 } };
        db.Add(route); await db.SaveChangesAsync(); road = route.SegmentId;
        var assets = new[] { new Pole { CommuneId = a, SegmentId = road, DataSource = DataSource.Simulated, Geom = new Point(108.00025,16.00004) { SRID = 4326 } },
            new Pole { CommuneId = b, SegmentId = road, DataSource = DataSource.Simulated, Geom = new Point(108.00075,15.99998) { SRID = 4326 } } };
        db.AddRange(assets); await db.SaveChangesAsync(); poles = assets.Select(x => x.PoleId).ToArray();
        var now = DateTime.UtcNow;
        var order = new WorkOrder { CommuneId = a, CreatedBy = user.UserId, AssignedTo = user.UserId, AssignedAt = now,
            StartedAt = now, TaskKind = TaskKind.Survey, WoStatus = WorkOrderStatus.InProgress, Title = "Survey" };
        db.Add(order); await db.SaveChangesAsync(); orderId = order.WorkOrderId;
        db.Add(new WorkOrderSegment { WorkOrderId = order.WorkOrderId, CommuneId = a, SegmentId = road });
        var item = new SurveySweep { WorkOrderId = order.WorkOrderId, CommuneId = a, CapturedBy = user.UserId,
            ClientOpId = Guid.NewGuid(), BootSessionId = Guid.NewGuid(), CreateRequestHash = new('a',64), SubmissionRequestHash = new('b',64),
            UtcAnchor = now, DataSource = DataSource.Simulated, Status = SweepStatus.Queued, ProcessingStatus = SweepProcessingStatus.Queued,
            EndedElapsedNs = 20_000_000_000L };
        db.Add(item); await db.SaveChangesAsync(); sweep = item.SweepId;
        for (int i = 0; i <= 20; i++) db.Add(new SurveyGpsSample { SweepId = sweep, SampleNo = i, PhoneElapsedNs = i * 1_000_000_000L,
            Geom = new Point(108 + .001 * i / 20, 16 + .00002 * (1 - Math.Abs(i - 10) / 10d)) { SRID = 4326 }, AccuracyM = 2, SpeedMps = 5.4, Provider = "gps" });
        for (int i = 0; i <= 160; i++) db.Add(new SurveyLuxSample { SweepId = sweep, SampleNo = i, Seq = i,
            ModuleMs = i * 125, PhoneElapsedNs = i * 125_000_000L, Lux = 2 + 80 * Math.Exp(-Math.Pow((i / 8d - 5) / .3,2) / 2) });
        await media.Initialize(db, sweep);
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await using var db = Db();
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlRawAsync("SET LOCAL luxmap.audit_purge = 'on'");
        var versions = await db.Set<SurveyProcessingRun>().Where(x => x.SweepId == sweep)
            .Select(x => new { x.AlgorithmVersionId, x.ClockVersionId, x.ClassificationVersionId, x.ModelVersionId, x.ExtractorVersionId }).ToArrayAsync();
        var ids = versions.SelectMany(x => new[] { x.AlgorithmVersionId, x.ClockVersionId, x.ClassificationVersionId ?? 0, x.ModelVersionId ?? 0, x.ExtractorVersionId ?? 0 }).Distinct().ToArray();
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM audit_event WHERE commune_id = {a} OR commune_id = {b}");
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM pole_observation WHERE run_id IN (SELECT run_id FROM survey_processing_run WHERE sweep_id = {sweep})");
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM detection WHERE sweep_id = {sweep}");
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM survey_frame WHERE sweep_id = {sweep}");
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM survey_pass WHERE run_id IN (SELECT run_id FROM survey_processing_run WHERE sweep_id = {sweep})");
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM survey_processing_run WHERE sweep_id = {sweep}");
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM artifact_version WHERE version_id = ANY({ids}) AND NOT EXISTS (SELECT 1 FROM survey_processing_run WHERE algorithm_version_id = version_id OR clock_version_id = version_id OR classification_version_id = version_id OR model_version_id = version_id OR extractor_version_id = version_id)");
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM survey_gps_sample WHERE sweep_id = {sweep}");
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM survey_lux_sample WHERE sweep_id = {sweep}");
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM survey_raw_file WHERE sweep_id = {sweep}");
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM survey_video_clip WHERE sweep_id = {sweep}");
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM survey_sweep WHERE sweep_id = {sweep}");
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM work_order_segment WHERE work_order_id = {orderId}");
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM work_order WHERE work_order_id = {orderId}");
        if (parallelRoad is not null)
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM pole WHERE segment_id = {parallelRoad}");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM road_segment WHERE segment_id = {parallelRoad}");
        }
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM pole WHERE segment_id = {road}");
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM road_segment WHERE segment_id = {road}");
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM notification WHERE commune_id = {a} OR commune_id = {b}");
        var accounts = new[] { userId, bothManager, anchorManager };
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM app_user_commune WHERE user_id = ANY({accounts})");
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM app_user WHERE user_id = ANY({accounts})");
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM administrative_unit WHERE commune_id = {a} OR commune_id = {b}");
        await tx.CommitAsync();
        media.Dispose();
    }

    [Fact]
    public async Task Frames_detections_classification_and_three_coverages_are_persisted_before_review()
    {
        Assert.True(await Processor().ProcessOneAsync(sweepId: sweep));
        await using var db = Db();
        var run = await db.Set<SurveyProcessingRun>().SingleAsync(x => x.SweepId == sweep);
        Assert.Equal("succeeded", run.ResultState);
        Assert.Equal(100, run.CoveragePct); Assert.Equal(100, run.DetectionCoveragePct); Assert.Equal(0, run.DimCoveragePct);
        var item = await db.Set<SurveySweep>().SingleAsync(x => x.SweepId == sweep);
        var frames = await db.Set<SurveyFrame>().Where(x => x.SweepId == sweep).ToArrayAsync();
        Assert.NotEmpty(frames); Assert.Equal(frames.Length, item.FrameCount); Assert.Equal(SweepStatus.AwaitingReview, item.Status);
        Assert.Equal(frames.Length * 2, await db.Set<Detection>().CountAsync(x => x.SweepId == sweep));
        foreach (var frame in frames)
        {
            Assert.True(await media.ExistsAsync(LuxMap.Shared.Storage.StorageBucket.Survey, frame.ObjectKey));
            Assert.True(await media.ExistsAsync(LuxMap.Shared.Storage.StorageBucket.Survey, frame.ThumbnailKey));
        }
        var observations = await db.Set<PoleObservation>().Where(x => x.RunId == run.RunId).ToArrayAsync();
        Assert.Contains(observations, x => x.ClassifiedAs == FixtureStatus.Normal);
        Assert.Contains(observations, x => x.ClassifiedAs == FixtureStatus.Out);
        Assert.All(observations, x => { Assert.Null(x.BaselineRatio); Assert.False(x.DimEvaluationEligible); });
        Assert.False(await db.Set<PoleCurrentStatus>().AnyAsync(x => poles.Contains(x.PoleId)));
        var error = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE survey_frame SET width = width + 1 WHERE sweep_id = {sweep}"));
        Assert.Equal("55000", error.SqlState);
        error = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM detection WHERE sweep_id = {sweep}"));
        Assert.Equal("55000", error.SqlState);

        // BE-27: only the manager who can review the WHOLE run (both communes) is told; the engineer is not.
        var notice = Assert.Single(await Notices(db));
        Assert.Equal((bothManager, NotificationType.SurveyReadyForReview, a, $"Khảo sát {sweep} chờ duyệt"),
            (notice.RecipientUserId, notice.Type, notice.CommuneId, notice.Title));
    }

    private Task<List<Notification>> Notices(LuxMapDbContext db)
        => db.Set<Notification>().IgnoreQueryFilters().Where(n => n.EntityId == sweep).OrderBy(n => n.RecipientUserId).ToListAsync();

    [Fact]
    public async Task Object_failure_does_not_leave_a_row_pointing_to_missing_thumbnail()
    {
        media.FailThumbnail = true;
        Assert.True(await Processor().ProcessOneAsync(sweepId: sweep));
        await using var db = Db();
        Assert.True(media.ImageWrites > 0);
        Assert.False(await db.Set<SurveyFrame>().AnyAsync(x => x.SweepId == sweep));
        Assert.False(await db.Set<Detection>().AnyAsync(x => x.SweepId == sweep));
        Assert.Equal("failed", (await db.Set<SurveyProcessingRun>().SingleAsync(x => x.SweepId == sweep)).ResultState);
    }

    [Fact]
    public async Task Concurrent_claims_produce_one_run_and_one_completion_audit_per_commune()
    {
        var worked = await Task.WhenAll(Processor().ProcessOneAsync(sweepId: sweep), Processor().ProcessOneAsync(sweepId: sweep));
        Assert.Single(worked, x => x);
        await using var db = Db();
        var run = await db.Set<SurveyProcessingRun>().SingleAsync(x => x.SweepId == sweep);
        Assert.Equal("succeeded", run.ResultState);
        Assert.Equal(100, run.CoveragePct);
        var item = await db.Set<SurveySweep>().SingleAsync(x => x.SweepId == sweep);
        Assert.Equal(SweepStatus.AwaitingReview, item.Status);
        Assert.Equal(SweepProcessingStatus.Succeeded, item.ProcessingStatus);
        Assert.Equal(poles.Order(), (await db.Set<PoleObservation>().Where(x => poles.Contains(x.PoleId)).Select(x => x.PoleId).ToArrayAsync()).Order());
        var audit = await db.Set<AuditEvent>().Where(x => x.Action == AuditAction.Completed && x.EntityId == sweep).ToArrayAsync();
        Assert.Equal(2, audit.Length);
        Assert.Equal(new[] { a, b }.Order(), audit.Select(x => x.CommuneId).Order());
        Assert.All(audit, x => Assert.Equal(AuditActorKind.Cv, x.ActorKind));
    }

    [Fact]
    public async Task Expired_lease_is_reclaimed_with_a_new_attempt()
    {
        await using (var db = Db())
        {
            using var seed = db.EnterUnscopedSystemWriteBackdoor();
            var item = await db.Set<SurveySweep>().SingleAsync(x => x.SweepId == sweep);
            item.ProcessingStatus = SweepProcessingStatus.Processing; item.Status = SweepStatus.Processing;
            item.ProcessingAttempt = 1; item.ProcessingLeaseOwner = Guid.NewGuid(); item.ProcessingLeaseExpiresAt = DateTime.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync();
        }
        Assert.True(await Processor().ProcessOneAsync(sweepId: sweep));
        await using var verify = Db();
        Assert.Equal(2, (await verify.Set<SurveyProcessingRun>().SingleAsync(x => x.SweepId == sweep)).Attempt);
        Assert.Null((await verify.Set<SurveySweep>().SingleAsync(x => x.SweepId == sweep)).ProcessingLeaseOwner);
    }

    [Fact]
    public async Task Invalid_clock_keeps_failure_diagnostics_and_no_success_or_observation()
    {
        await using (var db = Db()) await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM survey_lux_sample WHERE sweep_id = {sweep} AND sample_no > 1");
        Assert.True(await Processor().ProcessOneAsync(sweepId: sweep));
        await using var verify = Db();
        var run = await verify.Set<SurveyProcessingRun>().SingleAsync(x => x.SweepId == sweep);
        Assert.Equal("CLOCK_INSUFFICIENT", run.ErrorCode);
        Assert.Equal("clock", run.Stage);
        Assert.Equal(SweepProcessingStatus.Failed, (await verify.Set<SurveySweep>().SingleAsync(x => x.SweepId == sweep)).ProcessingStatus);
        Assert.Empty(await verify.Set<PoleObservation>().Where(x => poles.Contains(x.PoleId)).ToArrayAsync());
        var notices = await Notices(verify);
        Assert.Equal(new[] { bothManager, userId }.Order(), notices.Select(n => n.RecipientUserId).Order());
        Assert.All(notices, n => Assert.Equal((NotificationType.SurveyProcessingFailed, $"Phiếu {orderId}. Mã lỗi: CLOCK_INSUFFICIENT."), (n.Type, n.Body)));
    }

    [Fact]
    public async Task Revoked_commune_permission_fails_job_without_writing_foreign_results()
    {
        await using (var db = Db()) await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM app_user_commune WHERE commune_id = {b}");
        Assert.True(await Processor().ProcessOneAsync(sweepId: sweep));
        await using var verify = Db();
        Assert.Equal("JOB_SCOPE_INVALID", (await verify.Set<SurveyProcessingRun>().SingleAsync(x => x.SweepId == sweep)).ErrorCode);
        Assert.Empty(await verify.Set<PoleObservation>().Where(x => poles.Contains(x.PoleId)).ToArrayAsync());
    }

    [Fact]
    public async Task Finite_job_scope_blocks_a_foreign_observation_write()
    {
        await using var db = Db([a]);
        db.Add(new PoleObservation { PoleId = poles[1], CommuneId = b, QualityFlags = "[]" });
        Assert.Equal("COMMUNE_FORBIDDEN", (await Assert.ThrowsAsync<LuxMapException>(() => db.SaveChangesAsync())).Code);
    }

    [Fact]
    public async Task Database_trigger_rejects_rewriting_completed_results()
    {
        await Processor().ProcessOneAsync(sweepId: sweep);
        await using var db = Db();
        var error = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE survey_processing_run SET stage = stage WHERE sweep_id = {sweep}"));
        Assert.Equal("55000", error.SqlState);
        // DisposeAsync then exercises the SET LOCAL purge path on the same immutable tables.
    }

    private sealed class AdvancingClock(double stepSeconds) : TimeProvider
    {
        private DateTimeOffset value = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() { var now = value; value = value.AddSeconds(stepSeconds); return now; }
    }
    private sealed class CapturingLogger : ILogger<SurveyProcessor>
    {
        public List<(LogLevel Level, string Message, Exception? Error)> Events { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel level, EventId eventId, TState state, Exception? error, Func<TState,Exception?,string> format)
            => Events.Add((level,format(state,error),error));
    }

    [Fact]
    public async Task Checkpoints_renew_the_lease_and_preserve_the_completion_audit()
    {
        var processor = new SurveyProcessor(source,catalog,Options.Create(new SurveyProcessingOptions()),
            NullLogger<SurveyProcessor>.Instance,new AdvancingClock(35), media.Pipeline);
        Assert.True(await processor.ProcessOneAsync(sweepId: sweep));
        await using var db = Db();
        var run = await db.Set<SurveyProcessingRun>().SingleAsync(x => x.SweepId == sweep);
        Assert.Equal("succeeded",run.ResultState);
        Assert.True(run.LeaseExpiresAt > run.StartedAt.AddSeconds(120));
        Assert.True(await db.Set<AuditEvent>().AnyAsync(x => x.EntityId == sweep && x.Action == AuditAction.DetailsChanged));
        Assert.Equal(2,await db.Set<AuditEvent>().CountAsync(x => x.EntityId == sweep && x.Action == AuditAction.Completed));
    }

    [Fact]
    public async Task Expired_owner_logs_warning_and_cannot_publish()
    {
        var log = new CapturingLogger();
        var processor = new SurveyProcessor(source,catalog,Options.Create(new SurveyProcessingOptions()),log,new AdvancingClock(121));
        Assert.True(await processor.ProcessOneAsync(sweepId: sweep));
        await using var db = Db();
        Assert.False(await db.Set<SurveyProcessingRun>().AnyAsync(x => x.SweepId == sweep));
        Assert.Contains(log.Events,x => x.Level == LogLevel.Warning && x.Message.Contains(sweep,StringComparison.Ordinal));
        Assert.Equal(SweepProcessingStatus.Processing,(await db.Set<SurveySweep>().SingleAsync(x => x.SweepId == sweep)).ProcessingStatus);
    }

    private sealed class FailingCheckpointClock : TimeProvider
    {
        private int calls;
        public override DateTimeOffset GetUtcNow() => ++calls == 2
            ? throw new InvalidOperationException("Simulated checkpoint failure") : DateTimeOffset.UtcNow;
    }

    [Fact]
    public async Task Unexpected_errors_are_logged_and_retries_stop_at_the_limit()
    {
        var log = new CapturingLogger();
        for (int attempt=1;attempt<=3;attempt++)
        {
            var processor = new SurveyProcessor(source,catalog,Options.Create(new SurveyProcessingOptions()),log,new FailingCheckpointClock());
            Assert.True(await processor.ProcessOneAsync(sweepId: sweep));
        }
        Assert.False(await Processor().ProcessOneAsync(sweepId: sweep));
        await using var db = Db();
        var runs = await db.Set<SurveyProcessingRun>().Where(x=>x.SweepId==sweep).ToArrayAsync();
        Assert.Equal(3,runs.Length);
        Assert.All(runs,x=> { Assert.Equal("failed",x.ResultState); Assert.Equal("PROCESSING_ERROR",x.ErrorCode); });
        Assert.Equal(SweepProcessingStatus.Failed,(await db.Set<SurveySweep>().SingleAsync(x=>x.SweepId==sweep)).ProcessingStatus);
        Assert.Equal(3,log.Events.Count(x=>x.Level==LogLevel.Error && x.Error is InvalidOperationException
            && x.Message.Contains(sweep,StringComparison.Ordinal)));
        // BE-27: a retry is not news — only the final failure is told, once per person.
        Assert.Equal(2, (await Notices(db)).Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Parallel_routes_choose_only_nearest_or_reject_equal_distance(bool ambiguous)
    {
        await using var db = Db();
        using (db.EnterUnscopedSystemWriteBackdoor())
        {
            var original = await db.Set<RoadSegment>().SingleAsync(x => x.SegmentId == road);
            var other = new RoadSegment { CommuneId = a, SegmentName = "Parallel", RoadClass = RoadClass.InterVillage,
                DataSource = DataSource.Simulated, LengthM = 999,
                Geom = new LineString(original.Geom.Coordinates.Select(c => new Coordinate(c.X, c.Y + .000135)).ToArray()) { SRID = 4326 } };
            db.Add(other); await db.SaveChangesAsync(); parallelRoad = other.SegmentId;
            db.Add(new Pole { CommuneId = a, SegmentId = parallelRoad, DataSource = DataSource.Simulated,
                Geom = new Point(108.00025,16.000145) { SRID = 4326 } });
            db.Add(new WorkOrderSegment { WorkOrderId = orderId, CommuneId = a, SegmentId = parallelRoad, Position = 1 });
            await db.SaveChangesAsync();
        }
        if (ambiguous)
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE survey_gps_sample SET geom = ST_Translate(geom,0,0.0000675) WHERE sweep_id = {sweep}");
        var projected = await SurveyChainageQuery.Gps(db, sweep, [road,parallelRoad], [a,b], new(), default);
        Assert.Equal(21, projected.Length);
        Assert.All(projected, x => Assert.Equal(ambiguous, x.RouteAmbiguous));
        if (!ambiguous) Assert.All(projected, x => Assert.Equal(road, x.SegmentId));
        Assert.True(await Processor().ProcessOneAsync(sweepId: sweep));
        var run = await db.Set<SurveyProcessingRun>().SingleAsync(x => x.SweepId == sweep);
        var passes = await db.Set<SurveyPass>().Where(x => x.RunId == run.RunId).ToArrayAsync();
        Assert.DoesNotContain(passes, x => x.SegmentId == parallelRoad);
        if (ambiguous) { Assert.Empty(passes); Assert.Equal(0, run.CoveragePct); }
        else { Assert.Single(passes); Assert.InRange(run.CoveragePct!.Value, 66, 67); }
    }

    [Theory]
    [InlineData(.00032, 30, 40)]
    [InlineData(.002, 210, 230)]
    public async Task Short_lateral_drift_is_retained_and_marks_crossing_degraded(double latitudeShift, double minimum, double maximum)
    {
        await using var db = Db();
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE survey_gps_sample SET geom = ST_Translate(geom,0,{latitudeShift}) WHERE sweep_id = {sweep} AND sample_no BETWEEN 4 AND 7");
        var gps = await SurveyChainageQuery.Gps(db, sweep, [road], [a,b], new(), default);
        Assert.Equal(21, gps.Length);
        Assert.All(gps.Where(x => x.TimeNs >= 4_000_000_000L && x.TimeNs <= 7_000_000_000L), x => Assert.InRange(x.RouteDistanceM!.Value, minimum, maximum));
        Assert.True(await Processor().ProcessOneAsync(sweepId: sweep));
        var run = await db.Set<SurveyProcessingRun>().SingleAsync(x => x.SweepId == sweep);
        Assert.Single(await db.Set<SurveyPass>().Where(x => x.RunId == run.RunId).ToArrayAsync());
        var observation = await db.Set<PoleObservation>().SingleAsync(x => x.RunId == run.RunId && x.PoleId == poles[0]);
        Assert.Contains("gps_degraded", observation.QualityFlags, StringComparison.Ordinal);
    }

    private async Task PlacePolesOnRouteForChainageTests()
    {
        // Midpoints of the two route legs preserve the quarter/three-quarter assertions.
        // CV tests keep the default off-route poles so their camera-side evidence remains valid.
        await using var db = Db();
        var assets = await db.Set<Pole>().Where(x => poles.Contains(x.PoleId)).ToArrayAsync();
        foreach (var pole in assets)
            pole.Geom = new Point(pole.PoleId == poles[0] ? 108.00025 : 108.00075, 16.00001) { SRID = 4326 };
        await db.SaveChangesAsync();
    }

    private sealed class ChangeRouteBetweenProjections(Func<Task> change) : DbCommandInterceptor
    {
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("ST_LineLocatePoint", StringComparison.Ordinal))
            {
                Assert.Equal(System.Data.IsolationLevel.RepeatableRead, command.Transaction?.IsolationLevel);
                if (command.CommandText.Contains("JOIN pole p", StringComparison.Ordinal)) await change();
            }
            return result;
        }
    }

    [Fact]
    public async Task Projection_snapshot_survives_a_concurrent_route_edit()
    {
        await PlacePolesOnRouteForChainageTests();
        var interceptor = new ChangeRouteBetweenProjections(async () =>
        {
            await using var writer = Db();
            await writer.Database.ExecuteSqlInterpolatedAsync($"UPDATE road_segment SET geom = ST_Translate(geom,0.0004,0) WHERE segment_id = {road}");
        });
        await using var db = Db(interceptor: interceptor);
        var snapshot = await SurveyChainageQuery.Snapshot(db, sweep, [road], [a,b], "simulated", new(), default);
        Assert.InRange(snapshot.Poles[0].ChainageM / snapshot.Poles[0].LengthM, .249, .251);
        Assert.Equal(snapshot.Gps[0].RouteGeometryJson, snapshot.Poles[0].RouteGeometryJson);
        await using var after = Db();
        var changed = await SurveyChainageQuery.Poles(after, [road], [a,b], "simulated", new(), default);
        Assert.InRange(changed[0].ChainageM, 0, 1);
        Assert.NotEqual(snapshot.Poles[0].RouteGeometryJson, changed[0].RouteGeometryJson);
    }

    private sealed class CaptureProjection : DbCommandInterceptor
    {
        public string? Text { get; private set; }
        public (string Name, object? Value)[] Parameters { get; private set; } = [];
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("ST_LineLocatePoint",StringComparison.Ordinal))
            {
                Text = command.CommandText;
                Parameters = command.Parameters.Cast<DbParameter>().Select(p=>(p.ParameterName,p.Value)).ToArray();
            }
            return ValueTask.FromResult(result);
        }
    }

    [Fact]
    public async Task Chainage_is_projected_metres_and_its_actual_sql_uses_gist()
    {
        await PlacePolesOnRouteForChainageTests();
        var capture = new CaptureProjection();
        await using var db = Db(interceptor: capture);
        var projected = await SurveyChainageQuery.Poles(db, [road], [a,b], "simulated", new(), default);
        Assert.Equal(2, projected.Length);
        Assert.InRange(projected[0].LengthM, 105, 110); // Declared length is deliberately 999.
        Assert.InRange(projected[0].ChainageM / projected[0].LengthM, .249, .251);
        Assert.InRange(projected[1].ChainageM / projected[1].LengthM, .749, .751);
        Assert.NotNull(capture.Text);
        // Same segment but outside its bbox: a meaningful selectivity test for the exact production query.
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO pole (segment_id, commune_id, geom, data_source, near_sensitive_poi)
            SELECT {road}, {a}, ST_SetSRID(ST_MakePoint(108.02 + i * 0.00001, 16.02),4326), 'simulated', false
            FROM generate_series(1,2000) AS i
            """);
        await db.Database.ExecuteSqlRawAsync("ANALYZE pole");
        await db.Database.OpenConnectionAsync();
        await using var transaction = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlRawAsync("SET LOCAL enable_seqscan = off");
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.CommandText = "EXPLAIN (FORMAT JSON) " + capture.Text;
        foreach (var (name,value) in capture.Parameters)
        {
            var parameter = command.CreateParameter(); parameter.ParameterName = name; parameter.Value = value;
            command.Parameters.Add(parameter);
        }
        var plan = (string)(await command.ExecuteScalarAsync())!;
        Assert.Contains("ix_pole_geom", plan, StringComparison.Ordinal);
    }
}
