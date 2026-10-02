using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using LuxMap.Infrastructure.Storage;
using LuxMap.Modules.Assets.Entities;
using LuxMap.Modules.Identity.Entities;
using LuxMap.Modules.Survey.Entities;
using LuxMap.Persistence;
using LuxMap.Persistence.Audit;
using LuxMap.Shared.Contracts.Enums;
using LuxMap.Shared.Storage;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using NetTopologySuite.Geometries;

namespace LuxMap.Api.Tests;

/// <summary>Requires migrated PostGIS. P2a author only compiles this suite; Claude runs it.</summary>
[Collection(nameof(AssetDatabaseCollection))]
public sealed class SurveyIngestTests(AssetImportFixture factory) : IAsyncLifetime
{
    private readonly Dictionary<string, AppUser> users = [];
    private readonly Dictionary<string, HttpClient> clients = [];
    private readonly Guid boot = Guid.NewGuid();
    private readonly FakeS3 storage = new();
    private WebApplicationFactory<Program> host = null!;
    private string commune = null!;
    private string foreign = null!;
    private string[] roads = [];
    private string wo = null!;
    private static readonly DateTime Anchor = new(2026, 10, 2, 12, 0, 0, DateTimeKind.Utc);
    private Task<T> Db<T>(Func<LuxMapDbContext, Task<T>> work) => factory.QueryAsync(work);

