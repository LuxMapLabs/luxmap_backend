using System.Net;
using System.Text.Json;
using LuxMap.Modules.Assets.Entities;
using LuxMap.Modules.Survey.Entities;
using LuxMap.Modules.Survey.Ingest;
using LuxMap.Modules.Survey.Review;
using LuxMap.Modules.WorkOrders.Entities;
using LuxMap.Persistence;
using LuxMap.Persistence.Audit;
using LuxMap.Shared.Authorization;
using LuxMap.Shared.Contracts.Enums;
using LuxMap.Shared.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NetTopologySuite.Geometries;
using Npgsql;

namespace LuxMap.Api.Tests;

/// <summary>BE-17 — what a run drove on each segment (D-1…D-4) and the latest attempt on the sweep detail (D-5).</summary>
[Collection(nameof(AssetDatabaseCollection))]
public sealed class SweepCoverageTests(AssetImportFixture factory) : IAsyncLifetime
{
    private NpgsqlDataSource source = null!;
    private ModuleAssemblyCatalog catalog = null!;
    private string a = null!, b = null!, road = null!, bare = null!, bent = null!, order = null!, user = null!;
    private string[] poles = [];
    private long algorithm, clock;

    private sealed class Scope(string[] ids) : ICommuneScopeAccessor { CommuneScope ICommuneScopeAccessor.Scope => CommuneScope.ForCommunes(ids); }
    private sealed class Actor(string id) : ICurrentActorAccessor { public string? UserId => id; public UserRole? Role => UserRole.Manager; }
    private sealed class Correlation : ICorrelationIdAccessor { public string CorrelationId => "sweep-coverage-test"; }

    private LuxMapDbContext Db(string[]? ids = null)
    {
        var builder = new DbContextOptionsBuilder<LuxMapDbContext>();
        PersistenceServiceCollectionExtensions.Configure(builder, source);
        return new(builder.Options, catalog, new Scope(ids ?? [a, b]), new Actor(user));
    }

    private SurveyReviewService Review(LuxMapDbContext db, string[]? ids = null) => new(db, new Actor(user), new Scope(ids ?? [a, b]),
        new AuditTrail(db, new Correlation()), new PhotoStore(), Options.Create(new SurveyReviewOptions()));

