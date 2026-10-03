using System.Net;
using LuxMap.Modules.Assets.Entities;
using LuxMap.Modules.Faults.Entities;
using LuxMap.Modules.Identity.Entities;
using LuxMap.Modules.Survey.Entities;
using LuxMap.Modules.Survey.Processing.Frames;
using LuxMap.Modules.Survey.Review;
using LuxMap.Modules.WorkOrders.Entities;
using LuxMap.Persistence;
using LuxMap.Persistence.Audit;
using LuxMap.Shared.Authorization;
using LuxMap.Shared.Contracts.Enums;
using LuxMap.Shared.Contracts.Paging;
using LuxMap.Shared.Http;
using LuxMap.Shared.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NetTopologySuite.Geometries;
using Npgsql;

namespace LuxMap.Api.Tests;

/// <summary>PostGIS suite, compiled here only. Claude runs it on the migrated test database.</summary>
[Collection(nameof(AssetDatabaseCollection))]
public sealed class SurveyPublicationTests(AssetImportFixture factory) : IAsyncLifetime
{
    private NpgsqlDataSource source = null!;
    private ModuleAssemblyCatalog catalog = null!;
    private string a = null!, b = null!, road = null!, order = null!, user = null!;
    private string[] poles = [];
    private long algorithm, clock, model, extractor;
    private readonly ObjectSpy objects = new();
    private sealed class Scope(string[] ids) : ICommuneScopeAccessor { public CommuneScope ScopeValue => CommuneScope.ForCommunes(ids); CommuneScope ICommuneScopeAccessor.Scope => ScopeValue; }
    private sealed class Actor(string id) : ICurrentActorAccessor { public string? UserId => id; public UserRole? Role => UserRole.Manager; }
    private sealed class Correlation : ICorrelationIdAccessor { public string CorrelationId => "survey-publication-test"; }
    private LuxMapDbContext Db(string[]? ids = null, Microsoft.EntityFrameworkCore.Diagnostics.IInterceptor? interceptor = null)
    {
        var builder = new DbContextOptionsBuilder<LuxMapDbContext>();
        PersistenceServiceCollectionExtensions.Configure(builder, source);
        if (interceptor is not null) builder.AddInterceptors(interceptor);
        return new(builder.Options, catalog, new Scope(ids ?? [a, b]), new Actor(user));
    }
    private SurveyReviewService Service(LuxMapDbContext db, string[]? ids = null) => new(db, new Actor(user),
        new Scope(ids ?? [a, b]), new AuditTrail(db, new Correlation()), objects, Options.Create(new SurveyReviewOptions()));

