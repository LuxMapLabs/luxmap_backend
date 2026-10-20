using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LuxMap.Modules.AI.Detection;
using LuxMap.Modules.Assets.Entities;
using LuxMap.Modules.Citizen;
using LuxMap.Modules.Citizen.Entities;
using LuxMap.Modules.Faults.Entities;
using LuxMap.Modules.WorkOrders;
using LuxMap.Modules.WorkOrders.Entities;
using LuxMap.Persistence;
using LuxMap.Persistence.Audit;
using LuxMap.Shared.AI;
using LuxMap.Shared.Contracts.Enums;
using LuxMap.Shared.Storage;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;

namespace LuxMap.Api.Tests;

public partial class WorkOrderTests
{
    private WebApplicationFactory<Program>? polePhotoHost;
    private readonly Dictionary<string, HttpClient> polePhotoClients = [];
    private readonly HintStub poleHints = new();
    private readonly PhotoSaveProbe poleSave = new();
    private readonly PhotoStore poleStore = new();
    private sealed class HintStub : ISingleLampPhotoHinter
    {
        public SingleLampPhotoHint? Result = new("off", 0.91, "model-test-v1", "model-test-v1+cq-legacy");
        public int Calls;
        public Func<Task>? DuringInference;
        public async Task<SingleLampPhotoHint?> HintAsync(Stream photo, Stream thumbnail, CancellationToken ct)
        {
            Interlocked.Increment(ref Calls);
            if (DuringInference is { } action) await action();
            return Result;
        }
    }
    private async Task<HttpClient> PoleClient(string who = "a")
    {
        polePhotoHost ??= factory.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.Replace(ServiceDescriptor.Singleton<IObjectStore>(poleStore));
            s.Replace(ServiceDescriptor.Singleton<ISingleLampPhotoHinter>(poleHints));
            s.AddDbContext<LuxMapDbContext>((_, o) => o.AddInterceptors(poleSave, photoCommands));
        }));
        if (polePhotoClients.TryGetValue(who, out var prior)) return prior;
        var client = polePhotoHost.CreateClient();
        if (who != "anonymous")
        {
            var token = await (await client.PostLoginAsync(users[who].Username, factory.AccountPassword)).ReadTokensAsync();
            client.DefaultRequestHeaders.Authorization = new("Bearer", token.AccessToken);
        }
        return polePhotoClients[who] = client;
    }
    private static string PhotosPath(string id) => $"{Route}/{id}/pole-photos";
    private async Task<(int Status, JsonElement Body)> PoleUpload(string id, string pole, string who = "a", string? op = null,
        byte[]? bytes = null, string time = "2026-10-05T14:00:00Z", string latitude = "16", string longitude = "108",
        string? extra = null, bool noLength = false)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(pole), "pole_id");
        form.Add(new StringContent(time), "captured_at");
        form.Add(new StringContent(latitude), "lat"); form.Add(new StringContent(longitude), "lng");
        form.Add(new StringContent(op ?? Guid.NewGuid().ToString()), "client_op_id");
        // Content-Type and filename intentionally lie: magic bytes decide.
        form.Add(new ByteArrayContent(bytes ?? Jpeg()), "photo", "untrusted.png");
        if (extra is not null) form.Add(new StringContent("server-owned"), extra);
        HttpContent body = noLength ? new UnknownLengthContent(await form.ReadAsByteArrayAsync()) : form;
        if (noLength) body.Headers.ContentType = form.Headers.ContentType;
        using var request = new HttpRequestMessage(HttpMethod.Post, PhotosPath(id)) { Content = body };
        var response = await (await PoleClient(who)).SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        return ((int)response.StatusCode, JsonDocument.Parse(text).RootElement.Clone());
    }
    private sealed class UnknownLengthContent(byte[] bytes) : HttpContent
    {
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => stream.WriteAsync(bytes).AsTask();
    }
    private async Task<JsonElement> PoleDecision(string id, string photo, string decision = "confirm", int expected = 200, string who = "manager")
    {
        var response = await (await PoleClient(who)).PostAsJsonAsync(PhotosPath(id) + $"/{photo}/decision", new { decision, note = "Đã kiểm tra" });
        var text = await response.Content.ReadAsStringAsync();
        Assert.True((int)response.StatusCode == expected, text);
        return JsonDocument.Parse(text).RootElement.Clone();
    }
    private async Task<string> NewPolePhoto(string id, string pole, string time = "2026-10-05T14:00:00Z")
    {
        var upload = await PoleUpload(id, pole, time: time);
        Assert.True(upload.Status == 201, upload.Body.GetRawText());
        return upload.Body.GetProperty("photo_id").GetString()!;
    }

    [Theory]
    [InlineData("manager", 403)] [InlineData("superior", 403)] [InlineData("admin", 403)]
    [InlineData("b", 404)] [InlineData("anonymous", 401)] [InlineData("a", 201)]
    public async Task Pole_photo_upload_requires_the_assigned_engineer(string who, int expected)
    {
        var pole = await PlantPole(.2); var id = await Plant(WorkOrderStatus.InProgress);
        var result = await PoleUpload(id, pole, who);
        Assert.Equal(expected, result.Status);
    }

    [Theory]
    [InlineData("manager", 200)] [InlineData("superior", 403)] [InlineData("admin", 403)] [InlineData("a", 403)] [InlineData("b", 403)]
    public async Task Pole_photo_decision_is_manager_only(string who, int expected)
    {
        var pole = await PlantPole(.2); var id = await Plant(WorkOrderStatus.InProgress);
        var photo = await NewPolePhoto(id, pole);
        await PoleDecision(id, photo, expected: expected, who: who);
    }

    [Theory]
    [InlineData(TaskKind.Survey)] [InlineData(TaskKind.Inspection)] [InlineData(TaskKind.Repair)]
    public async Task Pole_photos_accept_all_three_kinds_but_do_not_satisfy_after_evidence(TaskKind kind)
    {
        var pole = await PlantPole(.2); var id = await Plant(WorkOrderStatus.InProgress);
        await Db(async db =>
        {
            using var fixture = db.EnterUnscopedSystemWriteBackdoor();
            var order = await db.Set<WorkOrder>().IgnoreQueryFilters().SingleAsync(w => w.WorkOrderId == id);
            order.TaskKind = kind;
            if (kind == TaskKind.Survey)
            {
                order.SegmentId = null;
                db.Add(new WorkOrderSegment { WorkOrderId = id, CommuneId = home, SegmentId = segment, Position = 0 });
            }
            await db.SaveChangesAsync(); return 0;
        });
        await NewPolePhoto(id, pole);
        Assert.Equal(0, await Db(db => db.Set<RepairEvidence>().IgnoreQueryFilters().CountAsync(e => e.WorkOrderId == id)));
        if (kind == TaskKind.Repair)
            await Send("a", "POST", $"/{id}/complete", new { report_note = "Đã sửa và kiểm tra đèn" }, 409, "AFTER_EVIDENCE_REQUIRED");
    }

    [Theory]
    [InlineData(WorkOrderStatus.Assigned)] [InlineData(WorkOrderStatus.Done)] [InlineData(WorkOrderStatus.Cancelled)]
    public async Task New_pole_photo_requires_in_progress(WorkOrderStatus status)
    {
        var pole = await PlantPole(.2); var id = await Plant(status);
        Assert.Equal(409, (await PoleUpload(id, pole)).Status);
    }

    [Fact]
    public async Task Pole_photo_membership_uses_the_whole_WO12_set_and_separate_pole_communes()
    {
        var id = await Plant(WorkOrderStatus.InProgress);
        var outside = await PlantPole(.2, road: foreignSegment);
        Assert.Equal("POLE_NOT_IN_WORK_ORDER", Code((await PoleUpload(id, outside)).Body));
        var hidden = await PlantPole(.3, commune: foreign);
        Assert.Equal(404, (await PoleUpload(id, hidden)).Status);
        // This pole is in the caller's commune, but its road belongs to another commune; a linked
        // fault includes it in WO-12 even without that road being on the order.
        var fault = await PlantPoleFault(outside, FaultStatus.Confirmed);
        await Db(async db =>
        {
            using var fixture = db.EnterUnscopedSystemWriteBackdoor();
            db.Add(new WorkOrderFault { WorkOrderId = id, FaultId = fault, CommuneId = home, LinkedAt = DateTime.UtcNow });
            await db.SaveChangesAsync(); return 0;
        });
        await NewPolePhoto(id, outside);
        // More than a page: no query against only the first page is sufficient.
        await Db(async db =>
        {
            using var fixture = db.EnterUnscopedSystemWriteBackdoor();
            for (var n = 0; n < 205; n++) db.Add(new Pole { CommuneId = home, SegmentId = segment, DataSource = DataSource.Simulated,
                Geom = new NetTopologySuite.Geometries.Point(108, 16) { SRID = 4326 } });
            await db.SaveChangesAsync(); return 0;
        });
        var last = await PlantPole(.9);
        await NewPolePhoto(id, last);
    }

    [Theory]
    [InlineData("on", FixtureStatus.Dim, FixtureStatus.Dim)]
    [InlineData("on", FixtureStatus.Normal, FixtureStatus.Normal)]
    [InlineData("on", FixtureStatus.Out, FixtureStatus.Normal)]
    [InlineData("on", FixtureStatus.Unknown, FixtureStatus.Normal)]
    [InlineData("off", FixtureStatus.Normal, FixtureStatus.Out)]
    [InlineData("off", FixtureStatus.Dim, FixtureStatus.Out)]
    public async Task Pole_photo_confirmation_publishes_ON_OFF_without_claiming_brightness(string hint, FixtureStatus before, FixtureStatus after)
    {
        poleHints.Result = new(hint, .91, "model-v1", "artifact-v1");
        var pole = await PlantPole(.2, status: before, confidence: before == FixtureStatus.Unknown ? null : .8);
        var id = await Plant(WorkOrderStatus.InProgress);
        var photo = await NewPolePhoto(id, pole);
        Assert.Equal(before, await Db(db => db.Set<PoleCurrentStatus>().IgnoreQueryFilters().Where(p => p.PoleId == pole).Select(p => p.FixtureStatus).SingleAsync()));
        Assert.Equal(0, await Db(db => db.Set<Fault>().IgnoreQueryFilters().CountAsync(f => f.PoleId == pole)));
        var decision = await PoleDecision(id, photo);
        Assert.True(decision.GetProperty("status_applied").GetBoolean());
        var current = await Db(db => db.Set<PoleCurrentStatus>().IgnoreQueryFilters().SingleAsync(p => p.PoleId == pole));
        Assert.Equal(after, current.FixtureStatus);
        Assert.Equal(new DateTime(2026, 10, 5, 14, 0, 0, DateTimeKind.Utc), current.LastEvaluatedAt);
        Assert.Null(current.LastSweepId); Assert.Null(current.LastRunId);
        Assert.Equal(hint == "on" && before is FixtureStatus.Dim or FixtureStatus.Normal ? .8 : .91, current.StatusConfidence);
        Assert.Equal(0, await Db(db => db.Set<LuxMap.Modules.Survey.Entities.LuminanceHistory>().IgnoreQueryFilters().CountAsync(h => h.PoleId == pole)));
        Assert.Equal(0, await Db(db => db.Set<LuxMap.Modules.Survey.Entities.LuminanceBaseline>().IgnoreQueryFilters().CountAsync(h => h.PoleId == pole)));
        var faults = await Db(db => db.Set<Fault>().IgnoreQueryFilters().Where(f => f.PoleId == pole).ToArrayAsync());
        if (hint == "off")
        {
            var fault = Assert.Single(faults); Assert.Equal(FaultStatus.Confirmed, fault.FaultStatus);
            Assert.Equal(photo, fault.OriginPolePhotoId); Assert.Equal(DataSource.Simulated, fault.DataSource);
            Assert.Equal(SourceChannel.Cv, fault.SourceChannel); Assert.Equal("artifact-v1", fault.DetectionModelVersion);
            Assert.Equal(users["manager"].UserId, fault.ConfirmedBy);
        }
        else Assert.Empty(faults);
        var second = await PoleDecision(id, photo, expected: 409);
        Assert.Equal("POLE_PHOTO_ALREADY_DECIDED", Code(second));
    }

    [Fact]
    public async Task Pole_photo_ON_without_current_row_creates_normal_and_never_closes_faults()
    {
        poleHints.Result = new("on", .7, "v1", "a1");
        var pole = await PlantPole(.2); var existing = await PlantPoleFault(pole, FaultStatus.Confirmed);
        var id = await Plant(WorkOrderStatus.InProgress);
        await PoleDecision(id, await NewPolePhoto(id, pole));
        Assert.Equal(FixtureStatus.Normal, await Db(db => db.Set<PoleCurrentStatus>().IgnoreQueryFilters().Where(p => p.PoleId == pole).Select(p => p.FixtureStatus).SingleAsync()));
        Assert.Equal(FaultStatus.Confirmed, await Db(db => db.Set<Fault>().IgnoreQueryFilters().Where(f => f.FaultId == existing).Select(f => f.FaultStatus).SingleAsync()));
    }

    [Theory]
    [InlineData("2026-10-03T14:00:00Z")] [InlineData("2026-10-02T14:00:00Z")]
    public async Task Old_or_equal_pole_photo_is_confirmed_but_cannot_overwrite_or_create_fault(string time)
    {
        var pole = await PlantPole(.2, status: FixtureStatus.Normal, confidence: .8); var id = await Plant(WorkOrderStatus.InProgress);
        var photo = await NewPolePhoto(id, pole, time);
        var result = await PoleDecision(id, photo);
        Assert.False(result.GetProperty("status_applied").GetBoolean()); Assert.Equal("not_newer", result.GetProperty("status_reason").GetString());
        Assert.Equal("confirm", result.GetProperty("decision").GetString());
        Assert.Equal(FixtureStatus.Normal, await Db(db => db.Set<PoleCurrentStatus>().IgnoreQueryFilters().Where(p => p.PoleId == pole).Select(p => p.FixtureStatus).SingleAsync()));
        Assert.Equal(0, await Db(db => db.Set<Fault>().IgnoreQueryFilters().CountAsync(f => f.PoleId == pole)));
    }

    [Fact]
    public async Task Concurrent_OFF_photos_and_concurrent_decisions_leave_one_open_fault()
    {
        var pole = await PlantPole(.2); var id = await Plant(WorkOrderStatus.InProgress);
        var first = await NewPolePhoto(id, pole); var second = await NewPolePhoto(id, pole, "2026-10-06T14:00:00Z");
        await Task.WhenAll(PoleDecision(id, first), PoleDecision(id, second));
        Assert.Equal(1, await Db(db => db.Set<Fault>().IgnoreQueryFilters().CountAsync(f => f.PoleId == pole)));
        var third = await NewPolePhoto(id, pole, "2026-10-07T14:00:00Z");
        var manager = await PoleClient("manager");
        var decisions = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => manager.PostAsJsonAsync(PhotosPath(id) + $"/{third}/decision", new { decision = "confirm" })));
        Assert.Equal([200, 409], decisions.Select(r => (int)r.StatusCode).Order());
        Assert.Equal(1, await Db(db => db.Set<Fault>().IgnoreQueryFilters().CountAsync(f => f.PoleId == pole)));
    }

    [Theory]
    [InlineData("inconclusive")] [InlineData(null)]
    public async Task Unreviewed_inconclusive_or_unavailable_can_only_be_rejected(string? state)
    {
        poleHints.Result = state is null ? null : new(state, null, "v1", "a1");
        var pole = await PlantPole(.2); var id = await Plant(WorkOrderStatus.InProgress);
        var photo = await NewPolePhoto(id, pole);
        Assert.Equal("POLE_PHOTO_INCONCLUSIVE", Code(await PoleDecision(id, photo, expected: 409)));
        Assert.False((await PoleDecision(id, photo, "reject")).GetProperty("status_applied").GetBoolean());
        Assert.Equal(0, await Db(db => db.Set<PoleCurrentStatus>().IgnoreQueryFilters().CountAsync(p => p.PoleId == pole)));
    }

    [Fact]
    public async Task Pole_photo_replay_is_immutable_checks_all_content_and_still_checks_current_access()
    {
        var pole = await PlantPole(.2); var id = await Plant(WorkOrderStatus.InProgress); var op = Guid.NewGuid().ToString();
        var first = await PoleUpload(id, pole, op: op); Assert.Equal(201, first.Status);
        poleHints.Result = null;
        var retry = await PoleUpload(id, pole, op: op); Assert.Equal(200, retry.Status); Assert.Equal(first.Body.GetRawText(), retry.Body.GetRawText());
        Assert.Equal(1, poleHints.Calls); Assert.Equal(2, poleStore.Objects.Count);
        foreach (var changed in new[] {
            await PoleUpload(id, pole, op: op, latitude: "16.1"),
            await PoleUpload(id, pole, op: op, longitude: "108.1"),
            await PoleUpload(id, pole, op: op, time: "2026-10-06T14:00:00Z"),
            await PoleUpload(id, pole, op: op, bytes: Jpeg(7)),
            await PoleUpload(id, await PlantPole(.4), op: op) })
        { Assert.Equal(409, changed.Status); Assert.Equal("IDEMPOTENCY_CONFLICT", Code(changed.Body)); }
        await Send("a", "POST", $"/{id}/complete", new { report_note = "Đã hoàn thành kiểm tra" }, 200);
        Assert.Equal(200, (await PoleUpload(id, pole, op: op)).Status);
        await Db(async db =>
        {
            using var fixture = db.EnterUnscopedSystemWriteBackdoor();
            var order = await db.Set<WorkOrder>().IgnoreQueryFilters().SingleAsync(w => w.WorkOrderId == id);
            order.AssignedTo = users["b"].UserId; await db.SaveChangesAsync(); return 0;
        });
        Assert.Equal(404, (await PoleUpload(id, pole, op: op)).Status);
    }

    [Fact]
    public async Task Simultaneous_identical_pole_photo_retries_have_one_row_and_two_audits()
    {
        var pole = await PlantPole(.2); var id = await Plant(WorkOrderStatus.InProgress); var op = Guid.NewGuid().ToString();
        // Resolve host/client before parallel requests to avoid racing the test helper's dictionary.
        await PoleClient();
        var uploads = await Task.WhenAll(PoleUpload(id, pole, op: op), PoleUpload(id, pole, op: op));
        Assert.Equal([200, 201], uploads.Select(r => r.Status).Order());
        Assert.Equal(uploads[0].Body.GetRawText(), uploads[1].Body.GetRawText());
        Assert.Equal(1, await Db(db => db.Set<WorkOrderPolePhoto>().IgnoreQueryFilters().CountAsync(p => p.WorkOrderId == id)));
        var events = await Db(db => db.Set<AuditEvent>().IgnoreQueryFilters().Where(e => e.EntityId == id).ToArrayAsync());
        Assert.Equal([AuditAction.Submitted, AuditAction.Analyzed], events.OrderBy(e => e.AuditId).Select(e => e.Action));
        Assert.Equal(AuditActorKind.User, events.Single(e => e.Action == AuditAction.Submitted).ActorKind);
        Assert.Equal(AuditActorKind.Cv, events.Single(e => e.Action == AuditAction.Analyzed).ActorKind);
    }

    [Fact]
    public async Task Pole_photo_list_detail_and_media_share_parent_scope_and_preserve_original_bytes()
    {
        var pole = await PlantPole(.2); var id = await Plant(WorkOrderStatus.InProgress);
        var photo = await NewPolePhoto(id, pole); var path = PhotosPath(id) + "/" + photo;
        foreach (var who in new[] { "manager", "a", "superior", "admin" })
        {
            var client = await PoleClient(who);
            foreach (var suffix in new[] { "", "/thumbnail", "/original" }) Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(path + suffix)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(PhotosPath(id))).StatusCode);
        }
        foreach (var suffix in new[] { "", "/thumbnail", "/original" })
            Assert.Equal(HttpStatusCode.NotFound, (await (await PoleClient("b")).GetAsync(path + suffix)).StatusCode);
        var manager = await PoleClient("manager");
        var original = await manager.GetAsync(path + "/original");
        Assert.Equal(Jpeg(), await original.Content.ReadAsByteArrayAsync()); Assert.True(original.Headers.CacheControl?.NoStore);
        var other = await Plant(WorkOrderStatus.InProgress);
        Assert.Equal(HttpStatusCode.NotFound, (await manager.GetAsync(PhotosPath(other) + "/" + photo)).StatusCode);
        var list = await manager.GetFromJsonAsync<JsonElement>(PhotosPath(id) + "?pole_id=" + pole);
        Assert.Equal(photo, list.GetProperty("items")[0].GetProperty("photo_id").GetString());
        Assert.Equal(1, poleHints.Calls);
    }

    [Theory]
    [InlineData("timestamp")] [InlineData("nan")] [InlineData("uuid")] [InlineData("server-owned")] [InlineData("png")] [InlineData("broken-jpeg")]
    public async Task Pole_photo_input_is_strict_before_storage(string kind)
    {
        var pole = await PlantPole(.2); var id = await Plant(WorkOrderStatus.InProgress);
        var result = await PoleUpload(id, pole, time: kind == "timestamp" ? "2026-10-05T14:00:00" : "2026-10-05T14:00:00Z",
            latitude: kind == "nan" ? "NaN" : "16", op: kind == "uuid" ? "invalid" : null,
            extra: kind == "server-owned" ? "data_source" : null,
            bytes: kind == "png" ? [137, 80, 78, 71] : kind == "broken-jpeg" ? [255, 216, 255, 0] : null);
        Assert.Equal(kind is "png" or "broken-jpeg" ? 415 : 400, result.Status);
        Assert.Empty(poleStore.Objects); Assert.Equal(0, poleHints.Calls);
    }

    [Fact]
    public async Task Pole_photo_body_and_file_caps_are_real_HTTP_limits_including_unknown_length()
    {
        var pole = await PlantPole(.2); var id = await Plant(WorkOrderStatus.InProgress);
        foreach (var length in new[] { 16 * 1024 * 1024 + 1, 17 * 1024 * 1024 + 1 })
            foreach (var noLength in new[] { false, true })
            {
                var result = await PoleUpload(id, pole, bytes: new byte[length], noLength: noLength);
                Assert.Equal(413, result.Status); Assert.Equal("UPLOAD_TOO_LARGE", Code(result.Body));
            }
        var jpeg = Jpeg(); var exact = new byte[16 * 1024 * 1024]; jpeg.CopyTo(exact, 0);
        Assert.Equal(201, (await PoleUpload(id, pole, bytes: exact)).Status);
    }

    [Fact]
    public async Task Pole_photo_pixel_cap_precedes_decode_even_without_AI()
    {
        poleHints.Result = null;
        var pole = await PlantPole(.2); var id = await Plant(WorkOrderStatus.InProgress);
        var bytes = Jpeg();
        // Patch the JPEG SOF header to claim 65535² pixels without allocating them.
        var found = false;
        for (var at = 0; at < bytes.Length - 9; at++)
            if (bytes[at] == 255 && bytes[at + 1] is 192 or 194)
            { bytes[at + 5] = bytes[at + 6] = bytes[at + 7] = bytes[at + 8] = 255; found = true; break; }
        Assert.True(found);
        Assert.Equal(400, (await PoleUpload(id, pole, bytes: bytes)).Status);
        Assert.Empty(poleStore.Objects); Assert.Equal(0, poleHints.Calls);
    }

    [Fact]
    public async Task Pole_photo_rechecks_assignment_after_storage_without_holding_the_parent_lock()
    {
        var pole = await PlantPole(.2); var id = await Plant(WorkOrderStatus.InProgress);
        poleStore.DuringWrite = async () => await Db(async db =>
        {
            using var fixture = db.EnterUnscopedSystemWriteBackdoor();
            await db.Database.ExecuteSqlRawAsync("SET lock_timeout = '2s'");
            var order = await db.Set<WorkOrder>().IgnoreQueryFilters().SingleAsync(w => w.WorkOrderId == id);
            order.AssignedTo = users["b"].UserId; await db.SaveChangesAsync(); return 0;
        });
        Assert.Equal(404, (await PoleUpload(id, pole)).Status);
        Assert.Equal(0, await Db(db => db.Set<WorkOrderPolePhoto>().IgnoreQueryFilters().CountAsync(p => p.WorkOrderId == id)));
    }

    [Fact]
    public async Task Pole_photo_rechecks_state_after_inference_without_holding_a_transaction()
    {
        var pole = await PlantPole(.2); var id = await Plant(WorkOrderStatus.InProgress);
        poleHints.DuringInference = async () => await Db(async db =>
        {
            using var fixture = db.EnterUnscopedSystemWriteBackdoor();
            await db.Database.ExecuteSqlRawAsync("SET lock_timeout = '2s'");
            var order = await db.Set<WorkOrder>().IgnoreQueryFilters().SingleAsync(w => w.WorkOrderId == id);
            order.WoStatus = WorkOrderStatus.Done; order.CompletedAt = DateTime.UtcNow; order.ReportNote = "done";
            await db.SaveChangesAsync(); return 0;
        });
        Assert.Equal(409, (await PoleUpload(id, pole)).Status);
        Assert.Equal(0, await Db(db => db.Set<WorkOrderPolePhoto>().IgnoreQueryFilters().CountAsync(p => p.WorkOrderId == id)));
    }

    [Fact]
    public async Task Pole_photo_audited_marker_requires_exactly_one_event_and_DB_checks_reject_invalid_AI()
    {
        var pole = await PlantPole(.2); var id = await Plant(WorkOrderStatus.InProgress);
        var photo = await NewPolePhoto(id, pole);
        await Db(async db =>
        {
            var row = await db.Set<WorkOrderPolePhoto>().IgnoreQueryFilters().SingleAsync(p => p.PhotoId == photo);
            row.DecisionNote = "missing audit";
            await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
            db.AddRange(new AuditEvent { CommuneId = home, EntityId = id, CorrelationId = "guard" },
                new AuditEvent { CommuneId = home, EntityId = id, CorrelationId = "guard" });
            await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
            return 0;
        });
        foreach (var sql in new[] {
            "UPDATE work_order_pole_photo SET confidence = 'NaN'::float8 WHERE photo_id = {0}",
            "UPDATE work_order_pole_photo SET confidence = 'Infinity'::float8 WHERE photo_id = {0}",
            "UPDATE work_order_pole_photo SET confidence = NULL WHERE photo_id = {0}",
            "UPDATE work_order_pole_photo SET cv_state = 'dim' WHERE photo_id = {0}",
            "UPDATE work_order_pole_photo SET lat = 'NaN'::float8 WHERE photo_id = {0}" })
            await Db(async db =>
            {
                var error = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(sql, photo));
                Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState); return 0;
            });
    }

    [Fact]
    public async Task Shared_photo_hinter_preserves_Citizen_labels_and_exact_legacy_hash()
    {
        using var services = new ServiceCollection().AddLogging().AddSingleton(new LuxMap.Modules.AI.AiOptions())
            .AddSingleton<YoloModel>().AddSingleton<YoloOnOffDetector>().BuildServiceProvider();
        var config = new ConfigurationBuilder().AddInMemoryCollection([new("Citizen:DaylightMeanLuma", "0.45")]).Build();
        var shared = new YoloSingleLampPhotoHinter(services, config,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<YoloSingleLampPhotoHinter>.Instance);
        using var original = new MemoryStream(Jpeg()); using var thumb = new MemoryStream(Jpeg());
        var result = await shared.HintAsync(original, thumb, CancellationToken.None);
        Assert.NotNull(result);
        var expectedRules = "cq-" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("citizen-hint:single-lamp;daylight-mean-luma<=0.45")))[..12];
        Assert.Equal(services.GetRequiredService<YoloOnOffDetector>().Artifact.Version + "+" + expectedRules, result.ArtifactVersion);
        var adapter = new YoloCitizenPhotoHinter(new FixedHint(result));
        var citizen = await adapter.HintAsync(original, thumb, CancellationToken.None);
        Assert.NotNull(citizen); Assert.Equal(result.ArtifactVersion, citizen.ArtifactVersion);
        Assert.Equal(result.Confidence, citizen.Confidence);
        Assert.Equal(result.CvState switch { "on" => CitizenPhotoHint.LampOn, "off" => CitizenPhotoHint.LampOff, _ => CitizenPhotoHint.Inconclusive }, citizen.Label);
    }

    private sealed class PhotoSaveProbe : SaveChangesInterceptor
    {
        public bool FailAnalysis;
        public AuditAction? FailAction;
        public readonly List<int> EventCounts = [];
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            var entries = data.Context!.ChangeTracker.Entries<AuditEvent>().Where(e => e.State == EntityState.Added).ToArray();
            if (data.Context.ChangeTracker.Entries<WorkOrderPolePhoto>().Any(e => e.State is EntityState.Added or EntityState.Modified)
                || data.Context.ChangeTracker.Entries<Fault>().Any(e => e.State == EntityState.Added && e.Entity.OriginPolePhotoId != null)
                || entries.Any(e => e.Entity.EntityId.StartsWith("WO-", StringComparison.Ordinal)))
                EventCounts.Add(entries.Length);
            if (FailAction is { } fail && entries.Any(e => e.Entity.Action == fail))
                throw new InvalidOperationException("Injected review/decision save failure");
            if (FailAnalysis && entries.Any(e => e.Entity.Action == AuditAction.Analyzed))
                throw new InvalidOperationException("Injected second-save failure");
            return ValueTask.FromResult(result);
        }
    }

    [Fact]
    public async Task Pole_photo_second_save_failure_rolls_back_photo_and_all_audits()
    {
        var pole = await PlantPole(.2); var id = await Plant(WorkOrderStatus.InProgress);
        poleSave.FailAnalysis = true;
        Assert.Equal(500, (await PoleUpload(id, pole)).Status);
        Assert.Equal(0, await Db(db => db.Set<WorkOrderPolePhoto>().IgnoreQueryFilters().CountAsync(p => p.WorkOrderId == id)));
        Assert.Equal(0, await Db(db => db.Set<AuditEvent>().IgnoreQueryFilters().CountAsync(e => e.EntityId == id)));
        poleSave.FailAnalysis = false;
        await PoleDecision(id, await NewPolePhoto(id, pole));
        Assert.All(poleSave.EventCounts, n => Assert.Equal(1, n));
    }

    [Fact]
    public async Task Pole_photo_missing_model_still_returns_201_with_unavailable_and_one_user_audit()
    {
        // Exercise the real lazy adapter's missing-file path, not a fake null result.
        await using var broken = factory.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.Replace(ServiceDescriptor.Singleton<IObjectStore>(poleStore));
            s.Replace(ServiceDescriptor.Singleton(new LuxMap.Modules.AI.AiOptions { ModelPath = "missing-pole-photo-model.onnx" }));
        }));
        polePhotoHost = broken;
        var pole = await PlantPole(.2); var id = await Plant(WorkOrderStatus.InProgress);
        var response = await PoleUpload(id, pole);
        Assert.Equal(201, response.Status);
        Assert.Equal("unavailable", response.Body.GetProperty("ai_status").GetString());
        Assert.Equal(JsonValueKind.Null, response.Body.GetProperty("cv_state").ValueKind);
        Assert.Equal(JsonValueKind.Null, response.Body.GetProperty("artifact_version").ValueKind);
        var events = await Db(db => db.Set<AuditEvent>().IgnoreQueryFilters().Where(e => e.EntityId == id).ToArrayAsync());
        Assert.Equal(AuditAction.Submitted, Assert.Single(events).Action);
        polePhotoHost = null;
    }

    [Fact]
    public async Task Pole_photo_cross_commune_uses_poles_source_and_scope_on_every_read()
    {
        var pole = await PlantPole(.2, commune: foreign); var id = await Plant(WorkOrderStatus.InProgress);
        await Db(async db =>
        {
            using var fixture = db.EnterUnscopedSystemWriteBackdoor();
            db.Add(new LuxMap.Modules.Identity.Entities.AppUserCommune { UserId = users["a"].UserId, CommuneId = foreign });
            var p = await db.Set<Pole>().IgnoreQueryFilters().SingleAsync(p => p.PoleId == pole);
            p.DataSource = DataSource.CalibrationRig;
            await db.SaveChangesAsync(); return 0;
        });
        var upload = await PoleUpload(id, pole);
        Assert.Equal(201, upload.Status); Assert.Equal("calibration_rig", upload.Body.GetProperty("data_source").GetString());
        var photo = upload.Body.GetProperty("photo_id").GetString()!;
        var row = await Db(db => db.Set<WorkOrderPolePhoto>().IgnoreQueryFilters().SingleAsync(p => p.PhotoId == photo));
        Assert.Equal(foreign, row.CommuneId);
        await PoleDecision(id, photo);
        Assert.Equal(DataSource.CalibrationRig, await Db(db => db.Set<Fault>().IgnoreQueryFilters().Where(f => f.OriginPolePhotoId == photo).Select(f => f.DataSource).SingleAsync()));
        // The other engineer can see this reassigned parent but lacks the photo's commune.
        await Db(async db =>
        {
            using var fixture = db.EnterUnscopedSystemWriteBackdoor();
            var order = await db.Set<WorkOrder>().IgnoreQueryFilters().SingleAsync(w => w.WorkOrderId == id);
            order.AssignedTo = users["b"].UserId; await db.SaveChangesAsync(); return 0;
        });
        var reader = await PoleClient("b");
        foreach (var suffix in new[] { "", "/original", "/thumbnail" })
            Assert.Equal(HttpStatusCode.NotFound, (await reader.GetAsync(PhotosPath(id) + "/" + photo + suffix)).StatusCode);
        Assert.Empty((await reader.GetFromJsonAsync<JsonElement>(PhotosPath(id))).GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task Pole_photo_decision_waits_for_the_same_parent_pole_lock_as_sweep_publication()
    {
        var pole = await PlantPole(.2); var id = await Plant(WorkOrderStatus.InProgress);
        var photo = await NewPolePhoto(id, pole);
        var manager = await PoleClient("manager");
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LuxMapDbContext>();
        await using var tx = await db.Database.BeginTransactionAsync();
        // NO KEY UPDATE: a child's FK KEY SHARE does not mask a missing explicit lock.
        await db.Database.ExecuteSqlAsync($"SELECT 1 FROM pole WHERE pole_id = {pole} FOR NO KEY UPDATE");
        var decision = manager.PostAsJsonAsync(PhotosPath(id) + $"/{photo}/decision", new { decision = "confirm" });
        try
        {
            await Task.Delay(600);
            Assert.False(decision.IsCompleted, "Decision bypassed the publication pole lock");
        }
        finally { await tx.RollbackAsync(); }
        Assert.Equal(HttpStatusCode.OK, (await decision).StatusCode);
    }

    [Fact]
    public async Task Pole_photo_DB_only_target_FK_is_present_and_rejects_a_different_commune()
    {
        var pole = await PlantPole(.2); var id = await Plant(WorkOrderStatus.InProgress);
        var photo = await NewPolePhoto(id, pole);
        await Db(async db =>
        {
            Assert.Equal(1, await db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM pg_constraint WHERE conname = 'fk_pole_photo_same_commune'").SingleAsync());
            var error = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlAsync(
                $"UPDATE work_order_pole_photo SET commune_id = {foreign} WHERE photo_id = {photo}"));
            Assert.Equal("fk_pole_photo_same_commune", error.ConstraintName); return 0;
        });
    }

    private sealed class FixedHint(SingleLampPhotoHint hint) : ISingleLampPhotoHinter
    {
        public Task<SingleLampPhotoHint?> HintAsync(Stream p, Stream t, CancellationToken ct) => Task.FromResult<SingleLampPhotoHint?>(hint);
    }
}
