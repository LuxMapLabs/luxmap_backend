using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LuxMap.Modules.Assets.Entities;
using LuxMap.Modules.Faults.Entities;
using LuxMap.Modules.WorkOrders.Entities;
using LuxMap.Persistence;
using LuxMap.Persistence.Audit;
using LuxMap.Shared.Contracts.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace LuxMap.Api.Tests;

public partial class WorkOrderTests
{
    private readonly PhotoCommandProbe photoCommands = new();
    private async Task<JsonElement> PoleReview(string id, string photo, string? label = "on", string? note = "Kiểm tra tại cột",
        int expected = 200, string who = "a")
    {
        var response = await (await PoleClient(who)).PatchAsJsonAsync(PhotosPath(id) + $"/{photo}/review",
            new { final_cv_state = label, review_note = note });
        var text = await response.Content.ReadAsStringAsync();
        Assert.True((int)response.StatusCode == expected, $"Expected {expected}, got {(int)response.StatusCode}: {text}");
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    private static void NullReview(JsonElement item)
    {
        foreach (var field in new[] { "final_cv_state", "review_note", "reviewed_by", "reviewed_at" })
            Assert.Equal(JsonValueKind.Null, item.GetProperty(field).ValueKind);
    }

    [Fact]
    public async Task Unreviewed_photo_has_four_null_review_fields_on_every_response()
    {
        var pole = await PlantPole(.2); var id = await Plant(WorkOrderStatus.InProgress); var op = Guid.NewGuid().ToString();
        var upload = await PoleUpload(id, pole, op: op); Assert.Equal(201, upload.Status); NullReview(upload.Body);
        var photo = upload.Body.GetProperty("photo_id").GetString()!;
        NullReview((await PoleUpload(id, pole, op: op)).Body);
        var client = await PoleClient();
        NullReview(await client.GetFromJsonAsync<JsonElement>(PhotosPath(id) + "/" + photo));
        NullReview((await client.GetFromJsonAsync<JsonElement>(PhotosPath(id))).GetProperty("items")[0]);
        NullReview(await PoleDecision(id, photo));
    }

    [Theory]
    [InlineData("manager", 403)] [InlineData("superior", 403)] [InlineData("admin", 403)]
    [InlineData("b", 404)] [InlineData("anonymous", 401)] [InlineData("a", 200)]
    public async Task Pole_photo_review_is_assignee_only(string who, int expected)
    {
        var pole = await PlantPole(.2); var id = await Plant(WorkOrderStatus.InProgress);
        await PoleReview(id, await NewPolePhoto(id, pole), expected: expected, who: who);
    }

    private async Task PhotoOrderStatus(string id, WorkOrderStatus status)
    {
        await Db(async db =>
        {
            using var fixture = db.EnterUnscopedSystemWriteBackdoor();
            var row = await db.Set<WorkOrder>().IgnoreQueryFilters().SingleAsync(w => w.WorkOrderId == id);
            row.WoStatus = status;
            if (status is WorkOrderStatus.Done or WorkOrderStatus.Verified)
            { row.CompletedAt = DateTime.UtcNow; row.ReportNote = "Đã kiểm tra"; }
            row.ClosedAt = status is WorkOrderStatus.Verified or WorkOrderStatus.Cancelled ? DateTime.UtcNow : null;
            if (status == WorkOrderStatus.Open) { row.AssignedTo = null; row.AssignedAt = null; }
            await db.SaveChangesAsync(); return 0;
        });
    }

    [Theory]
    [InlineData(WorkOrderStatus.Open)] [InlineData(WorkOrderStatus.Assigned)] [InlineData(WorkOrderStatus.Done)]
    [InlineData(WorkOrderStatus.Verified)] [InlineData(WorkOrderStatus.Cancelled)]
    public async Task Pole_photo_review_requires_in_progress(WorkOrderStatus status)
    {
        var pole = await PlantPole(.2); var id = await Plant(WorkOrderStatus.InProgress);
        var photo = await NewPolePhoto(id, pole); await PhotoOrderStatus(id, status);
        Assert.Equal(status == WorkOrderStatus.Open ? "WORK_ORDER_NOT_FOUND" : "WORK_ORDER_NOT_IN_PROGRESS",
            Code(await PoleReview(id, photo, expected: status == WorkOrderStatus.Open ? 404 : 409)));
    }

    [Theory]
    [InlineData("confirm")] [InlineData("reject")]
    public async Task Decided_photo_cannot_be_reviewed(string decision)
    {
        var pole = await PlantPole(.2); var id = await Plant(WorkOrderStatus.InProgress);
        var photo = await NewPolePhoto(id, pole); await PoleDecision(id, photo, decision);
        Assert.Equal("POLE_PHOTO_ALREADY_DECIDED", Code(await PoleReview(id, photo, expected: 409)));
    }

    [Theory]
    [InlineData("on")] [InlineData("off")] [InlineData("inconclusive")] [InlineData(null)]
    public async Task Changed_or_missing_AI_label_requires_a_nonblank_note(string? ai)
    {
        poleHints.Result = ai is null ? null : new(ai, ai == "inconclusive" ? null : .91, "v1", "a1");
        var pole = await PlantPole(.2); var id = await Plant(WorkOrderStatus.InProgress);
        var photo = await NewPolePhoto(id, pole);
        foreach (var label in new[] { "on", "off" })
            foreach (var note in new string?[] { null, " \t\n " })
                await PoleReview(id, photo, label, note, expected: label == ai ? 200 : 400);
        await PoleReview(id, photo, "off", new string('x', 2001), expected: 400);
        foreach (var label in new string?[] { null, "ON", "inconclusive", "dim", "" })
            await PoleReview(id, photo, label, expected: 400);
        var client = await PoleClient();
        foreach (var json in new[] { "{}", "{\"final_cv_state\":1}", "{\"final_cv_state\":null}" })
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PatchAsync(PhotosPath(id) + $"/{photo}/review",
                new StringContent(json, System.Text.Encoding.UTF8, "application/json"))).StatusCode);
    }

    [Fact]
    public async Task Review_changes_no_AI_status_fault_or_objects_and_identical_review_is_a_noop()
    {
        var pole = await PlantPole(.2); var id = await Plant(WorkOrderStatus.InProgress);
        var op = Guid.NewGuid().ToString();
        var upload = await PoleUpload(id, pole, op: op); Assert.Equal(201, upload.Status);
        var photo = upload.Body.GetProperty("photo_id").GetString()!;
        var original = await (await PoleClient()).GetFromJsonAsync<JsonElement>(PhotosPath(id) + "/" + photo);
        var first = await PoleReview(id, photo, note: "  Thực tế sáng  ");
        Assert.Equal("Thực tế sáng", first.GetProperty("review_note").GetString());
        Assert.Equal(users["a"].UserId, first.GetProperty("reviewed_by").GetString());
        var retry = await PoleReview(id, photo, note: "Thực tế sáng");
        Assert.Equal(first.GetRawText(), retry.GetRawText());
        var back = await PoleReview(id, photo, "off", note: null);
        Assert.Equal("off", back.GetProperty("final_cv_state").GetString());
        Assert.Equal(JsonValueKind.Null, back.GetProperty("review_note").ValueKind);
        foreach (var field in new[] { "cv_state", "confidence", "model_version", "artifact_version", "analyzed_at" })
            Assert.Equal(original.GetProperty(field).GetRawText(), back.GetProperty(field).GetRawText());
        var replay = await PoleUpload(id, pole, op: op); Assert.Equal(200, replay.Status);
        Assert.Equal(back.GetRawText(), replay.Body.GetRawText());
        Assert.Equal(1, poleHints.Calls); Assert.Equal(2, poleStore.Objects.Count);
        Assert.Equal(0, await Db(db => db.Set<PoleCurrentStatus>().IgnoreQueryFilters().CountAsync(p => p.PoleId == pole)));
        Assert.Equal(0, await Db(db => db.Set<Fault>().IgnoreQueryFilters().CountAsync(p => p.PoleId == pole)));
        var events = await Db(db => db.Set<AuditEvent>().IgnoreQueryFilters().Where(e => e.EntityId == id && e.Action == AuditAction.DetailsChanged).OrderBy(e => e.AuditId).ToArrayAsync());
        Assert.Equal(2, events.Length);
        Assert.Contains("Thực tế sáng", JsonDocument.Parse(events[1].BeforeState!).RootElement.GetProperty("review_note").GetString());
        Assert.Equal("off", JsonDocument.Parse(events[1].AfterState!).RootElement.GetProperty("final_cv_state").GetString());
        // Agreeing with AI retains the original provenance, score and artifact.
        await PoleDecision(id, photo);
        var fault = await Db(db => db.Set<Fault>().IgnoreQueryFilters().SingleAsync(f => f.OriginPolePhotoId == photo));
        Assert.Equal(SourceChannel.Cv, fault.SourceChannel); Assert.Equal(.91, fault.StatusConfidence);
        Assert.Equal("model-test-v1+cq-legacy", fault.DetectionModelVersion); Assert.Null(fault.ReportedBy);
        Assert.All(poleSave.EventCounts, n => Assert.Equal(1, n));
    }

    [Theory]
    [InlineData("on", "off")] [InlineData("off", "on")]
    [InlineData("inconclusive", "off")] [InlineData(null, "off")]
    [InlineData("inconclusive", "on")] [InlineData(null, "on")]
    public async Task Human_label_is_published_without_borrowing_AI_confidence_and_with_two_actors(string? ai, string final)
    {
        poleHints.Result = ai is null ? null : new(ai, ai == "inconclusive" ? null : .91, "v1", "a1");
        var pole = await PlantPole(.2); var id = await Plant(WorkOrderStatus.InProgress);
        var photo = await NewPolePhoto(id, pole); await PoleReview(id, photo, final);
        Assert.True((await PoleDecision(id, photo)).GetProperty("status_applied").GetBoolean());
        var current = await Db(db => db.Set<PoleCurrentStatus>().IgnoreQueryFilters().SingleAsync(p => p.PoleId == pole));
        Assert.Equal(final == "off" ? FixtureStatus.Out : FixtureStatus.Normal, current.FixtureStatus);
        Assert.Null(current.StatusConfidence); Assert.Null(current.LastSweepId); Assert.Null(current.LastRunId);
        var faults = await Db(db => db.Set<Fault>().IgnoreQueryFilters().Where(f => f.PoleId == pole).ToArrayAsync());
        if (final == "off")
        {
            var fault = Assert.Single(faults); Assert.Equal(SourceChannel.FieldReport, fault.SourceChannel);
            Assert.Equal(users["a"].UserId, fault.ReportedBy); Assert.Equal(users["manager"].UserId, fault.ConfirmedBy);
            Assert.Null(fault.StatusConfidence); Assert.Null(fault.DetectionModelVersion); Assert.Equal(FaultStatus.Confirmed, fault.FaultStatus);
        }
        else Assert.Empty(faults);
        var events = await Db(db => db.Set<AuditEvent>().IgnoreQueryFilters().Where(e => e.EntityId == id || faults.Select(f => f.FaultId).Contains(e.EntityId)).ToArrayAsync());
        foreach (var entry in events.Where(e => e.Action is AuditAction.Applied or AuditAction.Created or AuditAction.Confirmed))
        {
            var provenance = JsonDocument.Parse(entry.AfterState!).RootElement.GetProperty("provenance");
            Assert.Equal("engineer", provenance.GetProperty("label_source").GetString());
            Assert.Equal(final, provenance.GetProperty("effective_cv_state").GetString());
            Assert.Equal(users["a"].UserId, provenance.GetProperty("reviewed_by").GetString());
            Assert.Equal(JsonSerializer.Serialize(ai), provenance.GetProperty("ai_hint").GetProperty("cv_state").GetRawText());
            Assert.Equal(users["manager"].UserId, entry.ActorUserId);
        }
    }

    [Fact]
    public async Task Human_ON_preserves_dim_and_its_previous_score_but_advances_the_clock()
    {
        var pole = await PlantPole(.2, status: FixtureStatus.Dim, confidence: .6); var id = await Plant(WorkOrderStatus.InProgress);
        var photo = await NewPolePhoto(id, pole); await PoleReview(id, photo);
        Assert.Equal("classification_preserved", (await PoleDecision(id, photo)).GetProperty("status_reason").GetString());
        var current = await Db(db => db.Set<PoleCurrentStatus>().IgnoreQueryFilters().SingleAsync(p => p.PoleId == pole));
        Assert.Equal(FixtureStatus.Dim, current.FixtureStatus); Assert.Equal(.6, current.StatusConfidence);
        var older = await NewPolePhoto(id, pole, "2026-10-04T14:00:00Z"); await PoleReview(id, older, "off");
        Assert.Equal("not_newer", (await PoleDecision(id, older)).GetProperty("status_reason").GetString());
        Assert.Equal(0, await Db(db => db.Set<Fault>().IgnoreQueryFilters().CountAsync(f => f.PoleId == pole)));
    }

    [Theory]
    [InlineData(SourceChannel.Cv)] [InlineData(SourceChannel.FieldReport)]
    public async Task Human_OFF_does_not_duplicate_an_effective_open_lamp_out_from_either_source(SourceChannel source)
    {
        var pole = await PlantPole(.2); var faultId = await PlantPoleFault(pole, FaultStatus.Confirmed);
        await Db(async db =>
        {
            using var fixture = db.EnterUnscopedSystemWriteBackdoor();
            var fault = await db.Set<Fault>().IgnoreQueryFilters().SingleAsync(f => f.FaultId == faultId);
            fault.FaultType = FaultType.LampDim; fault.OverrideFaultType = FaultType.LampOut; fault.SourceChannel = source;
            await db.SaveChangesAsync(); return 0;
        });
        poleHints.Result = new("on", .91, "v1", "a1");
        var id = await Plant(WorkOrderStatus.InProgress); var photo = await NewPolePhoto(id, pole);
        await PoleReview(id, photo, "off"); await PoleDecision(id, photo);
        Assert.Equal(1, await Db(db => db.Set<Fault>().IgnoreQueryFilters().CountAsync(f => f.PoleId == pole)));
    }

    [Fact(Skip = "ACCT-1 / D-6b: requires a multi-commune account; awaiting My review.")]
    public async Task Review_checks_parent_membership_and_the_photos_own_commune()
    {
        var pole = await PlantPole(.2, commune: foreign); var id = await Plant(WorkOrderStatus.InProgress);
        await Db(async db =>
        {
            using var fixture = db.EnterUnscopedSystemWriteBackdoor();
            db.Add(new LuxMap.Modules.Identity.Entities.AppUserCommune { UserId = users["a"].UserId, CommuneId = foreign });
            await db.SaveChangesAsync(); return 0;
        });
        var photo = await NewPolePhoto(id, pole); var other = await Plant(WorkOrderStatus.InProgress);
        Assert.Equal("POLE_PHOTO_NOT_FOUND", Code(await PoleReview(other, photo, expected: 404)));
        // Parent remains visible in home; the photo's foreign commune is separately revoked.
        await Db(async db =>
        {
            await db.Database.ExecuteSqlAsync($"DELETE FROM app_user_commune WHERE user_id = {users["a"].UserId} AND commune_id = {foreign}");
            return 0;
        });
        // Commune claims belong to the access-token snapshot: sign in again after revocation.
        polePhotoClients.Remove("a", out var old); old!.Dispose();
        Assert.Equal("POLE_PHOTO_NOT_FOUND", Code(await PoleReview(id, photo, expected: 404)));
        var outside = await Plant(WorkOrderStatus.InProgress, commune: foreign);
        Assert.Equal("WORK_ORDER_NOT_FOUND", Code(await PoleReview(outside, photo, expected: 404)));
    }

    private sealed class PhotoCommandProbe : DbCommandInterceptor
    {
        public bool Enabled;
        public readonly List<(string Sql, DbTransaction? Transaction, bool Tracked)> Commands = [];
        public TaskCompletionSource? ParentRequested;
        public TaskCompletionSource? Locked;
        public TaskCompletionSource? Release;
        private void Capture(DbCommand command, CommandEventData data)
        {
            if (Enabled) Commands.Add((command.CommandText, command.Transaction,
                data.Context!.ChangeTracker.Entries<WorkOrderPolePhoto>().Any()));
        }
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData data,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            Capture(command, data);
            if (command.CommandText.StartsWith("SELECT 1 FROM work_order WHERE", StringComparison.Ordinal)) ParentRequested?.TrySetResult();
            return ValueTask.FromResult(result);
        }
        public override async ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData data,
            int result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.StartsWith("SELECT 1 FROM work_order_pole_photo", StringComparison.Ordinal) && Locked is { } locked)
            {
                var release = Release!; Locked = null; locked.TrySetResult(); await release.Task.WaitAsync(cancellationToken);
            }
            return result;
        }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData data,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        { Capture(command, data); return ValueTask.FromResult(result); }
    }

    [Fact]
    public async Task Review_lock_commands_precede_the_tracked_photo_read_in_order()
    {
        var pole = await PlantPole(.2); var id = await Plant(WorkOrderStatus.InProgress);
        var photo = await NewPolePhoto(id, pole); photoCommands.Enabled = true;
        await PoleReview(id, photo);
        var commands = photoCommands.Commands;
        var locks = commands.Where(c => c.Sql.Contains("FOR UPDATE", StringComparison.Ordinal)).ToArray();
        Assert.Equal(3, locks.Length);
        Assert.StartsWith("SELECT 1 FROM work_order WHERE", locks[0].Sql);
        Assert.StartsWith("SELECT 1 FROM pole WHERE", locks[1].Sql);
        Assert.StartsWith("SELECT 1 FROM work_order_pole_photo WHERE", locks[2].Sql);
        Assert.NotNull(locks[0].Transaction); Assert.All(locks, l => { Assert.Same(locks[0].Transaction, l.Transaction); Assert.False(l.Tracked); });
        var afterLock = commands.Skip(commands.FindIndex(c => c.Sql == locks[2].Sql) + 1).First(c => c.Sql.Contains("FROM work_order_pole_photo", StringComparison.Ordinal));
        Assert.Same(locks[0].Transaction, afterLock.Transaction); Assert.False(afterLock.Tracked);
    }

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public async Task Review_and_decision_serialize_and_publish_the_latest_locked_review(bool reviewFirst)
    {
        var pole = await PlantPole(.2); var id = await Plant(WorkOrderStatus.InProgress);
        var photo = await NewPolePhoto(id, pole); await PoleClient("manager");
        var locked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        photoCommands.Locked = locked; photoCommands.Release = release;
        var first = reviewFirst ? PoleReview(id, photo) : PoleDecision(id, photo);
        Task<JsonElement>? second = null;
        try
        {
            await locked.Task.WaitAsync(TimeSpan.FromSeconds(10));
            second = reviewFirst ? PoleDecision(id, photo) : PoleReview(id, photo, expected: 409);
            await Task.Delay(200); Assert.False(second.IsCompleted);
        }
        finally { release.TrySetResult(); }
        await first; var result = await second!;
        if (reviewFirst)
        {
            Assert.Equal("on", result.GetProperty("final_cv_state").GetString());
            Assert.Equal(FixtureStatus.Normal, await Db(db => db.Set<PoleCurrentStatus>().IgnoreQueryFilters().Where(p => p.PoleId == pole).Select(p => p.FixtureStatus).SingleAsync()));
        }
        else Assert.Equal("POLE_PHOTO_ALREADY_DECIDED", Code(result));
    }

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public async Task Review_rechecks_assignment_and_status_after_the_parent_lock(bool reassign)
    {
        var pole = await PlantPole(.2); var id = await Plant(WorkOrderStatus.InProgress);
        var photo = await NewPolePhoto(id, pole);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LuxMapDbContext>();
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlAsync($"SELECT 1 FROM work_order WHERE work_order_id = {id} FOR UPDATE");
        photoCommands.ParentRequested = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var review = PoleReview(id, photo, expected: reassign ? 404 : 409);
        try
        {
            await photoCommands.ParentRequested.Task.WaitAsync(TimeSpan.FromSeconds(10));
            if (reassign)
                await db.Database.ExecuteSqlAsync($"UPDATE work_order SET assigned_to = {users["b"].UserId} WHERE work_order_id = {id}");
            else
                await db.Database.ExecuteSqlAsync($"UPDATE work_order SET wo_status = 'done', completed_at = now(), report_note = 'done' WHERE work_order_id = {id}");
        }
        finally { await tx.CommitAsync(); }
        Assert.Equal(reassign ? "WORK_ORDER_NOT_FOUND" : "WORK_ORDER_NOT_IN_PROGRESS", Code(await review));
    }

    [Theory]
    [InlineData(AuditAction.DetailsChanged)] [InlineData(AuditAction.Confirmed)]
    public async Task Review_and_decision_failures_roll_back_their_whole_transaction(AuditAction fail)
    {
        poleHints.Result = new("on", .91, "v1", "a1");
        var pole = await PlantPole(.2); var id = await Plant(WorkOrderStatus.InProgress);
        var photo = await NewPolePhoto(id, pole);
        if (fail == AuditAction.Confirmed) await PoleReview(id, photo, "off");
        var count = await Db(db => db.Set<AuditEvent>().IgnoreQueryFilters().CountAsync(e => e.CommuneId == home));
        poleSave.FailAction = fail;
        if (fail == AuditAction.DetailsChanged) await PoleReview(id, photo, "off", expected: 500);
        else await PoleDecision(id, photo, expected: 500);
        var row = await Db(db => db.Set<WorkOrderPolePhoto>().IgnoreQueryFilters().SingleAsync(p => p.PhotoId == photo));
        Assert.Null(row.Decision); if (fail == AuditAction.DetailsChanged) Assert.Null(row.FinalCvState);
        Assert.Equal(count, await Db(db => db.Set<AuditEvent>().IgnoreQueryFilters().CountAsync(e => e.CommuneId == home)));
        Assert.Equal(0, await Db(db => db.Set<PoleCurrentStatus>().IgnoreQueryFilters().CountAsync(p => p.PoleId == pole)));
        Assert.Equal(0, await Db(db => db.Set<Fault>().IgnoreQueryFilters().CountAsync(f => f.PoleId == pole)));
    }

    [Fact]
    public async Task Photo_review_checks_and_reviewer_FK_are_enforced_by_Postgres()
    {
        var pole = await PlantPole(.2); var id = await Plant(WorkOrderStatus.InProgress);
        var photo = await NewPolePhoto(id, pole);
        var cases = new[]
        {
            ("final_cv_state = 'on'", "ck_pole_photo_review_complete"),
            ("review_note = 'orphan'", "ck_pole_photo_review_complete"),
            ("final_cv_state = 'on', reviewed_by = {1}, reviewed_at = now()", "ck_pole_photo_review_note_required"),
            ("final_cv_state = 'dim', reviewed_by = {1}, reviewed_at = now(), review_note = 'x'", "ck_pole_photo_review_complete"),
            ("final_cv_state = 'on', reviewed_by = {1}, reviewed_at = now(), review_note = E' \\t\\n'", "ck_pole_photo_review_note"),
            ("final_cv_state = 'on', reviewed_by = {1}, reviewed_at = now(), review_note = chr(11)", "ck_pole_photo_review_note"),
            ("final_cv_state = 'on', reviewed_by = {1}, reviewed_at = now(), review_note = repeat('x', 2001)", "ck_pole_photo_review_note"),
            ("final_cv_state = 'off', reviewed_by = 'USER-missing', reviewed_at = now()", "fk_pole_photo_reviewed_by"),
        };
        foreach (var (set, constraint) in cases)
            await Db(async db =>
            {
                // SET clauses above are trusted test literals; values remain parameters.
                var sql = "UPDATE work_order_pole_photo SET " + set + " WHERE photo_id = {0}";
                var error = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(sql, photo, users["a"].UserId));
                Assert.Equal(constraint, error.ConstraintName); return 0;
            });
        await PoleReview(id, photo, "on", "v");
        await Db(async db =>
        {
            var error = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlAsync(
                $"UPDATE work_order_pole_photo SET final_cv_state = NULL WHERE photo_id = {photo}"));
            Assert.Equal("ck_pole_photo_review_complete", error.ConstraintName); return 0;
        });
    }
    [Fact]
    public async Task Human_photo_fault_DB_check_requires_reporter_and_excludes_AI_score_and_other_origins()
    {
        poleHints.Result = new("on", .91, "v1", "a1");
        var pole = await PlantPole(.2); var id = await Plant(WorkOrderStatus.InProgress);
        var photo = await NewPolePhoto(id, pole); await PoleReview(id, photo, "off"); await PoleDecision(id, photo);
        foreach (var set in new[] { "reported_by = NULL", "status_confidence = .91", "detection_model_version = 'a1'", "source_channel = 'iot'" })
            await Db(async db =>
            {
                var sql = "UPDATE fault SET " + set + " WHERE origin_pole_photo_id = {0}";
                var error = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(sql, photo));
                Assert.Equal("ck_fault_pole_photo_source", error.ConstraintName); return 0;
            });
    }

}