    public async Task InitializeAsync()
    {
        catalog = factory.Services.GetRequiredService<ModuleAssemblyCatalog>(); source = factory.Services.GetRequiredService<NpgsqlDataSource>();
        await using var db = Db([]); using var seed = db.EnterUnscopedSystemWriteBackdoor();
        var communes = new[] { new AdministrativeUnit { Name = "publication A" }, new AdministrativeUnit { Name = "publication B" } };
        db.AddRange(communes); await db.SaveChangesAsync(); a = communes[0].CommuneId; b = communes[1].CommuneId;
        var person = new AppUser { Username = "review" + Guid.NewGuid().ToString("N"), Email = Guid.NewGuid() + "@example.invalid",
            FullName = "Test manager", PasswordHash = "unused", PasswordAlgorithm = "pbkdf2-aspnetcore-v3", Role = UserRole.Manager };
        db.Add(person); await db.SaveChangesAsync(); user = person.UserId;
        var segment = new RoadSegment { CommuneId = a, SegmentName = "Survey publication", RoadClass = RoadClass.InterVillage,
            DataSource = DataSource.Simulated, LengthM = 100, Geom = new LineString([new(108,16), new(108.001,16)]) { SRID = 4326 } };
        db.Add(segment); await db.SaveChangesAsync(); road = segment.SegmentId;
        var assets = new[] { new Pole { CommuneId = a, SegmentId = road, DataSource = DataSource.Simulated, NearSensitivePoi = true, Geom = new Point(108,16) { SRID = 4326 } },
            new Pole { CommuneId = b, SegmentId = road, DataSource = DataSource.Simulated, Geom = new Point(108.001,16) { SRID = 4326 } } };
        db.AddRange(assets); await db.SaveChangesAsync(); poles = assets.Select(p => p.PoleId).ToArray();
        var wo = new WorkOrder { CommuneId = a, CreatedBy = user, AssignedTo = user, AssignedAt = DateTime.UtcNow, TaskKind = TaskKind.Survey,
            WoStatus = WorkOrderStatus.InProgress, Title = "Survey", StartedAt = DateTime.UtcNow };
        db.Add(wo); await db.SaveChangesAsync(); order = wo.WorkOrderId;
        db.Add(new WorkOrderSegment { WorkOrderId = order, CommuneId = a, SegmentId = road });
        var versions = new[] { "association_algorithm", "clock_algorithm", "cv_model", "frame_extractor" }.Select(c => new ArtifactVersion
            { Component = c, Version = Guid.NewGuid().ToString("N"), ArtifactHash = new('a', 64), CreatedAt = DateTime.UtcNow }).ToArray();
        db.AddRange(versions); await db.SaveChangesAsync();
        algorithm = versions[0].VersionId; clock = versions[1].VersionId; model = versions[2].VersionId; extractor = versions[3].VersionId;
    }

    private sealed record Capture(string Id, long Run, uint Version, string Frame, DateTime At);
    private async Task<Capture> Plant(int day, double lux = 100, string state = "on", string direction = "forward")
    {
        await using var db = Db(); using var seed = db.EnterUnscopedSystemWriteBackdoor();
        var at = DateTime.UnixEpoch.AddDays(20000 + day);
        var sweep = new SurveySweep { WorkOrderId = order, CommuneId = a, CapturedBy = user, ClientOpId = Guid.NewGuid(), BootSessionId = Guid.NewGuid(),
            CreateRequestHash = new('a',64), UtcAnchor = at, EndedElapsedNs = 10_000_000_000L, DataSource = DataSource.Simulated,
            Status = SweepStatus.AwaitingReview, ProcessingStatus = SweepProcessingStatus.Succeeded };
        db.Add(sweep); await db.SaveChangesAsync();
        var run = new SurveyProcessingRun { SweepId = sweep.SweepId, CommuneId = a, Attempt = 1, LeaseOwner = Guid.NewGuid(),
            LeaseExpiresAt = at.AddMinutes(1), InputHash = new('b',64), SettingsSnapshot = "{}", GisSnapshot = "{}",
            AlgorithmVersionId = algorithm, ClockVersionId = clock, ModelVersionId = model, ExtractorVersionId = extractor,
            ResultState = "succeeded", Stage = "complete", StartedAt = at, FinishedAt = at.AddSeconds(10) };
        db.Add(run);
        var clip = new SurveyVideoClip { SweepId = sweep.SweepId, ClipNo = 0, ObjectKey = "test", Sha256 = new('c',64), ByteCount = 4, ContentType = "video/mp4", StoredAt = at };
        db.Add(clip); await db.SaveChangesAsync();
        var frame = new SurveyFrame { SweepId = sweep.SweepId, ClipId = clip.ClipId, ExtractorVersionId = extractor, ObjectKey = "original",
            ThumbnailKey = "thumb", Sha256 = new('d',64), ByteCount = 4, ThumbnailBytes = 4, Width = 1, Height = 1, DataSource = DataSource.Simulated };
        var pass = new SurveyPass { Run = run, SegmentId = road, Direction = direction, FromFraction = direction == "forward" ? 0 : 1,
            ToFraction = direction == "forward" ? 1 : 0, EndElapsedNs = 10_000_000_000L, QualityFlags = "{}" };
        db.AddRange(frame, pass); await db.SaveChangesAsync();
        for (int i = 0; i < poles.Length; i++)
        {
            var baseline = await new SurveyBaselineLookup(db).FindAsync(new(poles[i], direction, sweep.SweepId, at, DataSource.Simulated), default);
            var cv = i == 0 ? state : "off";
            var classification = FrameClassification.Classify(new(cv, .95, frame.FrameId, 1, "associated"), lux, baseline?.Value, [], .8);
            db.Add(new PoleObservation { Run = run, Pass = pass, PoleId = poles[i], CommuneId = i == 0 ? a : b, DataSource = DataSource.Simulated,
                ObservedAt = at.AddSeconds(5), ObservedElapsedNs = 5_000_000_000L, PeakAtElapsedNs = 5_000_000_000L, PeakLux = lux,
                AssociationConfidence = .9, CvState = cv, CvConfidence = .95, RepresentativeFrameId = frame.FrameId,
                ClassifiedAs = classification.Status, BaselineRatio = classification.BaselineRatio, BaselineId = baseline?.Id, BaselineValue = baseline?.Value,
                DimEvaluationEligible = classification.DimEvaluationEligible, QualityFlags = "[]", ReasonCodes = System.Text.Json.JsonSerializer.Serialize(classification.Reasons) });
        }
        await db.SaveChangesAsync(); return new(sweep.SweepId, run.RunId, sweep.Version, frame.FrameId, at);
    }
    private async Task<ReviewSweepResponse> Accept(Capture c, Guid? operation = null)
    {
        await using var db = Db(); return await Service(db).Review(c.Id, new(operation ?? Guid.NewGuid(), c.Run, "accept", null, c.Version), default);
    }