    public async Task InitializeAsync()
    {
        catalog = factory.Services.GetRequiredService<ModuleAssemblyCatalog>();
        source = factory.Services.GetRequiredService<NpgsqlDataSource>();
        await using var db = Db([]);
        using var seed = db.EnterUnscopedSystemWriteBackdoor();
        var communes = new[] { new AdministrativeUnit { Name = "coverage A " + Guid.NewGuid().ToString("N") }, new AdministrativeUnit { Name = "coverage B " + Guid.NewGuid().ToString("N") } };
        db.AddRange(communes);
        await db.SaveChangesAsync();
        (a, b) = (communes[0].CommuneId, communes[1].CommuneId);
        // The fixture's manager, not a new account per test: InitializeAsync runs for EVERY test, and accounts pile up (BE-27 note).
        user = await db.Database.SqlQuery<string>($"SELECT user_id AS \"Value\" FROM app_user WHERE username = {factory.ManagerUsername}").SingleAsync();

        // Declared 100 m; the line itself is ~107 m — the metres reported are the DECLARED ones (D-3).
        var segments = new[]
        {
            new RoadSegment { CommuneId = a, SegmentName = "Coverage driven", RoadClass = RoadClass.InterVillage, DataSource = DataSource.Simulated,
                LengthM = 100, Geom = new LineString([new(108, 16), new(108.001, 16)]) { SRID = 4326 } },
            new RoadSegment { CommuneId = a, SegmentName = "Coverage bare", RoadClass = RoadClass.InterVillage, DataSource = DataSource.Simulated,
                LengthM = 50, Geom = new LineString([new(108, 16.001), new(108.0005, 16.001)]) { SRID = 4326 } },
            // An L: 0.001° north (~110.6 m), then 0.001° east (~107 m). In degrees the legs are equal, so half way is the corner; in
            // metres (3405, where pass fractions are measured) half way is still on the north leg, below the corner.
            new RoadSegment { CommuneId = a, SegmentName = "Coverage bent", RoadClass = RoadClass.InterVillage, DataSource = DataSource.Simulated,
                LengthM = 218, Geom = new LineString([new(107.9, 16.0), new(107.9, 16.001), new(107.901, 16.001)]) { SRID = 4326 } },
        };
        db.AddRange(segments);
        await db.SaveChangesAsync();
        (road, bare, bent) = (segments[0].SegmentId, segments[1].SegmentId, segments[2].SegmentId);

        var assets = new[]
        {
            new Pole { CommuneId = a, SegmentId = road, DataSource = DataSource.Simulated, Geom = new Point(108.0002, 16) { SRID = 4326 } },
            new Pole { CommuneId = b, SegmentId = road, DataSource = DataSource.Simulated, Geom = new Point(108.0009, 16) { SRID = 4326 } },
        };
        db.AddRange(assets);
        await db.SaveChangesAsync();
        poles = [.. assets.Select(p => p.PoleId)];

        var wo = new WorkOrder
        {
            CommuneId = a, CreatedBy = user, AssignedTo = user, AssignedAt = DateTime.UtcNow, TaskKind = TaskKind.Survey,
            WoStatus = WorkOrderStatus.InProgress, Title = "Coverage", StartedAt = DateTime.UtcNow,
        };
        db.Add(wo);
        await db.SaveChangesAsync();
        order = wo.WorkOrderId;
        db.AddRange(new WorkOrderSegment { WorkOrderId = order, CommuneId = a, SegmentId = road, Position = 0 },
            new WorkOrderSegment { WorkOrderId = order, CommuneId = a, SegmentId = bare, Position = 1 },
            new WorkOrderSegment { WorkOrderId = order, CommuneId = a, SegmentId = bent, Position = 2 });
        var versions = new[] { "association_algorithm", "clock_algorithm" }.Select(c => new ArtifactVersion
            { Component = c, Version = Guid.NewGuid().ToString("N"), ArtifactHash = new('a', 64), CreatedAt = DateTime.UtcNow }).ToArray();
        db.AddRange(versions);
        await db.SaveChangesAsync();
        (algorithm, clock) = (versions[0].VersionId, versions[1].VersionId);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // ---- pure interval arithmetic ----------------------------------------------------------------------

    [Fact]
    public void Passes_are_merged_regardless_of_direction_and_clamped()
    {
        // A reverse pass counts the same as a forward one; 1.2 is clamped to the end; 0.9..0.95 stays open.
        var union = SweepCoverage.Union([(0.0, 0.4), (0.9, 0.6), (0.35, 0.5), (1.2, 0.95)]);

        Assert.Equal([(0.0, 0.5), (0.6, 0.9), (0.95, 1.0)], union);
    }

    [Fact]
    public void Gaps_are_the_complement_without_slivers_shorter_than_a_metre()
    {
        // 0.5..0.505 on 100 m is 0.5 m: noise between two passes that met, not a stretch nobody drove.
        var gaps = SweepCoverage.Gaps([(0.1, 0.5), (0.505, 0.8)], lengthM: 100);

        Assert.Equal([(0.0, 0.1), (0.8, 1.0)], gaps);
        Assert.Equal(0.7, SweepCoverage.CoveredRatio(gaps), 6);
        Assert.Equal([(0.0, 1.0)], SweepCoverage.Gaps([], lengthM: 100));
    }

    [Fact]
    public void A_gap_of_exactly_one_metre_is_kept_only_shorter_ones_are_dropped()
    {
        Assert.Equal([(0.0, 0.25)], SweepCoverage.Gaps([(0.25, 1.0)], lengthM: 4));   // 1 m at the start
        Assert.Equal([(0.75, 1.0)], SweepCoverage.Gaps([(0.0, 0.75)], lengthM: 4));   // 1 m at the end
        Assert.Equal([(0.4, 0.5)], SweepCoverage.Gaps([(0.0, 0.4), (0.5, 1.0)], lengthM: 10)); // 1 m in the middle
        Assert.Empty(SweepCoverage.Gaps([(0.0, 0.4), (0.49, 1.0)], lengthM: 10));      // 0.9 m
    }

    // ---- GET /sweeps/{id}/coverage ---------------------------------------------------------------------

    [Fact]
    public async Task Coverage_lists_passes_in_declared_metres_the_stretches_not_driven_and_the_poles_not_observed()
    {
        var sweep = await PlantAsync(passes: [("forward", 0.0, 0.4), ("reverse", 0.9, 0.6)], observed: [0]);
        await using var db = Db();

        var coverage = await Review(db).Coverage(sweep.Id, null, default);

        Assert.Equal(sweep.Run, coverage.RunId);
        Assert.Equal([road, bare, bent], coverage.Segments.Select(s => s.SegmentId));
        var driven = coverage.Segments[0];
        Assert.Equal(100, driven.LengthM);
        Assert.Equal([(0, "forward", 0.0, 40.0), (1, "reverse", 90.0, 60.0)],
            driven.Passes.Select(p => (p.PassNo, p.Direction, p.FromM!.Value, p.ToM!.Value)));
        Assert.Equal([(40.0, 60.0), (90.0, 100.0)], driven.Gaps.Select(g => (g.FromM!.Value, g.ToM!.Value)));
        Assert.Equal(0.7, driven.CoveredRatio, 6);
        Assert.Equal((2, 1), (driven.PolesExpected, driven.PolesObserved));
        Assert.Equal([poles[1]], driven.NotObservedPoleIds);

        // The gap is cut on the road itself, in 4326 [lng, lat]: 40..60 % of the line lies between lng 108.0004 and 108.0006.
        var line = Assert.IsType<double[][]>(driven.Gaps[0].Geometry!.Coordinates);
        Assert.Equal("LineString", driven.Gaps[0].Geometry!.Type);
        Assert.All(line, point => Assert.InRange(point[0], 108.00039, 108.00061));
        Assert.All(line, point => Assert.Equal(16, point[1], 6));

        var untouched = coverage.Segments[1];
        Assert.Empty(untouched.Passes);
        Assert.Equal([(0.0, 50.0)], untouched.Gaps.Select(g => (g.FromM!.Value, g.ToM!.Value)));
        Assert.Equal(0, untouched.CoveredRatio);
    }

    /// <summary>
    /// Pass fractions are measured in 3405; the gap must be cut there and only then turned into 4326. Cut in 4326, half way along the L
    /// is its corner; cut in 3405, it is ~1.7 m below the corner on the north leg.
    /// </summary>
    [Fact]
    public async Task A_gap_is_cut_on_the_projection_the_pass_was_measured_on()
    {
        var sweep = await PlantAsync(passes: [("forward", 0.0, 0.5)], observed: [], segment: bent);
        await using var db = Db();

        var gap = (await Review(db).Coverage(sweep.Id, null, default)).Segments.Single(s => s.SegmentId == bent).Gaps.Single();

        var line = Assert.IsType<double[][]>(gap.Geometry!.Coordinates);
        Assert.Equal(107.9, line[0][0], 7);                     // [lng, lat]: still on the north leg…
        Assert.InRange(line[0][1], 16.00095, 16.000995);       // …below the corner at 16.001
        Assert.Equal([107.901, 16.001], line[^1].Select(c => Math.Round(c, 7)));
        Assert.Contains(line, p => Math.Abs(p[0] - 107.9) < 1e-7 && Math.Abs(p[1] - 16.001) < 1e-7); // the corner is in the gap
        Assert.Equal((109.0, 218.0), (gap.FromM!.Value, gap.ToM!.Value));
    }

    [Fact]
    public async Task Coverage_reads_the_latest_succeeded_run_or_the_one_asked_for()
    {
        var sweep = await PlantAsync(passes: [("forward", 0.0, 0.4)], observed: []);
        var second = await AddRunAsync(sweep.Id, attempt: 2, state: "succeeded", stage: "complete", error: null, passes: [("forward", 0.0, 1.0)]);
        var failed = await AddRunAsync(sweep.Id, attempt: 3, state: "failed", stage: "clock", error: "CLOCK_RESIDUAL");
        var other = await PlantAsync(passes: [("forward", 0.0, 1.0)], observed: []);
        await using var db = Db();
        var service = Review(db);

        var latest = await service.Coverage(sweep.Id, null, default);
        var asked = await service.Coverage(sweep.Id, sweep.Run, default);

        Assert.Equal(second, latest.RunId);
        Assert.Equal(1.0, latest.Segments[0].CoveredRatio, 6);
        Assert.Equal(sweep.Run, asked.RunId);
        Assert.Equal(0.4, asked.Segments[0].CoveredRatio, 6);
        foreach (var run in new[] { failed, other.Run })
        {
            var error = await Assert.ThrowsAsync<LuxMapException>(() => service.Coverage(sweep.Id, run, default));
            Assert.Equal("RUN_NOT_FOUND", error.Code);
        }
    }

    [Fact]
    public async Task Coverage_needs_every_commune_of_the_run_and_of_the_job()
    {
        var sweep = await PlantAsync(passes: [("forward", 0.0, 1.0)], observed: [0, 1]);
        await using (var onlyA = Db([a]))
        {
            var missingPoleCommune = await Assert.ThrowsAsync<LuxMapException>(() => Review(onlyA, [a]).Coverage(sweep.Id, null, default));
            Assert.Equal(HttpStatusCode.NotFound, missingPoleCommune.StatusCode);
        }

        // The job touches a third commune through its segments: holding the poles' communes is not enough.
        var wider = await PlantAsync(passes: [("forward", 0.0, 1.0)], observed: [0, 1], extraCommune: factory.ForeignCommuneId);
        await using var db = Db();
        var missingJobCommune = await Assert.ThrowsAsync<LuxMapException>(() => Review(db).Coverage(wider.Id, null, default));
        Assert.Equal(HttpStatusCode.NotFound, missingJobCommune.StatusCode);
    }

    [Fact]
    public async Task Coverage_without_a_succeeded_run_is_not_found()
    {
        var sweep = await PlantAsync(passes: [], observed: [], state: "failed");
        await using var db = Db();

        var error = await Assert.ThrowsAsync<LuxMapException>(() => Review(db).Coverage(sweep.Id, null, default));

        Assert.Equal("RUN_NOT_FOUND", error.Code);
    }

    // ---- latest_run on GET /sweeps/{id} -----------------------------------------------------------------

    [Fact]
    public async Task The_detail_says_why_the_latest_attempt_failed_and_is_null_before_any_attempt()
    {
        var sweep = await PlantAsync(passes: [("forward", 0.0, 1.0)], observed: [0, 1]);
        await AddRunAsync(sweep.Id, attempt: 2, state: "failed", stage: "clock", error: "CLOCK_RESIDUAL");
        var none = await PlantAsync(passes: [], observed: [], withRun: false);
        await using var db = Db();
        var ingest = new SurveyIngestService(db, new Actor(user), new AuditTrail(db, new Correlation()), new PhotoStore());

        var failed = (await ingest.Detail(sweep.Id, default)).LatestRun!;
        var empty = await ingest.Detail(none.Id, default);

        Assert.Equal((2, "failed", "clock", "CLOCK_RESIDUAL"), (failed.Attempt, failed.ResultState, failed.Stage, failed.ErrorCode));
        Assert.Null(empty.LatestRun);
    }

    [Fact]
    public async Task The_list_does_not_carry_a_run()
    {
        await PlantAsync(passes: [("forward", 0.0, 1.0)], observed: [0, 1]);
        await using var db = Db();
        var ingest = new SurveyIngestService(db, new Actor(user), new AuditTrail(db, new Correlation()), new PhotoStore());

        var page = await ingest.List(order, null, null, null, LuxMap.Shared.Contracts.Paging.PageRequest.Create(), default);

        Assert.All(page.Items, item => Assert.IsNotType<SweepDetailResponse>(item));
        var json = JsonSerializer.Serialize<object>(page.Items[0], LuxMap.Shared.Serialization.LuxMapJsonOptions.Default);
        Assert.DoesNotContain("latest_run", json, StringComparison.Ordinal);
    }

    // ---- helpers ----------------------------------------------------------------------------------------

    private sealed record Planted(string Id, long Run);

    private async Task<Planted> PlantAsync(
        (string Direction, double From, double To)[] passes, int[] observed, string state = "succeeded", string? extraCommune = null,
        bool withRun = true, string? segment = null)
    {
        await using var db = Db();
        using var seed = db.EnterUnscopedSystemWriteBackdoor();
        var at = DateTime.UtcNow;
        var sweep = new SurveySweep
        {
            WorkOrderId = order, CommuneId = a, CapturedBy = user, ClientOpId = Guid.NewGuid(), BootSessionId = Guid.NewGuid(),
            CreateRequestHash = new('a', 64), UtcAnchor = at, EndedElapsedNs = 10_000_000_000L, DataSource = DataSource.Simulated,
            Status = SweepStatus.AwaitingReview, ProcessingStatus = SweepProcessingStatus.Succeeded,
        };
        db.Add(sweep);
        await db.SaveChangesAsync();
        if (!withRun) return new(sweep.SweepId, 0);

        var run = NewRun(sweep.SweepId, 1, state, state == "succeeded" ? "complete" : "clock", state == "succeeded" ? null : "CLOCK_RESIDUAL",
            extraCommune is null ? [a, b] : [a, b, extraCommune]);
        db.Add(run);
        await db.SaveChangesAsync();
        var made = Passes(run, passes, segment ?? road);
        db.AddRange(made);
        await db.SaveChangesAsync();
        foreach (var i in observed)
        {
            db.Add(new PoleObservation
            {
                Run = run, Pass = made[0], PoleId = poles[i], CommuneId = i == 0 ? a : b, DataSource = DataSource.Simulated,
                // "unknown" needs no frame (ck_pole_observation_classification); for coverage only the row's presence matters.
                ObservedAt = at, ObservedElapsedNs = 500_000_000L, AssociationConfidence = .9,
                ClassifiedAs = FixtureStatus.Unknown, DimEvaluationEligible = false,
                QualityFlags = "[]", ReasonCodes = "[]",
            });
        }

        await db.SaveChangesAsync();
        return new(sweep.SweepId, run.RunId);
    }

    private async Task<long> AddRunAsync(
        string sweepId, int attempt, string state, string stage, string? error, (string Direction, double From, double To)[]? passes = null)
    {
        await using var db = Db();
        using var seed = db.EnterUnscopedSystemWriteBackdoor();
        var run = NewRun(sweepId, attempt, state, stage, error, [a, b]);
        db.Add(run);
        db.AddRange(Passes(run, passes ?? [], road));
        await db.SaveChangesAsync();
        return run.RunId;
    }

    private static SurveyPass[] Passes(SurveyProcessingRun run, (string Direction, double From, double To)[] passes, string segment)
        => [.. passes.Select((p, i) => new SurveyPass
        {
            Run = run, SegmentId = segment, PassNo = i, Direction = p.Direction, FromFraction = p.From, ToFraction = p.To,
            StartElapsedNs = i * 1_000_000_000L, EndElapsedNs = (i + 1) * 1_000_000_000L, QualityFlags = "{}",
        })];

    private SurveyProcessingRun NewRun(string sweepId, int attempt, string state, string stage, string? error, string[] communes)
    {
        var at = DateTime.UtcNow;
        return new SurveyProcessingRun
        {
            SweepId = sweepId, CommuneId = a, Attempt = attempt, LeaseOwner = Guid.NewGuid(), LeaseExpiresAt = at.AddMinutes(1),
            InputHash = new('b', 64), SettingsSnapshot = "{}", AlgorithmVersionId = algorithm, ClockVersionId = clock,
            ResultState = state, Stage = stage, ErrorCode = error, StartedAt = at, FinishedAt = at.AddSeconds(10),
            CoveragePct = state == "succeeded" ? 50 : null,
            GisSnapshot = JsonSerializer.Serialize(new
            {
                segments = new[] { road, bare, bent }, communes,
                poles = poles.Select((id, i) => new { segment_id = road, pole_id = id, commune_id = i == 0 ? a : b }),
            }),
        };
    }
}