    public async Task InitializeAsync()
    {
        await Db(async db =>
        {
            using var system = db.EnterUnscopedSystemWriteBackdoor();
            var units = new[] { new AdministrativeUnit { Name = "Survey " + Guid.NewGuid() }, new AdministrativeUnit { Name = "Survey other " + Guid.NewGuid() } };
            db.AddRange(units); await db.SaveChangesAsync(); commune = units[0].CommuneId; foreign = units[1].CommuneId;
            foreach (var name in new[] { "manager", "owner", "other", "outside" })
            {
                var u = new AppUser { Username = "survey" + Guid.NewGuid().ToString("N"), Email = Guid.NewGuid() + "@example.invalid",
                    FullName = name, PasswordHash = "", PasswordAlgorithm = "pbkdf2-aspnetcore-v3", Role = name == "manager" ? UserRole.Manager : UserRole.FieldEngineer };
                u.PasswordHash = new PasswordHasher<AppUser>().HashPassword(u, factory.AccountPassword);
                users.Add(name, u); db.Add(u);
            }
            await db.SaveChangesAsync();
            foreach (var (name, u) in users) db.Add(new AppUserCommune { UserId = u.UserId, CommuneId = name == "outside" ? foreign : commune });
            var targets = Enumerable.Range(0, 2).Select(i => new RoadSegment { CommuneId = commune, SegmentName = "Survey route " + i,
                RoadClass = RoadClass.InterVillage, DataSource = DataSource.Simulated, LengthM = 100,
                Geom = new LineString([new Coordinate(108, 16), new Coordinate(108.001, 16.001)]) { SRID = 4326 } }).ToArray();
            db.AddRange(targets); await db.SaveChangesAsync(); roads = targets.Select(x => x.SegmentId).Reverse().ToArray(); return 0;
        });
        host = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.Replace(ServiceDescriptor.Singleton<IObjectStore>(new S3ObjectStore(storage, NullLogger<S3ObjectStore>.Instance)))));
        foreach (var (name, u) in users)
        {
            var c = host.CreateClient();
            var token = await (await c.PostLoginAsync(u.Username, factory.AccountPassword)).ReadTokensAsync();
            c.DefaultRequestHeaders.Authorization = new("Bearer", token.AccessToken); clients.Add(name, c);
        }
        var created = await Json(await clients["manager"].PostAsJsonAsync("/api/v1/work-orders", new { task_kind = "survey", title = "Night survey",
            commune_id = commune, segment_ids = roads, assigned_to = users["owner"].UserId }), 201);
        wo = created.GetProperty("work_order_id").GetString()!;
        Assert.Equal(roads, created.GetProperty("segment_ids").EnumerateArray().Select(x => x.GetString()));
        Assert.Equal(JsonValueKind.Null, created.GetProperty("segment_id").ValueKind);
        Assert.Empty(created.GetProperty("fault_ids").EnumerateArray());
        await Json(await clients["owner"].PostAsync($"/api/v1/work-orders/{wo}/start", null), 200);
    }

    public async Task DisposeAsync()
    {
        await Db(async db =>
        {
            await using var tx = await db.Database.BeginTransactionAsync();
            await db.Database.ExecuteSqlRawAsync("SET LOCAL luxmap.audit_purge = 'on'");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM audit_event WHERE commune_id = {commune} OR commune_id = {foreign}");
            foreach (var table in new[] { "survey_gps_sample", "survey_lux_sample", "survey_raw_file", "survey_video_clip" })
            {
                var sql = $"DELETE FROM {table} WHERE sweep_id IN (SELECT sweep_id FROM survey_sweep WHERE commune_id = {{0}})";
                await db.Database.ExecuteSqlRawAsync(sql, commune);
            }
            foreach (var table in new[] { "survey_sweep", "work_order_segment", "work_order", "road_segment" })
            {
                var sql = $"DELETE FROM {table} WHERE commune_id = {{0}}";
                await db.Database.ExecuteSqlRawAsync(sql, commune);
            }
            var ids = users.Values.Select(x => x.UserId).ToArray();
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM app_user WHERE user_id = ANY({ids})");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM administrative_unit WHERE commune_id = {commune} OR commune_id = {foreign}");
            await tx.CommitAsync(); return 0;
        });
        foreach (var c in clients.Values) c.Dispose();
        await host.DisposeAsync(); storage.Dispose();
    }

    private object Body(Guid op, double uncertainty = 2) => new { work_order_id = wo, client_op_id = op, boot_session_id = boot,
        elapsed_anchor_ns = "9007199254740000", started_elapsed_ns = "9007199254740000", utc_anchor = Anchor, utc_uncertainty_ms = uncertainty, data_source = "simulated" };
    private async Task<string> Create() => (await Json(await clients["owner"].PostAsJsonAsync("/api/v1/sweeps", Body(Guid.NewGuid())), 201)).GetProperty("sweep_id").GetString()!;
    private static async Task<JsonElement> Json(HttpResponseMessage response, int status)
    {
        using (response)
        {
            var body = await response.Content.ReadAsStringAsync();
            Assert.True((int)response.StatusCode == status, $"Expected {status}, got {(int)response.StatusCode}: {body}");
            return JsonDocument.Parse(body).RootElement.Clone();
        }
    }
    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    private async Task<JsonElement> Clip(string id, byte[] bytes, int status, string? hash = null, long? length = null, int no = 0)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/sweeps/{id}/clips/{no}") { Content = new ByteArrayContent(bytes) };
        request.Headers.Add("X-Content-SHA256", hash ?? Hash(bytes));
        request.Content.Headers.ContentType = new("video/mp4");
        if (length is not null) request.Content.Headers.ContentLength = length;
        return await Json(await clients["owner"].SendAsync(request), status);
    }
    private Task<JsonElement> Raw(string id, string kind, string text, int status = 200)
        => SendRaw(id, kind, Encoding.UTF8.GetBytes(text), status);
    private async Task<JsonElement> SendRaw(string id, string kind, byte[] bytes, int status)
        => await Json(await clients["owner"].PutAsync($"/api/v1/sweeps/{id}/raw/{kind}", new ByteArrayContent(bytes)), status);
    private static byte[] Video() => [0, 0, 0, 24, 102, 116, 121, 112, 105, 115, 111, 109];
    private string Gps(string suffix = "") => $$"""
        {"kind":"gps_track","schema_version":1,"boot_session_id":"{{boot}}","time_unit":"ns"}
        {"sample_no":0,"phone_elapsed_ns":"9007199254740993","lat":16,"lng":108,"accuracy_m":4,"provider":"gps"}
        """ + suffix;
    private string Lux() => $$"""
        {"kind":"lux_log","schema_version":1,"boot_session_id":"{{boot}}","module_firmware_version_id":12}
        {"sample_no":0,"module_epoch":0,"seq":0,"module_ms":"9007199254740993","phone_elapsed_ns":"9007199254740993","lux":12.4}
        """;
    private string Config() => JsonSerializer.Serialize(new { schema_version = 1, boot_session_id = boot, profile_id = 1,
        phone_model = "fixture", camera_id = "0", app_version = "fixture", module_firmware_version_id = 12,
        sensor_timestamp_source = "REALTIME", elapsed_anchor_ns = "9007199254740000", utc_anchor = Anchor, utc_uncertainty_ms = 2,
        camera = new { iso = 100, exposure_time_ns = "1000000", aperture = 2.4, fps = 30, focus_mode = "manual", focus_distance = 0,
            white_balance_mode = "manual", white_balance_value = 4000, resolution = new { width = 1920, height = 1080 },
            ae_enabled = false, eis_enabled = false, hdr_enabled = false, night_mode_enabled = false },
        mount = new { camera_side = "right", mount_height_m = 1.5, angle_deg = 10, sensor_position = "roof" } });

    [Fact]
    public async Task Concurrent_create_returns_one_id_and_one_audit_and_conflicting_body_is_409()
    {
        var op = Guid.NewGuid();
        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => clients["owner"].PostAsJsonAsync("/api/v1/sweeps", Body(op))));
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Created);
        var ids = new List<string>();
        foreach (var r in responses) ids.Add((await Json(r, r.StatusCode == HttpStatusCode.Created ? 201 : 200)).GetProperty("sweep_id").GetString()!);
        Assert.Single(ids.Distinct());
        Assert.Equal(1, await Db(db => db.Set<AuditEvent>().IgnoreQueryFilters().CountAsync(x => x.EntityId == ids[0] && x.EntityType == AuditEntityType.SurveySweep)));
        var conflict = await Json(await clients["owner"].PostAsJsonAsync("/api/v1/sweeps", Body(op, 3)), 409);
        Assert.Equal("IDEMPOTENCY_CONFLICT", conflict.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Clip_enforces_hash_magic_limit_and_retry()
    {
        var id = await Create(); var video = Video();
        await Clip(id, video, 400, new string('0', 64));
        await Clip(id, new byte[12], 415);
        await Clip(id, video, 413, length: 314572801);
        await Clip(id, video, 200); await Clip(id, video, 200);
        await Clip(id, video, 409, new string('0', 64));
        Assert.Equal(1, await Db(db => db.Set<SurveyVideoClip>().CountAsync(x => x.SweepId == id)));
    }

    [Fact]
    public async Task Broken_final_line_and_nonfinite_measurements_leave_no_samples_or_objects()
    {
        var id = await Create(); var before = storage.Completed;
        foreach (var invalid in new[] { Gps("\n{"), Gps().Replace("\"accuracy_m\":4", "\"accuracy_m\":\"NaN\""), Gps().Replace("\"accuracy_m\":4", "\"accuracy_m\":\"Infinity\"") })
            await Raw(id, "gps_track", invalid, 400);
        foreach (var invalid in new[] { Lux().Replace("12.4", "\"NaN\""), Lux().Replace("12.4", "\"Infinity\""), Lux() + "\n{" })
            await Raw(id, "lux_log", invalid, 400);
        Assert.Equal(0, await Db(db => db.Set<SurveyGpsSample>().CountAsync(x => x.SweepId == id)));
        Assert.Equal(0, await Db(db => db.Set<SurveyRawFile>().CountAsync(x => x.SweepId == id)));
        Assert.Equal(0, await Db(db => db.Set<SurveyLuxSample>().CountAsync(x => x.SweepId == id)));
        Assert.Equal(before, storage.Completed);
        await Raw(id, "gps_track", Gps()); await Raw(id, "lux_log", Lux());
        Assert.Equal(9007199254740993L, await Db(db => db.Set<SurveyGpsSample>().Where(x => x.SweepId == id).Select(x => x.PhoneElapsedNs).SingleAsync()));
        Assert.Equal(9007199254740993L, await Db(db => db.Set<SurveyLuxSample>().Where(x => x.SweepId == id).Select(x => x.ModuleMs).SingleAsync()));
    }

    [Fact]
    public async Task Submit_checks_completeness_and_manifest_then_queues_once_and_freezes_uploads()
    {
        var id = await Create();
        var incomplete = await Json(await clients["owner"].PostAsJsonAsync($"/api/v1/sweeps/{id}/submit", new { client_op_id = Guid.NewGuid(), ended_elapsed_ns = "9007199254740999", manifest = new { clips = Array.Empty<object>(), gps_hash = new string('0', 64), lux_hash = new string('0', 64), config_hash = new string('0', 64) } }), 409);
        Assert.Equal("UPLOAD_INCOMPLETE", incomplete.GetProperty("error").GetProperty("code").GetString());
        var video = Video(); await Clip(id, video, 200);
        var files = new[] { ("gps_track", Gps()), ("lux_log", Lux()), ("capture_config", Config()) };
        foreach (var (kind, text) in files) await Raw(id, kind, text);
        var manifest = new { client_op_id = Guid.NewGuid(), ended_elapsed_ns = "9007199254740999", manifest = new {
            clips = new[] { new { clip_no = 0, sha256 = Hash(video) } }, gps_hash = Hash(Encoding.UTF8.GetBytes(Gps())),
            lux_hash = Hash(Encoding.UTF8.GetBytes(Lux())), config_hash = Hash(Encoding.UTF8.GetBytes(Config())) } };
        await Json(await clients["owner"].PostAsJsonAsync($"/api/v1/sweeps/{id}/submit", new { manifest.client_op_id, manifest.ended_elapsed_ns, manifest = new { manifest.manifest.clips, gps_hash = new string('0', 64), manifest.manifest.lux_hash, manifest.manifest.config_hash } }), 409);
        var submitted = await Json(await clients["owner"].PostAsJsonAsync($"/api/v1/sweeps/{id}/submit", manifest), 202);
        Assert.Equal("queued", submitted.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, submitted.GetProperty("coverage_pct").ValueKind);
        Assert.Equal(0, submitted.GetProperty("frame_count").GetInt32());
        await Json(await clients["owner"].PostAsJsonAsync($"/api/v1/sweeps/{id}/submit", manifest), 200);
        await Clip(id, video, 409, no: 1);
        Assert.Equal(1, await Db(db => db.Set<AuditEvent>().IgnoreQueryFilters().CountAsync(x => x.EntityId == id && x.Action == AuditAction.Submitted)));
    }

    [Fact]
    public async Task Other_assignee_and_foreign_commune_cannot_read_or_write_sweep()
    {
        var id = await Create();
        foreach (var who in new[] { "other", "outside" })
        {
            await Json(await clients[who].GetAsync($"/api/v1/sweeps/{id}"), 404);
            await Json(await clients[who].PostAsJsonAsync("/api/v1/sweeps", Body(Guid.NewGuid())), 404);
            await Json(await clients[who].PutAsync($"/api/v1/sweeps/{id}/raw/gps_track", new StringContent(Gps())), 404);
            var list = await Json(await clients[who].GetAsync($"/api/v1/sweeps?work_order_id={wo}"), 200);
            Assert.Empty(list.GetProperty("items").EnumerateArray());
        }
    }

    [Fact]
    public async Task Sweep_listing_orders_equal_timestamps_by_id_width_before_text()
    {
        var ids = await Db(async db =>
        {
            using var system = db.EnterUnscopedSystemWriteBackdoor();
            await using var tx = await db.Database.BeginTransactionAsync();
            var max = await db.Database.SqlQueryRaw<long>("SELECT COALESCE(MAX(substring(sweep_id from 5)::bigint), 0) AS \"Value\" FROM survey_sweep").SingleAsync();
            var boundary = 1000L; while (boundary <= max + 1) boundary *= 10;
            var keys = new[] { $"SWP-{boundary - 1}", $"SWP-{boundary}" };
            foreach (var key in keys.Reverse()) db.Add(new SurveySweep { SweepId = key, WorkOrderId = wo, CommuneId = commune,
                CapturedBy = users["owner"].UserId, ClientOpId = Guid.NewGuid(), BootSessionId = boot, UtcAnchor = Anchor,
                DataSource = DataSource.Simulated, CreateRequestHash = new string('0', 64) });
            await db.SaveChangesAsync(); await tx.CommitAsync(); return keys;
        });
        var list = await Json(await clients["owner"].GetAsync($"/api/v1/sweeps?work_order_id={wo}&processing_status=not_started&data_source=simulated"), 200);
        Assert.Equal(ids, list.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("sweep_id").GetString()));
    }

    [Fact]
    public async Task A_clip_in_flight_does_not_hold_the_work_order_lock()
    {
        var id = await Create();
        var parts = storage.HoldParts();
        var upload = Clip(id, Video(), 200);
        await storage.PartStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        // A Manager edit updates the work_order row. While the upload held FOR UPDATE on it, this waited
        // for the whole clip — minutes over mobile data.
        using var patch = new HttpRequestMessage(HttpMethod.Patch, $"/api/v1/work-orders/{wo}")
            { Content = JsonContent.Create(new { title = "Renamed mid-upload" }) };
        var edited = clients["manager"].SendAsync(patch);
        var first = await Task.WhenAny(edited, Task.Delay(TimeSpan.FromSeconds(10)));
        // Let the upload finish BEFORE any assert: a failing assert would otherwise start the teardown
        // while the clip row is still being written, and the half-deleted fixture leaks into the next run.
        parts.SetResult();
        await upload;
        Assert.Same(edited, first);
        await Json(await edited, 200);
        Assert.Equal(1, await Db(db => db.Set<SurveyVideoClip>().CountAsync(x => x.SweepId == id)));
    }

    private sealed class FakeS3() : AmazonS3Client(new AnonymousAWSCredentials(), new AmazonS3Config { ServiceURL = "http://unused.invalid" })
    {
        public int Completed { get; private set; }
        public override Task<InitiateMultipartUploadResponse> InitiateMultipartUploadAsync(InitiateMultipartUploadRequest request, CancellationToken ct = default)
            => Task.FromResult(new InitiateMultipartUploadResponse { UploadId = Guid.NewGuid().ToString() });
        private TaskCompletionSource? gate;
        public TaskCompletionSource PartStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource HoldParts() => gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async Task<UploadPartResponse> UploadPartAsync(UploadPartRequest request, CancellationToken ct = default)
        {
            PartStarted.TrySetResult();
            if (gate is not null) await gate.Task.WaitAsync(ct);
            return new UploadPartResponse { ETag = "fake" };
        }
        public override Task<CompleteMultipartUploadResponse> CompleteMultipartUploadAsync(CompleteMultipartUploadRequest request, CancellationToken ct = default)
        { Completed++; return Task.FromResult(new CompleteMultipartUploadResponse()); }
        public override Task<AbortMultipartUploadResponse> AbortMultipartUploadAsync(AbortMultipartUploadRequest request, CancellationToken ct = default)
            => Task.FromResult(new AbortMultipartUploadResponse());
    }
}