    [Fact]
    public async Task Accept_publishes_all_communes_audits_faults_and_preserves_the_order_then_retry_is_idempotent()
    {
        var c = await Plant(1, state: "off"); var operation = Guid.NewGuid();
        var first = await Accept(c, operation); var retry = await Accept(c, operation);
        Assert.Equal(first, retry);
        await using var db = Db();
        Assert.Equal(2, await db.Set<LuminanceHistory>().CountAsync(h => h.SweepId == c.Id));
        Assert.Equal(2, await db.Set<PoleCurrentStatus>().CountAsync(h => h.LastSweepId == c.Id));
        var faults = await db.Set<Fault>().Where(f => poles.Contains(f.PoleId!)).ToArrayAsync(); Assert.Equal(2, faults.Length);
        Assert.All(faults, f => { Assert.Equal(SourceChannel.Cv, f.SourceChannel); Assert.NotNull(f.OriginObservationId); Assert.NotNull(f.DetectionModelVersion); Assert.Null(f.PriorityScore); });
        Assert.Equal(Severity.High, faults.Single(f => f.PoleId == poles[0]).Severity);
        Assert.Equal(2, await db.Set<AuditEvent>().CountAsync(e => e.ActorKind == AuditActorKind.Cv && e.EntityType == AuditEntityType.Fault && (e.CommuneId == a || e.CommuneId == b)));
        Assert.Equal(WorkOrderStatus.InProgress, (await db.Set<WorkOrder>().SingleAsync(w => w.WorkOrderId == order)).WoStatus);
        Assert.All(await db.Set<LuminanceHistory>().Where(h => h.SweepId == c.Id).ToArrayAsync(), h => Assert.Equal(c.At.AddSeconds(5), h.EvaluatedAt));
    }

    [Fact]
    public async Task Late_and_equal_capture_only_add_history_and_do_not_create_historical_faults()
    {
        var late = await Plant(1, state: "off"); var newer = await Plant(2); var equal = await Plant(2, state: "off");
        await Accept(newer); await Accept(late); await Accept(equal);
        await using var db = Db();
        var current = await db.Set<PoleCurrentStatus>().SingleAsync(p => p.PoleId == poles[0]);
        Assert.Equal(newer.Id, current.LastSweepId); Assert.Equal(FixtureStatus.Normal, current.FixtureStatus);
        Assert.False(await db.Set<Fault>().AnyAsync(f => f.PoleId == poles[0]));
        Assert.Equal(6, await db.Set<LuminanceHistory>().CountAsync(h => poles.Contains(h.PoleId)));
    }

