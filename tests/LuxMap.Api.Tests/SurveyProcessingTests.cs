using LuxMap.Modules.Assets.Entities;
using LuxMap.Modules.Identity.Entities;
using LuxMap.Modules.Survey.Entities;
using LuxMap.Modules.Survey.Processing;
using LuxMap.Modules.WorkOrders.Entities;
using LuxMap.Persistence;
using LuxMap.Persistence.Audit;
using LuxMap.Shared.Authorization;
using LuxMap.Shared.Http;
using Microsoft.Extensions.Logging;
using LuxMap.Shared.Contracts.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using System.Data.Common;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging.Abstractions;
using NetTopologySuite.Geometries;
using Npgsql;

namespace LuxMap.Api.Tests;

/// <summary>Requires the migrated test database. Author compiles only; Claude runs this suite.
/// Own data is removed in a transaction using the audit teardown convention.</summary>
[Collection(nameof(AssetDatabaseCollection))]
public sealed class SurveyProcessingTests(AssetImportFixture factory) : IAsyncLifetime
{
    private NpgsqlDataSource source = null!;
    private ModuleAssemblyCatalog catalog = null!;
    private string a = null!, b = null!, road = null!, sweep = null!;
    private string[] poles = [];
    private string userId = null!, orderId = null!;
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
    private SurveyProcessor Processor() => new(source, catalog, Options.Create(new SurveyProcessingOptions()), NullLogger<SurveyProcessor>.Instance);

    public async Task InitializeAsync()
    {
        catalog = factory.Services.GetRequiredService<ModuleAssemblyCatalog>();
        source = factory.Services.GetRequiredService<NpgsqlDataSource>();
        await using var db = Db();
        using var seed = db.EnterUnscopedSystemWriteBackdoor();
        var communes = new[] { new AdministrativeUnit { Name = "A" }, new AdministrativeUnit { Name = "B" } };
        db.AddRange(communes); await db.SaveChangesAsync(); a = communes[0].CommuneId; b = communes[1].CommuneId;
        var user = new AppUser { Username = "processing" + Guid.NewGuid().ToString("N"), Email = Guid.NewGuid() + "@example.invalid", FullName = "Test engineer",
            PasswordHash = "unused", PasswordAlgorithm = "pbkdf2-aspnetcore-v3", Role = UserRole.FieldEngineer };
        db.Add(user); await db.SaveChangesAsync(); userId = user.UserId;
        db.AddRange(new AppUserCommune { UserId = user.UserId, CommuneId = a }, new AppUserCommune { UserId = user.UserId, CommuneId = b });
        var route = new RoadSegment { CommuneId = a, SegmentName = "Curve", RoadClass = RoadClass.InterVillage,
            DataSource = DataSource.Simulated, LengthM = 999, Geom = new LineString([new(108,16), new(108.0005,16.00002), new(108.001,16)]) { SRID = 4326 } };
        db.Add(route); await db.SaveChangesAsync(); road = route.SegmentId;
        var assets = new[] { new Pole { CommuneId = a, SegmentId = road, DataSource = DataSource.Simulated, Geom = new Point(108.00025,16.00001) { SRID = 4326 } },
            new Pole { CommuneId = b, SegmentId = road, DataSource = DataSource.Simulated, Geom = new Point(108.00075,16.00001) { SRID = 4326 } } };
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
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await using var db = Db();
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlRawAsync("SET LOCAL luxmap.audit_purge = 'on'");
        var versions = await db.Set<SurveyProcessingRun>().Where(x => x.SweepId == sweep)
            .Select(x => new { x.AlgorithmVersionId, x.ClockVersionId }).ToArrayAsync();
        var ids = versions.SelectMany(x => new[] { x.AlgorithmVersionId, x.ClockVersionId }).Distinct().ToArray();
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM audit_event WHERE commune_id = {a} OR commune_id = {b}");
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM pole_observation WHERE run_id IN (SELECT run_id FROM survey_processing_run WHERE sweep_id = {sweep})");
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM survey_pass WHERE run_id IN (SELECT run_id FROM survey_processing_run WHERE sweep_id = {sweep})");
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM survey_processing_run WHERE sweep_id = {sweep}");
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM artifact_version WHERE version_id = ANY({ids}) AND NOT EXISTS (SELECT 1 FROM survey_processing_run WHERE algorithm_version_id = version_id OR clock_version_id = version_id)");
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM survey_gps_sample WHERE sweep_id = {sweep}");
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM survey_lux_sample WHERE sweep_id = {sweep}");
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM survey_sweep WHERE sweep_id = {sweep}");
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM work_order_segment WHERE work_order_id = {orderId}");
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM work_order WHERE work_order_id = {orderId}");
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM pole WHERE segment_id = {road}");
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM road_segment WHERE segment_id = {road}");
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM app_user_commune WHERE user_id = {userId}");
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM app_user WHERE user_id = {userId}");
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM administrative_unit WHERE commune_id = {a} OR commune_id = {b}");
        await tx.CommitAsync();
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
            NullLogger<SurveyProcessor>.Instance,new AdvancingClock(35));
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
