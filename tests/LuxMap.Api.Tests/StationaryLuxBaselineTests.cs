using System.Text.Json;
using LuxMap.Modules.Assets.Entities;
using LuxMap.Modules.Faults.Entities;
using LuxMap.Modules.Identity.Entities;
using LuxMap.Modules.Map.Features;
using LuxMap.Modules.Survey.Entities;
using LuxMap.Modules.Survey.LuxReadings;
using LuxMap.Modules.Survey.Review;
using LuxMap.Modules.WorkOrders;
using LuxMap.Modules.WorkOrders.Entities;
using LuxMap.Persistence;
using LuxMap.Persistence.Audit;
using LuxMap.Shared.Authorization;
using LuxMap.Shared.Contracts.Enums;
using LuxMap.Shared.Contracts.Errors;
using LuxMap.Shared.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NetTopologySuite.Geometries;
using Npgsql;

namespace LuxMap.Api.Tests;

/// <summary>
/// LUX-BASELINE — a protocol lux reading taken with a pole photo is judged, when the Manager decides the photo, against the pole's
/// own stationary baseline (median of earlier survey nights). Lux results are immutable, so this class keeps its own communes and
/// accounts and never deletes them (testing.md: survey data is not torn down).
/// </summary>
[Collection(nameof(AssetDatabaseCollection))]
public sealed class StationaryLuxBaselineTests(AssetImportFixture factory) : IAsyncLifetime
{
    private NpgsqlDataSource source = null!;
    private ModuleAssemblyCatalog catalog = null!;
    private string commune = null!, other = null!, road = null!, manager = null!, engineer = null!;

    private sealed class Scope(string[] ids) : ICommuneScopeAccessor { public CommuneScope ScopeValue => CommuneScope.ForCommunes(ids); CommuneScope ICommuneScopeAccessor.Scope => ScopeValue; }
    private sealed class Actor(string id, UserRole role) : ICurrentActorAccessor { public string? UserId => id; public UserRole? Role => role; }
    private sealed class Correlation : ICorrelationIdAccessor { public string CorrelationId => "stationary-lux-test"; }

    private LuxMapDbContext Db(string? actor = null, UserRole role = UserRole.Manager)
    {
        var builder = new DbContextOptionsBuilder<LuxMapDbContext>();
        PersistenceServiceCollectionExtensions.Configure(builder, source);
        return new(builder.Options, catalog, new Scope([commune, other]), new Actor(actor ?? manager, role));
    }