    [Fact]
    public async Task Existing_open_effective_type_suppresses_duplicate_and_normal_does_not_close_fault()
    {
        await Accept(await Plant(1, state: "off"));
        await using (var setup = Db())
        {
            using var seed = setup.EnterUnscopedSystemWriteBackdoor();
            var existing = await setup.Set<Fault>().SingleAsync(f => f.PoleId == poles[0]);
            existing.FaultType = FaultType.LampDim; existing.OverrideFaultType = FaultType.LampOut;
            await setup.SaveChangesAsync();
        }
        await Accept(await Plant(2, state: "off")); await Accept(await Plant(3));
        await using var db = Db(); Assert.Equal(2, await db.Set<Fault>().CountAsync(f => poles.Contains(f.PoleId!)));
        Assert.All(await db.Set<Fault>().Where(f => poles.Contains(f.PoleId!)).ToArrayAsync(), f => Assert.Equal(FaultStatus.Detected, f.FaultStatus));
    }

    [Fact]
    public async Task Concurrent_different_decisions_have_exactly_one_winner()
    {
        var c = await Plant(1);
        async Task<bool> Try(string decision)
        {
            await using var db = Db();
            try { await Service(db).Review(c.Id, new(Guid.NewGuid(), c.Run, decision, "review", c.Version), default); return true; }
            catch (LuxMapException e) when (e.StatusCode == HttpStatusCode.Conflict) { return false; }
        }
        var outcomes = await Task.WhenAll(Try("accept"), Try("return")); Assert.Single(outcomes, x => x);
    }

    [Fact]
    public async Task Missing_commune_refuses_review_and_thumbnail_before_object_access()
    {
        var c = await Plant(1); await using var db = Db([a]); var service = Service(db, [a]);
        var review = await Assert.ThrowsAsync<LuxMapException>(() => service.Review(c.Id, new(Guid.NewGuid(), c.Run, "accept", null, c.Version), default));
        Assert.Equal(HttpStatusCode.Forbidden, review.StatusCode);
        var thumbnail = await Assert.ThrowsAsync<LuxMapException>(() => service.Thumbnail(c.Frame, default));
        Assert.Equal(HttpStatusCode.NotFound, thumbnail.StatusCode); Assert.Equal(0, objects.Reads);
    }

    [Fact]
    public async Task Return_requires_note_and_publishes_nothing()
    {
        var c = await Plant(1); await using var db = Db(); var service = Service(db);
        var invalid = await Assert.ThrowsAsync<LuxMapException>(() => service.Review(c.Id, new(Guid.NewGuid(), c.Run, "return", " ", c.Version), default));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var result = await service.Review(c.Id, new(Guid.NewGuid(), c.Run, "return", "Capture again", c.Version), default);
        Assert.Equal(SweepStatus.Returned, result.Status); Assert.Null(result.AcceptedRunId);
        Assert.False(await db.Set<LuminanceHistory>().AnyAsync(h => h.SweepId == c.Id));
        Assert.False(await db.Set<PoleCurrentStatus>().AnyAsync(h => poles.Contains(h.PoleId)));
    }

    [Fact]
    public async Task A_corrected_pole_blocks_accept_but_not_return()
    {
        var c = await Plant(1);
        await using (var edit = Db())
        {
            var pole = await edit.Set<Pole>().SingleAsync(p => p.PoleId == poles[0]);
            pole.DataSource = DataSource.CalibrationRig; await edit.SaveChangesAsync();
        }
        await using var db = Db(); var service = Service(db);
        var accept = await Assert.ThrowsAsync<LuxMapException>(() => service.Review(c.Id, new(Guid.NewGuid(), c.Run, "accept", null, c.Version), default));
        Assert.Equal(("SURVEY_SCOPE_CHANGED", HttpStatusCode.Conflict), (accept.Code, accept.StatusCode));
        db.ChangeTracker.Clear();
        var returned = await service.Review(c.Id, new(Guid.NewGuid(), c.Run, "return", "Pole data source was corrected", c.Version), default);
        Assert.Equal(SweepStatus.Returned, returned.Status);
        Assert.False(await db.Set<LuminanceHistory>().AnyAsync(h => h.SweepId == c.Id));
    }

