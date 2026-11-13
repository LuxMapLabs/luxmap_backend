using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using LuxMap.Modules.Faults.Entities;
using LuxMap.Modules.WorkOrders.Entities;
using LuxMap.Persistence.Audit;
using LuxMap.Persistence.Versions;
using LuxMap.Shared.AI;
using LuxMap.Shared.Contracts.Enums;
using LuxMap.Shared.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LuxMap.Api.Tests;

/// <summary>POLE-VIDEO PV-c2: a clip of one lamp, uploaded for a work order, kept, judged frame by frame, and recorded as one pole photo.</summary>
public partial class WorkOrderTests
{
    private static string ClipUploadPath(string order, Guid op) => $"{Route}/{order}/pole-videos/{op}";
    private static string Sha(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    private static string Recently(int minutesAgo = 5) => DateTime.UtcNow.AddMinutes(-minutesAgo).ToString("yyyy-MM-ddTHH:mm:ssZ");

    /// <summary>Every frame of the clip gets this answer (the single-lamp model is stubbed).</summary>
    private void FramesSay(string state, double? confidence = .8, string? reason = null)
        => poleHints.PerCall = _ => new(state, state == "inconclusive" ? null : confidence, "model-test-v1", "model-test-v1+cq-legacy", reason);

    private async Task<(int Status, JsonElement Body)> PutClip(string order, string pole, byte[] clip, string who = "a", Guid? op = null,
        string? time = null, string latitude = "16", string longitude = "108", string? sha = null, bool noLength = false,
        string? declaredLength = null, string? query = null, bool omitPole = false)
    {
        var operation = op ?? Guid.NewGuid();
        var capture = Uri.EscapeDataString(time ?? Recently());
        var url = ClipUploadPath(order, operation) + $"?{(omitPole ? "" : $"pole_id={pole}&")}captured_at={capture}&lat={latitude}&lng={longitude}{query}";
        HttpContent content = noLength ? new UnknownLengthContent(clip) : declaredLength is null ? new ByteArrayContent(clip) : new DeclaredLengthContent(clip, long.Parse(declaredLength));
        content.Headers.ContentType = new("video/mp4");
        using var request = new HttpRequestMessage(HttpMethod.Put, url) { Content = content };
        request.Headers.TryAddWithoutValidation("X-Content-SHA256", sha ?? Sha(clip));
        var response = await (await PoleClient(who)).SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        return ((int)response.StatusCode, text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone());
    }

    private sealed class DeclaredLengthContent(byte[] bytes, long declared) : HttpContent
    {
        protected override bool TryComputeLength(out long length) { length = declared; return true; }
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => stream.WriteAsync(bytes).AsTask();
    }

    private async Task<(string Order, string Pole)> ClipTarget(WorkOrderStatus status = WorkOrderStatus.InProgress)
        => (await Plant(status), await PlantPole(.2)) is var (order, pole) ? (order, pole) : default;

    private Task<WorkOrderPolePhoto[]> Rows(string order) => Db(db => db.Set<WorkOrderPolePhoto>().IgnoreQueryFilters().Where(p => p.WorkOrderId == order).ToArrayAsync());
    private int StoredClips() => poleStore.Objects.Keys.Count(k => k.Item1 == StorageBucket.Video);

    [Theory]
    [InlineData("manager", 403)] [InlineData("superior", 403)] [InlineData("admin", 403)]
    [InlineData("b", 404)] [InlineData("anonymous", 401)] [InlineData("a", 201)]
    public async Task A_pole_clip_can_be_uploaded_only_by_the_assigned_engineer(string who, int expected)
    {
        FramesSay("on");
        var (order, pole) = await ClipTarget();
        var result = await PutClip(order, pole, await PoleVideoTestClips.MakeAsync(4), who);
        Assert.Equal(expected, result.Status);
    }

    [Fact]
    public async Task A_clip_is_kept_judged_and_recorded_as_one_pole_photo_that_carries_the_clip()
    {
        FramesSay("on", .8);
        var (order, pole) = await ClipTarget();
        var clip = await PoleVideoTestClips.MakeAsync(5);
        var (status, body) = await PutClip(order, pole, clip);
        Assert.True(status == 201, body.GetRawText());

        Assert.Equal(("on", .8, "available"), (body.GetProperty("cv_state").GetString(), body.GetProperty("confidence").GetDouble(), body.GetProperty("ai_status").GetString()));
        Assert.Equal("model-test-v1", body.GetProperty("model_version").GetString());
        Assert.Matches(@"^model-test-v1\+cq-legacy\+pv-[0-9a-f]{12}$", body.GetProperty("artifact_version").GetString());
        Assert.Equal("simulated", body.GetProperty("data_source").GetString());      // the pole's own, never the client's
        var video = body.GetProperty("video");
        Assert.Equal((5, 5, 0, 0), (video.GetProperty("sampled_frames").GetInt32(), video.GetProperty("on_frames").GetInt32(),
            video.GetProperty("off_frames").GetInt32(), video.GetProperty("daylight_frames").GetInt32()));
        Assert.InRange(video.GetProperty("duration_ms").GetInt32(), 4_900, 5_100);
        Assert.Equal((clip.Length, Sha(clip), false), (video.GetProperty("byte_count").GetInt64(), video.GetProperty("sha256").GetString(), video.GetProperty("hdr").GetBoolean()));
        Assert.Equal((.8, .8, .8), (video.GetProperty("confidence_min").GetDouble(), video.GetProperty("confidence_median").GetDouble(), video.GetProperty("confidence_max").GetDouble()));
        Assert.Equal(JsonValueKind.Null, video.GetProperty("reason_code").ValueKind);
        Assert.Equal(JsonValueKind.Null, video.GetProperty("viewed_by").ValueKind);
        var photoId = body.GetProperty("photo_id").GetString()!;
        Assert.EndsWith($"/pole-photos/{photoId}/video", video.GetProperty("video_url").GetString());

        // The row, the clip and the frame that stands for it.
        var row = Assert.Single(await Rows(order));
        Assert.Equal((users["a"].UserId, pole), (row.UploadedBy, row.PoleId));
        Assert.Equal(clip, poleStore.Objects[(StorageBucket.Video, row.VideoObjectKey!)]);
        Assert.Equal($"pole-videos/{order}/{row.ClientOpId:N}/{Sha(clip)}.mp4", row.VideoObjectKey);
        Assert.Equal(new byte[] { 0xFF, 0xD8, 0xFF }, poleStore.Objects[(StorageBucket.Evidence, row.ObjectKey)][..3]);
        Assert.True(poleStore.Objects.ContainsKey((StorageBucket.Evidence, row.ThumbnailKey)));
        Assert.Equal((64, 32), (row.Width, row.Height));
        var version = await Db(db => db.Set<ArtifactVersion>().SingleAsync(v => v.VersionId == row.VideoExtractorVersionId));
        Assert.Equal(ArtifactComponents.FrameExtractor, version.Component);

        // Submitted then analysed, by the engineer and by the model: two events, one per save.
        var events = await Db(db => db.Set<AuditEvent>().IgnoreQueryFilters().Where(e => e.EntityId == order).OrderBy(e => e.AuditId).ToArrayAsync());
        Assert.Equal([AuditAction.Submitted, AuditAction.Analyzed], events.Select(e => e.Action));
        Assert.All(events, e => Assert.Equal("video", JsonDocument.Parse(e.AfterState!).RootElement.GetProperty("media").GetString()));

        // It reads back as any pole photo does, the clip block included; a plain photo has none.
        var detail = await (await PoleClient("a")).GetFromJsonAsync<JsonElement>(PhotosPath(order) + "/" + photoId);
        Assert.Equal(Sha(clip), detail.GetProperty("video").GetProperty("sha256").GetString());
        var plain = await PoleUpload(order, pole);
        Assert.Equal(JsonValueKind.Null, plain.Body.GetProperty("video").ValueKind);
    }

    [Fact]
    public async Task The_same_operation_again_returns_what_was_stored_and_reads_judges_and_writes_nothing()
    {
        FramesSay("off", .9);
        var (order, pole) = await ClipTarget();
        var clip = await PoleVideoTestClips.MakeAsync(4);
        var op = Guid.NewGuid();
        var first = await PutClip(order, pole, clip, op: op);
        Assert.Equal(201, first.Status);
        var (calls, objects) = (poleHints.Calls, poleStore.Objects.Count);
        var eventsBefore = await Db(db => db.Set<AuditEvent>().IgnoreQueryFilters().CountAsync(e => e.EntityId == order));

        var again = await PutClip(order, pole, clip, op: op, time: first.Body.GetProperty("captured_at").GetString());
        Assert.Equal(200, again.Status);
        Assert.Equal(first.Body.GetProperty("photo_id").GetString(), again.Body.GetProperty("photo_id").GetString());
        Assert.Equal((calls, objects), (poleHints.Calls, poleStore.Objects.Count));
        Assert.Equal(eventsBefore, await Db(db => db.Set<AuditEvent>().IgnoreQueryFilters().CountAsync(e => e.EntityId == order)));
        Assert.Single(await Rows(order));

        // A different clip under the same operation is not a retry.
        var other = await PoleVideoTestClips.MakeAsync(4, fps: 30);
        var conflict = await PutClip(order, pole, other, op: op, time: first.Body.GetProperty("captured_at").GetString());
        Assert.Equal(409, conflict.Status);
        Assert.Equal("IDEMPOTENCY_CONFLICT", conflict.Body.GetProperty("error").GetProperty("code").GetString());
        Assert.Single(await Rows(order));
    }

    [Fact]
    public async Task A_retry_of_a_stored_clip_is_answered_after_its_capture_has_aged_past_the_week_but_a_new_clip_is_not()
    {
        FramesSay("on");
        poleClock = new(DateTimeOffset.UtcNow);
        var (order, pole) = await ClipTarget();
        var clip = await PoleVideoTestClips.MakeAsync(4);
        var op = Guid.NewGuid();
        var first = await PutClip(order, pole, clip, op: op);
        Assert.Equal(201, first.Status);
        var captured = first.Body.GetProperty("captured_at").GetString();

        // The phone never saw the 201 and retries a week later: the server already has the clip.
        poleClock.Advance(TimeSpan.FromDays(8));
        var again = await PutClip(order, pole, clip, op: op, time: captured);
        Assert.True(again.Status == 200, $"expected 200, got {again.Status} {again.Body}");
        Assert.Equal(first.Body.GetProperty("photo_id").GetString(), again.Body.GetProperty("photo_id").GetString());

        // The same capture under a new operation is a new clip, and too old.
        var fresh = await PutClip(order, pole, clip, time: captured);
        Assert.Equal((400, "VALIDATION_FAILED"), (fresh.Status, fresh.Body.GetProperty("error").GetProperty("code").GetString()));
        Assert.Single(await Rows(order));
    }

    [Fact]
    public async Task Parallel_attempts_of_one_operation_end_on_one_row()
    {
        FramesSay("on");
        var (order, pole) = await ClipTarget();
        var clip = await PoleVideoTestClips.MakeAsync(4);
        var op = Guid.NewGuid(); var time = Recently();
        var results = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => PutClip(order, pole, clip, op: op, time: time)));
        Assert.All(results, r => Assert.Contains(r.Status, new[] { 200, 201 }));
        Assert.Single(results.Select(r => r.Body.GetProperty("photo_id").GetString()).Distinct());
        Assert.Single(await Rows(order));
    }

    [Fact]
    public async Task The_request_is_checked_before_and_while_the_clip_is_taken()
    {
        FramesSay("on");
        var (order, pole) = await ClipTarget();
        var clip = await PoleVideoTestClips.MakeAsync(4);
        async Task Refused(int status, string code, Func<Task<(int Status, JsonElement Body)>> attempt, [System.Runtime.CompilerServices.CallerArgumentExpression(nameof(attempt))] string? what = null)
        {
            var result = await attempt();
            Assert.True(result.Status == status, $"{what}: expected {status}, got {result.Status} {result.Body}");
            Assert.Equal(code, result.Body.GetProperty("error").GetProperty("code").GetString());
        }
        await Refused(400, "VALIDATION_FAILED", () => PutClip(order, pole, clip, sha: "ABC"));                       // not 64 lower-case hex
        await Refused(400, "VALIDATION_FAILED", () => PutClip(order, pole, clip, omitPole: true));
        await Refused(400, "VALIDATION_FAILED", () => PutClip(order, pole, clip, query: "&extra=1"));                // unknown query key
        await Refused(400, "VALIDATION_FAILED", () => PutClip(order, pole, clip, latitude: "91"));
        await Refused(400, "VALIDATION_FAILED", () => PutClip(order, pole, clip, time: "2026-10-05T14:00:00"));      // no Z / offset
        await Refused(400, "VALIDATION_FAILED", () => PutClip(order, pole, clip, time: Recently(8 * 24 * 60)));      // older than a week
        await Refused(400, "VALIDATION_FAILED", () => PutClip(order, pole, clip, time: DateTime.UtcNow.AddHours(1).ToString("yyyy-MM-ddTHH:mm:ssZ")));
        await Refused(413, "UPLOAD_TOO_LARGE", () => PutClip(order, pole, clip, declaredLength: (100L * 1024 * 1024 + 1).ToString()));
        await Refused(400, "HASH_MISMATCH", () => PutClip(order, pole, clip, sha: Sha([1, 2, 3])));                  // body is not what the header says
        await Refused(415, "UNSUPPORTED_VIDEO_FORMAT", () => PutClip(order, pole, Jpeg()));                         // a photo is not a clip
        // No Content-Length (a chunked body) cannot be sent through the in-memory test server, which measures the body itself: ask the service.
        await PoleClient();
        using (var scope = polePhotoHost!.Services.CreateScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<LuxMap.Modules.WorkOrders.WorkOrderPolePhotoService>();
            var error = await Assert.ThrowsAsync<LuxMap.Shared.Http.LuxMapException>(() => service.UploadVideoAsync(order, pole, Recently(), "16", "108",
                Guid.NewGuid().ToString(), Sha(clip), new MemoryStream(clip), null, default));
            Assert.Equal(HttpStatusCode.BadRequest, error.StatusCode);
        }
        var badOp = await (await PoleClient("a")).PutAsync($"{Route}/{order}/pole-videos/not-a-guid?pole_id={pole}&captured_at={Recently()}&lat=16&lng=108", new ByteArrayContent(clip));
        Assert.Equal(HttpStatusCode.BadRequest, badOp.StatusCode);
        Assert.Empty(await Rows(order));
    }

    [Fact]
    public async Task A_pole_outside_the_work_order_or_an_order_not_in_progress_is_refused_before_the_clip_is_taken()
    {
        FramesSay("on");
        var (order, _) = await ClipTarget();
        var clip = await PoleVideoTestClips.MakeAsync(4);
        var outside = await PlantPole(.3, road: foreignSegment);
        var stored = StoredClips();
        var wrongPole = await PutClip(order, outside, clip);
        Assert.Equal((409, "POLE_NOT_IN_WORK_ORDER"), (wrongPole.Status, wrongPole.Body.GetProperty("error").GetProperty("code").GetString()));
        var (assigned, pole) = await ClipTarget(WorkOrderStatus.Assigned);
        var notStarted = await PutClip(assigned, pole, clip);
        Assert.Equal((409, "WORK_ORDER_NOT_IN_PROGRESS"), (notStarted.Status, notStarted.Body.GetProperty("error").GetProperty("code").GetString()));
        Assert.Equal(stored, StoredClips());                  // neither attempt took a single byte of the clip
        Assert.Equal(0, poleHints.Calls);
    }

    [Theory]
    [InlineData(2.0, 5, "VIDEO_TOO_SHORT")]
    [InlineData(32.0, 5, "VIDEO_TOO_LONG")]
    public async Task A_clip_of_the_wrong_length_is_a_400_that_gives_its_length_and_leaves_no_row(double seconds, int fps, string code)
    {
        FramesSay("on");
        var (order, pole) = await ClipTarget();
        var result = await PutClip(order, pole, await PoleVideoTestClips.MakeAsync(seconds, fps));
        Assert.Equal((400, code), (result.Status, result.Body.GetProperty("error").GetProperty("code").GetString()));
        Assert.Equal((int)(seconds * 1000), result.Body.GetProperty("error").GetProperty("details").GetProperty("duration_ms").GetInt32());
        Assert.Empty(await Rows(order));
        Assert.Equal(0, poleHints.Calls);
    }

    [Fact]
    public async Task A_clip_whose_frames_disagree_or_cannot_be_judged_is_kept_without_a_verdict_and_says_why()
    {
        var (order, pole) = await ClipTarget();
        var clip = await PoleVideoTestClips.MakeAsync(5);
        // Frames: ON, OFF, ON, OFF, ON — 60 % agree: below the 70 % limit.
        poleHints.PerCall = n => new(n % 2 == 0 ? "on" : "off", .9, "model-test-v1", "model-test-v1+cq-legacy");
        var mixed = await PutClip(order, pole, clip);
        Assert.Equal(201, mixed.Status);
        Assert.Equal(("inconclusive", "INCONSISTENT"), (mixed.Body.GetProperty("cv_state").GetString(), mixed.Body.GetProperty("video").GetProperty("reason_code").GetString()));
        Assert.Equal(JsonValueKind.Null, mixed.Body.GetProperty("confidence").ValueKind);
        Assert.Equal((3, 2), (mixed.Body.GetProperty("video").GetProperty("on_frames").GetInt32(), mixed.Body.GetProperty("video").GetProperty("off_frames").GetInt32()));

        // Every frame too bright: no frame can vote, and the reason is the brightness.
        poleHints.PerCall = _ => new("inconclusive", null, "model-test-v1", "model-test-v1+cq-legacy", "too_bright");
        poleHints.Calls = 0;
        var bright = await PutClip(order, pole, clip, time: Recently(9));
        Assert.Equal(201, bright.Status);
        Assert.Equal(("inconclusive", "TOO_BRIGHT", 5), (bright.Body.GetProperty("cv_state").GetString(), bright.Body.GetProperty("video").GetProperty("reason_code").GetString(),
            bright.Body.GetProperty("video").GetProperty("daylight_frames").GetInt32()));
        Assert.Equal(JsonValueKind.Null, bright.Body.GetProperty("video").GetProperty("confidence_min").ValueKind);
        // Nothing to confirm: a manager cannot publish an inconclusive clip.
        var manager = await PoleClient("manager");
        var decision = await manager.PostAsJsonAsync(PhotosPath(order) + $"/{bright.Body.GetProperty("photo_id").GetString()}/decision", new { decision = "confirm" });
        Assert.Equal(HttpStatusCode.Conflict, decision.StatusCode);
    }

    [Fact]
    public async Task When_the_model_is_unavailable_the_clip_is_still_kept_and_the_model_is_asked_only_once()
    {
        poleHints.PerCall = _ => null;
        var (order, pole) = await ClipTarget();
        var result = await PutClip(order, pole, await PoleVideoTestClips.MakeAsync(5));
        Assert.Equal(201, result.Status);
        Assert.Equal("unavailable", result.Body.GetProperty("ai_status").GetString());
        Assert.Equal(JsonValueKind.Null, result.Body.GetProperty("cv_state").ValueKind);
        var video = result.Body.GetProperty("video");
        Assert.Equal((5, 0, 0), (video.GetProperty("sampled_frames").GetInt32(), video.GetProperty("on_frames").GetInt32(), video.GetProperty("off_frames").GetInt32()));
        Assert.InRange(video.GetProperty("median_luma").GetDouble(), 0.05, 0.30);     // 0x20 grey: the brightness is kept without the model
        Assert.Equal(1, poleHints.Calls);
        var events = await Db(db => db.Set<AuditEvent>().IgnoreQueryFilters().Where(e => e.EntityId == order).Select(e => e.Action).ToArrayAsync());
        Assert.Equal([AuditAction.Submitted], events);                                // no analysis to record
    }

    [Fact]
    public async Task An_hdr_clip_is_accepted_and_flagged()
    {
        FramesSay("on");
        var (order, pole) = await ClipTarget();
        var result = await PutClip(order, pole, await PoleVideoTestClips.MakeAsync(4, encode: PoleVideoTestClips.Hlg));
        Assert.Equal(201, result.Status);
        Assert.True(result.Body.GetProperty("video").GetProperty("hdr").GetBoolean());
    }

    [Fact]
    public async Task A_work_order_closed_while_the_clip_is_arriving_gets_no_row()
    {
        FramesSay("on");
        var (order, pole) = await ClipTarget();
        poleStore.DuringWrite = async () =>
        {
            await Db(async db =>
            {
                using var fixture = db.EnterUnscopedSystemWriteBackdoor();
                var row = await db.Set<WorkOrder>().IgnoreQueryFilters().SingleAsync(w => w.WorkOrderId == order);
                row.WoStatus = WorkOrderStatus.Done; row.CompletedAt = DateTime.UtcNow; row.ReportNote = "done";
                await db.SaveChangesAsync(); return 0;
            });
            poleStore.DuringWrite = null;
        };
        var result = await PutClip(order, pole, await PoleVideoTestClips.MakeAsync(4));
        Assert.Equal((409, "WORK_ORDER_NOT_IN_PROGRESS"), (result.Status, result.Body.GetProperty("error").GetProperty("code").GetString()));
        Assert.Empty(await Rows(order));
    }

    [Fact]
    public async Task A_manager_decides_on_a_clip_like_on_a_photo_and_the_audit_says_it_came_from_an_unchecked_clip()
    {
        FramesSay("off", .9);
        var (order, pole) = await ClipTarget();
        var upload = await PutClip(order, pole, await PoleVideoTestClips.MakeAsync(4, encode: PoleVideoTestClips.Hlg));
        var photo = upload.Body.GetProperty("photo_id").GetString()!;
        using (await GetVideo(order, photo)) { }                 // PV-c3: a Manager confirms a clip only after opening it
        var decision = await PoleDecision(order, photo);
        Assert.True(decision.GetProperty("status_applied").GetBoolean());
        var fault = await Db(db => db.Set<Fault>().IgnoreQueryFilters().SingleAsync(f => f.OriginPolePhotoId == photo));
        Assert.Equal((FaultType.LampOut, DataSource.Simulated), (fault.FaultType, fault.DataSource));
        var confirmed = await Db(db => db.Set<AuditEvent>().IgnoreQueryFilters().SingleAsync(e => e.EntityId == order && e.Action == AuditAction.Confirmed));
        using var snapshot = JsonDocument.Parse(confirmed.AfterState!);
        var provenance = snapshot.RootElement.GetProperty("provenance");
        Assert.Equal("video", provenance.GetProperty("media").GetString());
        Assert.Equal(("not_verified", true), (provenance.GetProperty("video").GetProperty("capture_config").GetString(), provenance.GetProperty("video").GetProperty("hdr").GetBoolean()));
        Assert.True(provenance.GetProperty("video").GetProperty("extractor_version_id").GetInt64() > 0);
    }

    [Fact]
    public async Task The_frame_that_stands_for_the_clip_is_the_one_nearest_the_median_confidence_and_can_be_downloaded()
    {
        // Five frames with confidences .5 .9 .7 .6 .8: the median is .7 and the third frame holds it.
        double[] said = [.5, .9, .7, .6, .8];
        poleHints.PerCall = n => new("on", said[n], "model-test-v1", "model-test-v1+cq-legacy");
        var (order, pole) = await ClipTarget();
        var upload = await PutClip(order, pole, await PoleVideoTestClips.MakeAsync(5));
        Assert.Equal(.7, upload.Body.GetProperty("confidence").GetDouble());
        Assert.Equal((.5, .7, .9), (upload.Body.GetProperty("video").GetProperty("confidence_min").GetDouble(),
            upload.Body.GetProperty("video").GetProperty("confidence_median").GetDouble(), upload.Body.GetProperty("video").GetProperty("confidence_max").GetDouble()));
        var photo = upload.Body.GetProperty("photo_id").GetString()!;
        var original = await (await PoleClient("manager")).GetAsync(PhotosPath(order) + $"/{photo}/original");
        Assert.Equal(HttpStatusCode.OK, original.StatusCode);
        Assert.Equal(new byte[] { 0xFF, 0xD8, 0xFF }, (await original.Content.ReadAsByteArrayAsync())[..3]);
    }
}