    public async Task InitializeAsync()
    {
        catalog = factory.Services.GetRequiredService<ModuleAssemblyCatalog>(); source = factory.Services.GetRequiredService<NpgsqlDataSource>();
        await using var db = Db(""); using var seed = db.EnterUnscopedSystemWriteBackdoor();
        var tag = Guid.NewGuid().ToString("N")[..8];
        var units = new[] { new AdministrativeUnit { Name = "stationary lux A " + tag }, new AdministrativeUnit { Name = "stationary lux B " + tag } };
        db.AddRange(units); await db.SaveChangesAsync(); commune = units[0].CommuneId; other = units[1].CommuneId;
        manager = await Person(db, UserRole.Manager); engineer = await Person(db, UserRole.FieldEngineer);
        var segment = new RoadSegment { CommuneId = commune, SegmentName = "Stationary lux", RoadClass = RoadClass.InterVillage,
            DataSource = DataSource.Simulated, LengthM = 100, Geom = new LineString([new(108, 16), new(108.001, 16)]) { SRID = 4326 } };
        db.Add(segment); await db.SaveChangesAsync(); road = segment.SegmentId;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static async Task<string> Person(LuxMapDbContext db, UserRole role)
    {
        var person = new AppUser { Username = "stationary" + Guid.NewGuid().ToString("N"), Email = Guid.NewGuid() + "@example.invalid",
            FullName = "Stationary lux", PasswordHash = "unused", PasswordAlgorithm = "pbkdf2-aspnetcore-v3", PasswordSetAt = DateTime.UtcNow, Role = role };
        db.Add(person); await db.SaveChangesAsync(); return person.UserId;
    }

    private async Task<string> Pole(DateOnly? installed = null, DataSource source = DataSource.Simulated)
    {
        await using var db = Db(); using var seed = db.EnterUnscopedSystemWriteBackdoor();
        var pole = new Pole { CommuneId = commune, SegmentId = road, DataSource = source, Geom = new Point(108, 16) { SRID = 4326 } };
        db.Add(pole); await db.SaveChangesAsync();
        db.Add(new Fixture { PoleId = pole.PoleId, CommuneId = commune, FixtureType = FixtureType.LedRoadLamp, PowerSource = PowerSource.Grid,
            LampWatt = 60, InstallDate = installed ?? new DateOnly(2025, 1, 1), DataSource = DataSource.Simulated });
        await db.SaveChangesAsync();
        return pole.PoleId;
    }

    private async Task<string> Order(TaskKind kind = TaskKind.Survey)
    {
        await using var db = Db(); using var seed = db.EnterUnscopedSystemWriteBackdoor();
        var wo = new WorkOrder { CommuneId = commune, CreatedBy = manager, AssignedTo = engineer, AssignedAt = DateTime.UtcNow, TaskKind = kind,
            WoStatus = WorkOrderStatus.InProgress, Title = "Stationary lux", StartedAt = DateTime.UtcNow,
            SegmentId = kind == TaskKind.Survey ? null : road };
        db.Add(wo); await db.SaveChangesAsync();
        if (kind == TaskKind.Survey)
        {
            db.Add(new WorkOrderSegment { WorkOrderId = wo.WorkOrderId, CommuneId = commune, SegmentId = road });
            await db.SaveChangesAsync();
        }
        return wo.WorkOrderId;
    }

    private async Task<string> Photo(string order, string pole, DateTime at, string state = "on")
    {
        await using var db = Db(); using var seed = db.EnterUnscopedSystemWriteBackdoor();
        // A photo carries its pole's data_source (WorkOrderPolePhotoService), so read it from the pole.
        var source = await db.Set<Pole>().Where(p => p.PoleId == pole).Select(p => p.DataSource).SingleAsync();
        var photo = new WorkOrderPolePhoto
        {
            WorkOrderId = order, PoleId = pole, CommuneId = commune, DataSource = source, UploadedBy = engineer,
            UploadedAt = at, CapturedAt = at, Lat = 16, Lng = 108, ClientOpId = Guid.NewGuid(), RequestHash = new('a', 64),
            ObjectKey = "stationary-photo", ThumbnailKey = "stationary-thumb", ByteCount = 4, ThumbnailBytes = 4, Width = 1, Height = 1,
            CvState = state, Confidence = .9, ModelVersion = "stationary-model", ArtifactVersion = "stationary-artifact", AnalyzedAt = at,
        };
        db.Add(photo); await db.SaveChangesAsync();
        return photo.PhotoId;
    }

    private static LuxProtocolRequest Protocol(double lux, double height = 1.0, string module = "M01", double? filter = null, double[]? samples = null)
        => new() { Samples = samples ?? [lux * .995, lux, lux * 1.005, lux, lux], SensorHeightM = height, ModuleId = module, OpticalFilterTransmittance = filter };

    private DataSource readingSource = DataSource.Simulated;

    private async Task<LuxReadingResponse> Lux(string pole, string? photo, DateTime at, double lux, LuxProtocolRequest? protocol)
    {
        await using var db = Db(engineer, UserRole.FieldEngineer);
        var service = new LuxReadingService(db, NullLogger<LuxReadingService>.Instance, Options.Create(new LuxProtocolOptions()));
        var (_, reading) = await service.CreateAsync(new CreateLuxReadingRequest
        {
            ClientOpId = Guid.NewGuid().ToString(), PoleId = pole, PhotoId = photo, MeasuredAt = at, LuxValue = lux,
            MeterModel = "ESP32C3_LUX", DataSource = readingSource, Protocol = protocol,
        }, engineer, default);
        return reading;
    }

    private async Task<PolePhotoItem> Decide(string order, string photo)
    {
        await using var db = Db();
        var audit = new AuditTrail(db, new Correlation());
        var actor = new Actor(manager, UserRole.Manager);
        var publisher = new ReviewedPolePhotoPublisher(db, audit, actor, Options.Create(new SurveyReviewOptions()),
            new StationaryLuxEvaluator(db, Options.Create(new LuxProtocolOptions()), Options.Create(new SurveyReviewOptions())));
        var service = new WorkOrderPolePhotoService(db, actor, null!, null!, null!, audit, null!, publisher, null!, null!, TimeProvider.System,
            new PhotoLuxLookup(db));
        return await service.DecideAsync(order, photo, new() { Decision = "confirm" }, default);
    }

    /// <summary>Photo + protocol reading + decision on survey night <paramref name="night"/> (21:00 Vietnam that evening).</summary>
    private async Task<PolePhotoItem> Night(string order, string pole, int night, double lux, string state = "on", string module = "M01",
        double? filter = null, int minutes = 0)
    {
        var at = new DateTime(2026, 10, 1, 14, 0, 0, DateTimeKind.Utc).AddDays(night).AddMinutes(minutes);
        var photo = await Photo(order, pole, at, state);
        await Lux(pole, photo, at, lux, Protocol(lux, module: module, filter: filter));
        return await Decide(order, photo);
    }

    private async Task<PoleMapDetail> Detail(string pole)
    {
        await using var db = Db();
        var service = new PoleDetailService(db, new SurveyMediaAccess(db, new Scope([commune, other])),
            new LuxMap.Modules.Assets.Warranty.WarrantyCalendar(new(), TimeProvider.System), Options.Create(new LuxProtocolOptions()));
        return await service.GetAsync(pole, default);
    }

    private async Task<FixtureStatus?> Status(string pole)
    {
        await using var db = Db();
        return await db.Set<PoleCurrentStatus>().Where(s => s.PoleId == pole).Select(s => (FixtureStatus?)s.FixtureStatus).SingleOrDefaultAsync();
    }

    private async Task<Fault[]> OpenDimFaults(string pole)
    {
        await using var db = Db();
        var open = FaultStatusSets.Open.ToArray();
        return await db.Set<Fault>().Where(f => f.PoleId == pole && f.FaultType == FaultType.LampDim && open.Contains(f.FaultStatus)).ToArrayAsync();
    }

    [Fact]
    public async Task Three_nights_build_the_baseline_the_fourth_is_judged_and_a_dim_lamp_raises_a_confirmed_fault()
    {
        var pole = await Pole(); var order = await Order();

        var first = await Night(order, pole, 1, 10.0);
        Assert.Equal("normal", first.LuxClassifiedAs);
        Assert.Equal([StationaryLuxEvaluator.BaselineMissing], first.LuxReasonCodes);
        Assert.Null(first.LuxBaselineRatio);
        await Night(order, pole, 2, 10.4);
        Assert.Null((await Detail(pole)).StationaryLuminanceBaseline);
        var third = await Night(order, pole, 3, 9.8);
        Assert.Equal([StationaryLuxEvaluator.BaselineMissing], third.LuxReasonCodes);

        var baseline = (await Detail(pole)).StationaryLuminanceBaseline;
        Assert.NotNull(baseline);
        Assert.Equal(10.0, baseline.Value, 6);
        Assert.Equal(3, baseline.MemberCount);
        Assert.True(baseline.Stable);

        var dim = await Night(order, pole, 4, 7.0);
        Assert.Equal("dim", dim.LuxClassifiedAs);
        Assert.Equal(.7, dim.LuxBaselineRatio!.Value, 6);
        Assert.Equal([StationaryLuxEvaluator.Evaluated], dim.LuxReasonCodes);
        Assert.Equal(FixtureStatus.Dim, await Status(pole));
        var fault = Assert.Single(await OpenDimFaults(pole));
        Assert.Equal(FaultStatus.Confirmed, fault.FaultStatus);
        Assert.Equal(SourceChannel.Cv, fault.SourceChannel);
        Assert.Equal(dim.PhotoId, fault.OriginPolePhotoId);
        // A dim reading never joins the baseline.
        Assert.Equal(3, (await Detail(pole)).StationaryLuminanceBaseline!.MemberCount);

        var back = await Night(order, pole, 5, 10.2);
        Assert.Equal("normal", back.LuxClassifiedAs);
        Assert.Equal(FixtureStatus.Normal, await Status(pole));
        Assert.Single(await OpenDimFaults(pole));
        var detail = await Detail(pole);
        Assert.Equal(4, detail.StationaryLuminanceBaseline!.MemberCount);
        Assert.Equal(10.1, detail.StationaryLuminanceBaseline.Value, 6);
        Assert.Equal(["normal", "normal", "normal", "dim", "normal"],
            detail.StationaryLuminanceHistory.Select(p => ContractEnumName(p.ClassifiedAs)).ToArray());
    }

    [Fact]
    public async Task Field_readings_on_a_pole_inventoried_from_public_imagery_still_build_its_baseline()
    {
        // Found end-to-end on a copy of luxmap_dev: most poles were inventoried from photos (public_imagery) and are measured in the field.
        var pole = await Pole(source: DataSource.PublicImagery); var order = await Order();
        readingSource = DataSource.Field;

        for (var night = 1; night <= 3; night++) await Night(order, pole, night, 10.0);
        var dim = await Night(order, pole, 4, 7.0);

        Assert.Equal(3, (await Detail(pole)).StationaryLuminanceBaseline!.MemberCount);
        Assert.Equal("dim", dim.LuxClassifiedAs);
        Assert.Equal(DataSource.PublicImagery, Assert.Single(await OpenDimFaults(pole)).DataSource);
    }

    [Fact]
    public async Task Two_readings_either_side_of_midnight_are_one_survey_night()
    {
        var pole = await Pole(); var order = await Order();

        await Night(order, pole, 1, 10.0, minutes: 170);  // 23:50 Vietnam
        await Night(order, pole, 1, 10.0, minutes: 200);  // 00:20 Vietnam, the same night
        await Night(order, pole, 2, 10.0);
        Assert.Null((await Detail(pole)).StationaryLuminanceBaseline);

        await Night(order, pole, 3, 10.0);
        Assert.Equal(3, (await Detail(pole)).StationaryLuminanceBaseline!.MemberCount);
    }

    [Fact]
    public async Task A_reading_is_never_judged_against_a_baseline_holding_its_own_night()
    {
        var pole = await Pole(); var order = await Order();
        for (var night = 1; night <= 3; night++) await Night(order, pole, night, 10.0);

        // A second reading of night 3, decided after the baseline that includes night 3: still nothing earlier to judge it by.
        var again = await Night(order, pole, 3, 6.0, minutes: 30);

        Assert.Equal([StationaryLuxEvaluator.BaselineMissing], again.LuxReasonCodes);
        Assert.Empty(await OpenDimFaults(pole));
    }

    [Fact]
    public async Task A_reference_reading_without_a_protocol_is_stored_but_never_judged()
    {
        var pole = await Pole(); var order = await Order();
        var at = new DateTime(2026, 10, 2, 14, 0, 0, DateTimeKind.Utc);
        var photo = await Photo(order, pole, at);
        var reading = await Lux(pole, photo, at, 3.0, protocol: null);
        Assert.Null(reading.Protocol);

        var item = await Decide(order, photo);

        Assert.Equal(3.0, item.LuxValue);
        Assert.Null(item.LuxClassifiedAs);
        await using var db = Db();
        Assert.False(await db.Set<LuxEvaluation>().AnyAsync(e => e.PoleId == pole));
    }

    [Fact]
    public async Task A_reading_on_an_inspection_order_is_not_judged()
    {
        var pole = await Pole(); var order = await Order(TaskKind.Inspection);

        var item = await Night(order, pole, 1, 10.0);

        Assert.Null(item.LuxClassifiedAs);
    }

    [Theory]
    [InlineData("height")]
    [InlineData("spread")]
    [InlineData("few")]
    [InlineData("median")]
    public async Task A_reading_off_the_protocol_is_refused_with_its_reason(string fault)
    {
        var pole = await Pole();
        var at = new DateTime(2026, 10, 2, 14, 0, 0, DateTimeKind.Utc);
        var (protocol, lux, code) = fault switch
        {
            "height" => (Protocol(10, height: 1.2), 10.0, ErrorCodes.LuxProtocolMismatch),
            "spread" => (Protocol(10, samples: [10, 10, 10, 10, 12]), 10.0, ErrorCodes.LuxWindowUnstable),
            "few" => (Protocol(10, samples: [10, 10]), 10.0, ErrorCodes.LuxWindowUnstable),
            _ => (Protocol(10), 11.0, ErrorCodes.LuxSamplesMismatch),
        };

        var error = await Assert.ThrowsAsync<LuxMapException>(() => Lux(pole, null, at, lux, protocol));

        Assert.Equal(code, error.Code);
        await using var db = Db();
        Assert.False(await db.Set<LuxReading>().AnyAsync(r => r.PoleId == pole));
    }

    [Fact]
    public async Task A_reading_cannot_be_linked_to_a_photo_already_decided()
    {
        var pole = await Pole(); var order = await Order();
        var at = new DateTime(2026, 10, 2, 14, 0, 0, DateTimeKind.Utc);
        var photo = await Photo(order, pole, at);
        await Decide(order, photo);

        var error = await Assert.ThrowsAsync<LuxMapException>(() => Lux(pole, photo, at, 10, Protocol(10)));

        Assert.Equal(ErrorCodes.PolePhotoAlreadyDecided, error.Code);
    }

    [Fact]
    public async Task A_filter_test_is_judged_with_its_expected_ratio_but_moves_nothing()
    {
        var pole = await Pole(); var order = await Order();
        for (var night = 1; night <= 3; night++) await Night(order, pole, night, 10.0);
        var before = await Status(pole);

        var test = await Night(order, pole, 4, 7.0, filter: .7);

        Assert.Equal("dim", test.LuxClassifiedAs);
        Assert.Contains(StationaryLuxEvaluator.OpticalFilter, test.LuxReasonCodes!);
        Assert.Equal(before, await Status(pole));
        Assert.Empty(await OpenDimFaults(pole));
        Assert.Equal(3, (await Detail(pole)).StationaryLuminanceBaseline!.MemberCount);
        Assert.Equal(.7, (await Detail(pole)).StationaryLuminanceHistory[^1].ExpectedRatio);
    }

    [Fact]
    public async Task An_unstable_baseline_does_not_judge_dim()
    {
        var pole = await Pole(); var order = await Order();
        await Night(order, pole, 1, 6.0); await Night(order, pole, 2, 10.0); await Night(order, pole, 3, 14.0);
        Assert.False((await Detail(pole)).StationaryLuminanceBaseline!.Stable);

        var item = await Night(order, pole, 4, 5.0);

        Assert.Equal("normal", item.LuxClassifiedAs);
        Assert.Equal([StationaryLuxEvaluator.BaselineUnstable], item.LuxReasonCodes);
        Assert.Empty(await OpenDimFaults(pole));
    }

    [Fact]
    public async Task Another_module_starts_its_own_baseline()
    {
        var pole = await Pole(); var order = await Order();
        for (var night = 1; night <= 3; night++) await Night(order, pole, night, 10.0);

        var item = await Night(order, pole, 4, 5.0, module: "M02");

        Assert.Equal([StationaryLuxEvaluator.BaselineMissing], item.LuxReasonCodes);
        Assert.Empty(await OpenDimFaults(pole));
    }

    [Fact]
    public async Task Survey_findings_carry_a_judged_dim_so_a_follow_up_inspection_takes_it()
    {
        var pole = await Pole(); var order = await Order();
        for (var night = 1; night <= 3; night++) await Night(order, pole, night, 10.0);
        await Night(order, pole, 4, 7.0);

        await using var db = Db();
        var findings = await new SurveyFindingLookup(db).FindingsAsync(order, commune, default);

        Assert.Equal(FaultType.LampDim, Assert.Single(findings, f => f.PoleId == pole).FaultType);
    }

    [Fact]
    public async Task Stationary_results_cannot_be_changed_or_deleted()
    {
        var pole = await Pole(); var order = await Order();
        await Night(order, pole, 1, 10.0);

        await using var db = Db();
        string[] deletes =
        [
            "DELETE FROM lux_evaluation WHERE lux_id IN (SELECT lux_id FROM lux_reading WHERE pole_id = {0})",
            "DELETE FROM lux_reading_sample WHERE lux_id IN (SELECT lux_id FROM lux_reading WHERE pole_id = {0})",
        ];
        foreach (var sql in deletes)
        {
            var error = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(sql, pole));
            Assert.Contains("immutable", error.MessageText, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static string ContractEnumName(FixtureStatus status) => JsonSerializer.Serialize(status, LuxMap.Shared.Serialization.LuxMapJsonOptions.Default).Trim('"');
}