    [Fact]
    public async Task Accepted_simulated_captures_build_median_then_next_capture_can_be_dim_or_normal_without_self_scoring()
    {
        var captures = new[] { await Plant(1, 100), await Plant(2, 110), await Plant(3, 90) };
        foreach (var c in captures) await Accept(c);
        await using (var db = Db())
        {
            var baseline = await db.Set<LuminanceBaseline>().SingleAsync(baseline => baseline.PoleId == poles[0]);
            Assert.Equal(100, baseline.Value); Assert.Equal(3, baseline.MemberCount);
            Assert.Equal(3, await db.Set<BaselineMember>().CountAsync(m => m.BaselineId == baseline.BaselineId));
            var lookup = new SurveyBaselineLookup(db);
            Assert.Null(await lookup.FindAsync(new(poles[0], "forward", captures[2].Id, captures[2].At.AddDays(20), DataSource.Simulated), default));
            Assert.Null(await lookup.FindAsync(new(poles[0], "reverse", "other", captures[2].At.AddDays(20), DataSource.Simulated), default));
            Assert.Null(await lookup.FindAsync(new(poles[0], "forward", "other", captures[1].At, DataSource.Simulated), default));
        }
        var dim = await Plant(4, 50); var normal = await Plant(5, 100);
        await Accept(dim); await Accept(normal);
        await using var verify = Db();
        var histories = await verify.Set<LuminanceHistory>().Where(h => h.PoleId == poles[0] && (h.SweepId == dim.Id || h.SweepId == normal.Id)).ToArrayAsync();
        Assert.All(histories, h => { Assert.True(h.DimEvaluationEligible); Assert.NotNull(h.BaselineId); });
        Assert.Equal(FixtureStatus.Dim, histories.Single(h => h.SweepId == dim.Id).ClassifiedAs);
        Assert.Equal(FixtureStatus.Normal, histories.Single(h => h.SweepId == normal.Id).ClassifiedAs);
        Assert.Single(await verify.Set<Fault>().Where(f => f.PoleId == poles[0] && f.FaultType == FaultType.LampDim).ToArrayAsync());
    }

    [Fact]
    public async Task Result_page_exposes_stored_baseline_and_thumbnail_is_proxy_stream()
    {
        var c = await Plant(1); await using var db = Db();
        var page = await Service(db).Results(c.Id, null, PageRequest.Create(1, 1), default);
        Assert.Equal(2, page.Total); Assert.Single(page.Items);
        await using var stream = await Service(db).Thumbnail(c.Frame, default); Assert.Equal(1, objects.Reads); Assert.Equal(4, stream.Length);
    }

    [Fact]
    public async Task Wrong_run_stale_version_and_changed_retry_body_are_conflicts()
    {
        var c = await Plant(1); var other = await Plant(2);
        await using (var db = Db())
        {
            var error = await Assert.ThrowsAsync<LuxMapException>(() => Service(db).Review(c.Id, new(Guid.NewGuid(), other.Run, "accept", null, c.Version), default));
            Assert.Equal("INVALID_REVIEW_RUN", error.Code);
        }
        await using (var db = Db())
        {
            var error = await Assert.ThrowsAsync<LuxMapException>(() => Service(db).Review(c.Id, new(Guid.NewGuid(), c.Run, "accept", null, c.Version + 1), default));
            Assert.Equal("VERSION_CONFLICT", error.Code);
        }
        var key = Guid.NewGuid(); await Accept(c, key);
        await using (var db = Db())
        {
            var error = await Assert.ThrowsAsync<LuxMapException>(() => Service(db).Review(c.Id, new(key, c.Run, "return", "changed", c.Version), default));
            Assert.Equal("IDEMPOTENCY_CONFLICT", error.Code);
        }
    }

