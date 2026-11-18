using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LuxMap.Modules.WorkOrders.Entities;
using LuxMap.Persistence.Audit;
using LuxMap.Shared.Contracts.Enums;
using LuxMap.Shared.Storage;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LuxMap.Api.Tests;

// Shares the pole-photo host, isolated accounts, teardown and AssetDatabaseCollection of WorkOrderTests.
public partial class WorkOrderTests
{
    private async Task<JsonElement> PoleWithdraw(string id, string photo, int expected = 204, string who = "a")
    {
        using var response = await (await PoleClient(who)).DeleteAsync(PhotosPath(id) + "/" + photo);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True((int)response.StatusCode == expected, $"Expected {expected}, got {(int)response.StatusCode}: {text}");
        if (expected == 204) { Assert.Empty(text); return default; }
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    private Task<AuditEvent[]> WithdrawalEvents(string id)
        => Db(db => db.Set<AuditEvent>().IgnoreQueryFilters()
            .Where(e => e.EntityId == id && e.Action == AuditAction.Deleted).ToArrayAsync());

    [Fact]
    public async Task Withdrawal_hides_the_photo_from_all_routes_but_keeps_its_row_AI_objects_and_target()
    {
        var pole = await PlantPole(.2); var id = await Plant(WorkOrderStatus.InProgress);
        var photo = await NewPolePhoto(id, pole);
        await PoleReview(id, photo, "on");
        var before = Assert.Single(await Rows(id));
        var objects = poleStore.Objects.ToDictionary();
        await PoleWithdraw(id, photo);
        var after = Assert.Single(await Rows(id));
        Assert.NotNull(after.WithdrawnAt); Assert.Equal(users["a"].UserId, after.WithdrawnBy);
        Assert.Equal(before.CvState, after.CvState); Assert.Equal(before.FinalCvState, after.FinalCvState);
        Assert.Equal(before.ArtifactVersion, after.ArtifactVersion); Assert.Null(after.Decision);
        Assert.Equal(objects.Count, poleStore.Objects.Count);
        foreach (var (key, bytes) in objects) Assert.Equal(bytes, poleStore.Objects[key]);
        var manager = await PoleClient("manager");
        var list = await manager.GetFromJsonAsync<JsonElement>(PhotosPath(id));
        Assert.Equal(0, list.GetProperty("total").GetInt32()); Assert.Empty(list.GetProperty("items").EnumerateArray());
        var filtered = await manager.GetFromJsonAsync<JsonElement>(PhotosPath(id) + "?pole_id=" + pole);
        Assert.Equal(0, filtered.GetProperty("total").GetInt32());
        foreach (var suffix in new[] { "", "/thumbnail", "/original", "/video" })
        {
            using var response = await manager.GetAsync(PhotosPath(id) + "/" + photo + suffix);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal("POLE_PHOTO_NOT_FOUND", Code(await response.Content.ReadFromJsonAsync<JsonElement>()));
        }
        Assert.Equal("POLE_PHOTO_NOT_FOUND", Code(await PoleReview(id, photo, expected: 404)));
        Assert.Equal("POLE_PHOTO_NOT_FOUND", Code(await PoleDecision(id, photo, expected: 404)));
        var entry = Assert.Single(await WithdrawalEvents(id));
        Assert.Equal(AuditEntityType.WorkOrder, entry.EntityType); Assert.Equal(home, entry.CommuneId);
        Assert.Equal(users["a"].UserId, entry.ActorUserId);
        foreach (var snapshot in new[] { entry.BeforeState!, entry.AfterState! })
        {
            var state = JsonDocument.Parse(snapshot).RootElement;
            Assert.Equal(photo, state.GetProperty("photo_id").GetString());
            Assert.Equal(pole, state.GetProperty("pole_id").GetString());
            Assert.Equal("off", state.GetProperty("cv_state").GetString());
            Assert.Equal("on", state.GetProperty("final_cv_state").GetString());
            Assert.False(state.GetProperty("has_video").GetBoolean());
        }
        Assert.Equal(after.WithdrawnAt, entry.OccurredAt);
        Assert.Equal(201, (await PoleUpload(id, pole)).Status); // Target is still available for a new capture.
        Assert.All(poleSave.EventCounts, count => Assert.Equal(1, count));
    }

    [Fact]
    public async Task Withdrawal_retry_is_a_noop_even_after_done_but_still_checks_assignment()
    {
        var pole = await PlantPole(.2); var id = await Plant(WorkOrderStatus.InProgress);
        var photo = await NewPolePhoto(id, pole);
        await PoleWithdraw(id, photo);
        var first = Assert.Single(await Rows(id));
        await PoleWithdraw(id, photo);
        await PhotoOrderStatus(id, WorkOrderStatus.Done);
        await PoleWithdraw(id, photo);
        Assert.Equal("WORK_ORDER_NOT_FOUND", Code(await PoleWithdraw(id, photo, 404, "b")));
        var final = Assert.Single(await Rows(id));
        Assert.Equal(first.WithdrawnAt, final.WithdrawnAt); Assert.Equal(first.WithdrawnBy, final.WithdrawnBy);
        Assert.Single(await WithdrawalEvents(id));
    }

    [Theory]
    [InlineData("manager", 403)] [InlineData("superior", 403)] [InlineData("admin", 403)]
    [InlineData("b", 404)] [InlineData("anonymous", 401)]
    public async Task Withdrawal_requires_the_assigned_engineer(string who, int expected)
    {
        var pole = await PlantPole(.2); var id = await Plant(WorkOrderStatus.InProgress);
        var photo = await NewPolePhoto(id, pole);
        var error = await PoleWithdraw(id, photo, expected, who);
        Assert.Equal(expected == 404 ? "WORK_ORDER_NOT_FOUND" : expected == 403 ? "ROLE_FORBIDDEN" : "UNAUTHENTICATED", Code(error));
        Assert.Empty(await WithdrawalEvents(id));
    }

    [Fact]
    public async Task Withdrawal_checks_parent_then_membership_then_status_then_decision()
    {
        var pole = await PlantPole(.2); var id = await Plant(WorkOrderStatus.InProgress);
        var photo = await NewPolePhoto(id, pole); var other = await Plant(WorkOrderStatus.InProgress);
        var outside = await Plant(WorkOrderStatus.InProgress, commune: foreign);
        Assert.Equal("WORK_ORDER_NOT_FOUND", Code(await PoleWithdraw(outside, photo, 404)));
        Assert.Equal("POLE_PHOTO_NOT_FOUND", Code(await PoleWithdraw(other, photo, 404)));
        await PhotoOrderStatus(other, WorkOrderStatus.Done);
        Assert.Equal("POLE_PHOTO_NOT_FOUND", Code(await PoleWithdraw(other, photo, 404)));
        await PhotoOrderStatus(id, WorkOrderStatus.Done);
        Assert.Equal("WORK_ORDER_NOT_IN_PROGRESS", Code(await PoleWithdraw(id, photo, 409)));
        Assert.Empty(await WithdrawalEvents(id));
    }

    [Theory]
    [InlineData("confirm")] [InlineData("reject")]
    public async Task A_decided_photo_cannot_be_withdrawn(string decision)
    {
        var pole = await PlantPole(.2); var id = await Plant(WorkOrderStatus.InProgress);
        var photo = await NewPolePhoto(id, pole); await PoleDecision(id, photo, decision);
        Assert.Equal("POLE_PHOTO_ALREADY_DECIDED", Code(await PoleWithdraw(id, photo, 409)));
        await PhotoOrderStatus(id, WorkOrderStatus.Done);
        Assert.Equal("WORK_ORDER_NOT_IN_PROGRESS", Code(await PoleWithdraw(id, photo, 409)));
        Assert.Empty(await WithdrawalEvents(id));
    }

    [Fact]
    public async Task A_withdrawn_clip_cannot_stream_or_record_a_view_and_its_bytes_are_retained()
    {
        var (id, photo, bytes, key) = await NewPoleVideo();
        await PoleWithdraw(id, photo);
        using var response = await GetVideo(id, photo, range: "bytes=0-1");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("POLE_PHOTO_NOT_FOUND", Code(await response.Content.ReadFromJsonAsync<JsonElement>()));
        var row = Assert.Single(await Rows(id)); Assert.Null(row.VideoViewedBy); Assert.Null(row.VideoViewedAt);
        Assert.Empty(await ViewedEvents(id)); Assert.Equal(bytes, poleStore.Objects[(StorageBucket.Video, key)]);
        Assert.True(JsonDocument.Parse(Assert.Single(await WithdrawalEvents(id)).AfterState!).RootElement.GetProperty("has_video").GetBoolean());
    }

    [Fact]
    public async Task Both_upload_routes_reject_replay_of_a_withdrawn_operation_even_after_done_without_new_objects_or_AI()
    {
        var pole = await PlantPole(.2); var id = await Plant(WorkOrderStatus.InProgress); var op = Guid.NewGuid();
        var first = await PoleUpload(id, pole, op: op.ToString()); Assert.Equal(201, first.Status);
        var photo = first.Body.GetProperty("photo_id").GetString()!;
        await PoleWithdraw(id, photo); await PhotoOrderStatus(id, WorkOrderStatus.Done);
        var (calls, objects) = (poleHints.Calls, poleStore.Objects.Count);
        var photoReplay = await PoleUpload(id, pole, op: op.ToString());
        Assert.Equal(409, photoReplay.Status); Assert.Equal("POLE_PHOTO_WITHDRAWN", Code(photoReplay.Body));
        Assert.Equal(photo, photoReplay.Body.GetProperty("error").GetProperty("details").GetProperty("photo_id").GetString());
        // Shared operation namespace: a clip request using the withdrawn photo's key must also require a new key, before reading MP4.
        var videoReplay = await PutClip(id, pole, [1, 2, 3], op: op);
        Assert.Equal(409, videoReplay.Status); Assert.Equal("POLE_PHOTO_WITHDRAWN", Code(videoReplay.Body));
        Assert.Equal(photo, videoReplay.Body.GetProperty("error").GetProperty("details").GetProperty("photo_id").GetString());
        Assert.Equal((calls, objects), (poleHints.Calls, poleStore.Objects.Count));
        Assert.Single(await Rows(id)); Assert.Single(await WithdrawalEvents(id));
    }

    [Fact]
    public async Task Upload_replay_of_an_actual_withdrawn_clip_returns_the_new_error()
    {
        FramesSay("on"); var (id, pole) = await ClipTarget(); var op = Guid.NewGuid();
        var bytes = await PoleVideoTestClips.MakeAsync(4); var time = Recently();
        var first = await PutClip(id, pole, bytes, op: op, time: time); Assert.Equal(201, first.Status);
        var photo = first.Body.GetProperty("photo_id").GetString()!; await PoleWithdraw(id, photo);
        var (calls, objects) = (poleHints.Calls, poleStore.Objects.Count);
        var replay = await PutClip(id, pole, bytes, op: op, time: time);
        Assert.Equal(409, replay.Status); Assert.Equal("POLE_PHOTO_WITHDRAWN", Code(replay.Body));
        Assert.Equal(photo, replay.Body.GetProperty("error").GetProperty("details").GetProperty("photo_id").GetString());
        Assert.Equal((calls, objects), (poleHints.Calls, poleStore.Objects.Count));
    }

    [Theory]
    [InlineData("withdraw")] [InlineData("review")] [InlineData("decision")]
    public async Task Concurrent_withdrawal_rechecks_the_photo_after_locks(string competing)
    {
        var pole = await PlantPole(.2); var id = await Plant(WorkOrderStatus.InProgress);
        var photo = await NewPolePhoto(id, pole); await PoleClient("manager");
        var locked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        photoCommands.Locked = locked; photoCommands.Release = release;
        photoCommands.Enabled = true;
        var first = PoleWithdraw(id, photo);
        Task<JsonElement>? second = null;
        try
        {
            await locked.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var parent = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            photoCommands.ParentRequested = parent;
            second = competing switch
            {
                "review" => PoleReview(id, photo, expected: 404),
                "decision" => PoleDecision(id, photo, expected: 404),
                _ => PoleWithdraw(id, photo),
            };
            await parent.Task.WaitAsync(TimeSpan.FromSeconds(10)); Assert.False(second.IsCompleted);
        }
        finally { release.TrySetResult(); }
        await first; await second!;
        Assert.Single(await WithdrawalEvents(id));
        Assert.Null(Assert.Single(await Rows(id)).Decision);
        var locks = photoCommands.Commands.Where(c => c.Sql.Contains("FOR UPDATE", StringComparison.Ordinal)).Take(3).ToArray();
        Assert.Equal(3, locks.Length);
        Assert.StartsWith("SELECT 1 FROM work_order WHERE", locks[0].Sql);
        Assert.StartsWith("SELECT 1 FROM pole WHERE", locks[1].Sql);
        Assert.StartsWith("SELECT 1 FROM work_order_pole_photo WHERE", locks[2].Sql);
        Assert.NotNull(locks[0].Transaction);
        Assert.All(locks, c => { Assert.Same(locks[0].Transaction, c.Transaction); Assert.False(c.Tracked); });
    }

    [Theory]
    [InlineData("withdrawn_at = now()")]
    [InlineData("withdrawn_by = {1}")]
    [InlineData("withdrawn_at = now(), withdrawn_by = {1}, decision = 'reject', decided_by = {1}, decided_at = now(), status_reason = 'rejected'")]
    public async Task Withdrawal_DB_check_rejects_partial_withdrawal_and_decision(string set)
    {
        var pole = await PlantPole(.2); var id = await Plant(WorkOrderStatus.InProgress);
        var photo = await NewPolePhoto(id, pole);
        await Db(async db =>
        {
            var sql = "UPDATE work_order_pole_photo SET " + set + " WHERE photo_id = {0}";
            var error = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(sql, photo, users["a"].UserId));
            Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState);
            Assert.Equal("ck_pole_photo_withdrawal", error.ConstraintName); return 0;
        });
    }

    [Fact]
    public async Task Withdrawal_save_failure_leaves_the_photo_visible_and_no_deleted_audit()
    {
        var pole = await PlantPole(.2); var id = await Plant(WorkOrderStatus.InProgress);
        var photo = await NewPolePhoto(id, pole); poleSave.FailAction = AuditAction.Deleted;
        await PoleWithdraw(id, photo, 500);
        var row = Assert.Single(await Rows(id)); Assert.Null(row.WithdrawnAt); Assert.Null(row.WithdrawnBy);
        Assert.Empty(await WithdrawalEvents(id));
        using var response = await (await PoleClient()).GetAsync(PhotosPath(id) + "/" + photo);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task A_decision_winning_the_lock_prevents_a_concurrent_withdrawal()
    {
        var pole = await PlantPole(.2); var id = await Plant(WorkOrderStatus.InProgress);
        var photo = await NewPolePhoto(id, pole); await PoleClient("manager");
        var locked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        photoCommands.Locked = locked; photoCommands.Release = release;
        var first = PoleDecision(id, photo, "reject"); Task<JsonElement>? second = null;
        try
        {
            await locked.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var parent = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            photoCommands.ParentRequested = parent;
            second = PoleWithdraw(id, photo, 409);
            await parent.Task.WaitAsync(TimeSpan.FromSeconds(10)); Assert.False(second.IsCompleted);
        }
        finally { release.TrySetResult(); }
        await first;
        Assert.Equal("POLE_PHOTO_ALREADY_DECIDED", Code(await second!));
        Assert.Null(Assert.Single(await Rows(id)).WithdrawnAt); Assert.Empty(await WithdrawalEvents(id));
    }
}