    [Fact]
    public async Task Baseline_history_and_members_reject_database_mutation()
    {
        foreach (var day in new[] { 1, 2, 3 }) await Accept(await Plant(day));
        await using var db = Db();
        var error = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE luminance_baseline SET value = value + 1 WHERE pole_id = {poles[0]}"));
        Assert.Equal("55000", error.SqlState);
        error = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM luminance_history WHERE pole_id = {poles[0]}"));
        Assert.Equal("55000", error.SqlState);
        error = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM baseline_member WHERE baseline_id IN (SELECT baseline_id FROM luminance_baseline WHERE pole_id = {poles[0]})"));
        Assert.Equal("55000", error.SqlState);
    }

    private sealed class FailFaultSave : Microsoft.EntityFrameworkCore.Diagnostics.SaveChangesInterceptor
    {
        public override ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int>> SavingChangesAsync(
            Microsoft.EntityFrameworkCore.Diagnostics.DbContextEventData eventData,
            Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<Fault>().Any(e => e.State == EntityState.Added))
                throw new InvalidOperationException("Injected failure after publication batch");
            return ValueTask.FromResult(result);
        }
    }

    [Fact]
    public async Task Failure_after_history_batch_rolls_back_review_status_history_and_faults_together()
    {
        var c = await Plant(1);
        await using (var db = Db(interceptor: new FailFaultSave()))
            await Assert.ThrowsAsync<InvalidOperationException>(() => Service(db).Review(c.Id,
                new(Guid.NewGuid(), c.Run, "accept", null, c.Version), default));
        await using var verify = Db();
        Assert.Equal(SweepStatus.AwaitingReview, (await verify.Set<SurveySweep>().SingleAsync(s => s.SweepId == c.Id)).Status);
        Assert.False(await verify.Set<LuminanceHistory>().AnyAsync(h => h.SweepId == c.Id));
        Assert.False(await verify.Set<PoleCurrentStatus>().AnyAsync(p => poles.Contains(p.PoleId)));
        Assert.False(await verify.Set<Fault>().AnyAsync(f => poles.Contains(f.PoleId!)));
    }

    [Fact]
    public async Task Accepted_dim_sweeps_do_not_move_the_baseline()
    {
        foreach (int day in new[] { 1, 2, 3 }) await Accept(await Plant(day, 100));
        for (int day = 4; day < 12; day++)
        {
            var capture = await Plant(day, day < 9 ? 60 : 70);
            await Accept(capture);
            await using var db = Db();
            Assert.Equal(FixtureStatus.Dim, (await db.Set<LuminanceHistory>().SingleAsync(h => h.SweepId == capture.Id && h.PoleId == poles[0])).ClassifiedAs);
            var baseline = await db.Set<LuminanceBaseline>().SingleAsync(baseline => baseline.PoleId == poles[0]);
            Assert.Equal(100, baseline.Value); Assert.Equal(3, baseline.MemberCount);
        }
    }

    [Fact]
    public async Task Replacing_fixture_invalidates_old_baseline_until_three_new_members()
    {
        async Task<string> Install(int day)
        {
            await using var db = Db(); using var seed = db.EnterUnscopedSystemWriteBackdoor();
            var date = DateOnly.FromDateTime(DateTime.UnixEpoch.AddDays(20000 + day));
            var old = await db.Set<Fixture>().SingleOrDefaultAsync(f => f.PoleId == poles[0] && f.RemovedDate == null);
            if (old is not null) { old.RemovedDate = date; await db.SaveChangesAsync(); }
            var fixture = new Fixture { PoleId = poles[0], CommuneId = a, InstallDate = date,
                FixtureType = FixtureType.LedRoadLamp, PowerSource = PowerSource.Grid, LampWatt = 100, DataSource = DataSource.Simulated };
            db.Add(fixture); await db.SaveChangesAsync(); return fixture.FixtureId;
        }
        var first = await Install(0);
        foreach (int day in new[] { 1, 2, 3 }) await Accept(await Plant(day, 100));
        var replacement = await Install(4);
        for (int day = 4; day <= 6; day++)
        {
            var capture = await Plant(day, 200);
            await using (var db = Db())
                Assert.Null((await db.Set<PoleObservation>().SingleAsync(o => o.RunId == capture.Run && o.PoleId == poles[0])).BaselineId);
            await Accept(capture);
            await using var verify = Db();
            Assert.Equal(day == 6 ? 1 : 0, await verify.Set<LuminanceBaseline>().CountAsync(baseline => baseline.FixtureId == replacement));
        }
        var next = await Plant(7, 100);
        await using var check = Db();
        var result = await check.Set<PoleObservation>().SingleAsync(o => o.RunId == next.Run && o.PoleId == poles[0]);
        Assert.Equal(FixtureStatus.Dim, result.ClassifiedAs); Assert.Equal(200, result.BaselineValue);
        Assert.True(await check.Set<LuminanceBaseline>().AnyAsync(baseline => baseline.FixtureId == first));
    }

    public async Task DisposeAsync()
    {
        await using var db = Db(); await using var tx = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlRawAsync("SET LOCAL luxmap.audit_purge = 'on'");
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM audit_event WHERE commune_id = {a} OR commune_id = {b}");
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM fault WHERE pole_id = ANY({poles})");
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM pole_current_status WHERE pole_id = ANY({poles})");
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM luminance_history WHERE pole_id = ANY({poles})");
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM baseline_member WHERE baseline_id IN (SELECT baseline_id FROM luminance_baseline WHERE pole_id = ANY({poles}))");
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM pole_observation WHERE pole_id = ANY({poles})");
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM luminance_baseline WHERE pole_id = ANY({poles})");
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE survey_sweep SET status = 'awaiting_review', accepted_run_id = NULL, reviewed_at = NULL, reviewed_by = NULL, review_client_op_id = NULL, review_request_hash = NULL WHERE work_order_id = {order}");
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM survey_pass WHERE run_id IN (SELECT run_id FROM survey_processing_run WHERE sweep_id IN (SELECT sweep_id FROM survey_sweep WHERE work_order_id = {order}))");
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM survey_processing_run WHERE sweep_id IN (SELECT sweep_id FROM survey_sweep WHERE work_order_id = {order})");
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM survey_frame WHERE sweep_id IN (SELECT sweep_id FROM survey_sweep WHERE work_order_id = {order})");
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM survey_video_clip WHERE sweep_id IN (SELECT sweep_id FROM survey_sweep WHERE work_order_id = {order})");
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM survey_sweep WHERE work_order_id = {order}");
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM work_order_segment WHERE work_order_id = {order}");
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM work_order WHERE work_order_id = {order}");
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM fixture WHERE pole_id = ANY({poles})");
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM pole WHERE pole_id = ANY({poles})");
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM road_segment WHERE segment_id = {road}");
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM artifact_version WHERE version_id = {algorithm} OR version_id = {clock} OR version_id = {model} OR version_id = {extractor}");
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM app_user WHERE user_id = {user}");
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM administrative_unit WHERE commune_id = {a} OR commune_id = {b}");
        await tx.CommitAsync();
    }

    private sealed class ObjectSpy : IObjectStore
    {
        public int Reads { get; private set; }
        public Task<Stream> OpenAsync(StorageBucket bucket, string key, CancellationToken cancellationToken = default)
        { Reads++; return Task.FromResult<Stream>(new MemoryStream([255,216,255,217])); }
        public Task<bool> ExistsAsync(StorageBucket bucket, string key, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<StoredImage> StoreImageAsync(StorageBucket bucket, string id, Stream content, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<StoredObject> StoreStreamAsync(StorageBucket bucket, string key, Stream content, StreamUpload upload, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
