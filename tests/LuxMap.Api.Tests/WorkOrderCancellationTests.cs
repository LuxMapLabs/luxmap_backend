using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LuxMap.Modules.Notifications.Entities;
using LuxMap.Modules.WorkOrders;
using LuxMap.Modules.WorkOrders.Entities;
using LuxMap.Persistence;
using LuxMap.Persistence.Audit;
using LuxMap.Shared.Contracts.Enums;
using LuxMap.Shared.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LuxMap.Api.Tests;

public partial class WorkOrderTests
{
    private Task<JsonElement> RequestCancellation(string id, string? reason = "Xe hỏng, cần giao lại")
        => Send("a", "POST", $"/{id}/request-cancellation", new { reason }, 200);
    private Task<JsonElement> RejectCancellation(string id, string? reason = "Vui lòng tiếp tục lịch đã giao")
        => Send("manager", "POST", $"/{id}/reject-cancellation", new { reject_reason = reason }, 200);

    private static void CancellationState(JsonElement wo, string state, string? reason = null, string? rejection = null)
    {
        Assert.Equal(state, wo.GetProperty("cancellation_request_status").GetString());
        Assert.Equal(reason, wo.GetProperty("cancellation_request_note").GetString());
        Assert.Equal(rejection, wo.GetProperty("cancellation_reject_note").GetString());
        Assert.Equal(state == "none", wo.GetProperty("cancellation_requested_at").ValueKind == JsonValueKind.Null);
        Assert.Equal(state != "rejected", wo.GetProperty("cancellation_rejected_at").ValueKind == JsonValueKind.Null);
    }

    [Fact]
    public async Task Cancellation_substate_has_only_approved_edges_and_current_decision_semantics()
    {
        var id = await Create(assigned: users["a"].UserId);
        var initial = await Send("a", "GET", $"/{id}", null, 200);
        CancellationState(initial, "none");
        Assert.Contains("request_cancellation", initial.GetProperty("allowed_actions").EnumerateArray().Select(x => x.GetString()));
        await Send("manager", "POST", $"/{id}/reject-cancellation", new { reject_reason = "No request" }, 409, "INVALID_STATE_TRANSITION");
        var pending = await RequestCancellation(id, "  Xe hỏng  ");
        CancellationState(pending, "pending", "Xe hỏng");
        Assert.Equal("assigned", pending.GetProperty("wo_status").GetString());
        Assert.DoesNotContain("request_cancellation", pending.GetProperty("allowed_actions").EnumerateArray().Select(x => x.GetString()));
        Assert.Contains("start", pending.GetProperty("allowed_actions").EnumerateArray().Select(x => x.GetString()));
        Assert.Contains("reject_cancellation", (await Send("manager", "GET", $"/{id}", null, 200)).GetProperty("allowed_actions").EnumerateArray().Select(x => x.GetString()));
        await Send("a", "POST", $"/{id}/request-cancellation", new { reason = "Repeat" }, 409, "INVALID_STATE_TRANSITION");
        var rejected = await RejectCancellation(id, "  Cần tiếp tục  ");
        CancellationState(rejected, "rejected", "Xe hỏng", "Cần tiếp tục");
        Assert.Contains("request_cancellation", (await Send("a", "GET", $"/{id}", null, 200)).GetProperty("allowed_actions").EnumerateArray().Select(x => x.GetString()));
        await Send("manager", "POST", $"/{id}/reject-cancellation", new { reject_reason = "Repeat" }, 409, "INVALID_STATE_TRANSITION");
        var again = await RequestCancellation(id, "Lý do mới");
        CancellationState(again, "pending", "Lý do mới");
        // D-9: a decision from an old screen acts on the current request.
        CancellationState(await RejectCancellation(id), "rejected", "Lý do mới", "Vui lòng tiếp tục lịch đã giao");
        var events = await Db(db => db.Set<AuditEvent>().IgnoreQueryFilters().Where(x => x.EntityId == id).OrderBy(x => x.AuditId).ToArrayAsync());
        Assert.Equal(new[] { AuditAction.Created, AuditAction.CancellationRequested, AuditAction.CancellationRejected,
            AuditAction.CancellationRequested, AuditAction.CancellationRejected }, events.Select(x => x.Action));
        var requested = events[1];
        Assert.Equal(users["a"].UserId, requested.ActorUserId);
        Assert.Equal(UserRole.FieldEngineer, requested.ActorRole);
        Assert.Equal("Xe hỏng", requested.Note);
        CancellationState(JsonDocument.Parse(requested.BeforeState!).RootElement, "none");
        CancellationState(JsonDocument.Parse(requested.AfterState!).RootElement, "pending", "Xe hỏng");
        Assert.Equal(requested.OccurredAt, pending.GetProperty("cancellation_requested_at").GetDateTime());
        Assert.Equal(0, requested.OccurredAt.Ticks % 10);
    }

    [Fact]
    public async Task Cancellation_requires_assignee_commune_capability_and_assigned_status()
    {
        var id = await Plant(WorkOrderStatus.Assigned);
        using var anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync($"{Route}/{id}/request-cancellation", new { reason = "X" })).StatusCode);
        foreach (var role in new[] { "manager", "superior", "admin" })
            await Send(role, "POST", $"/{id}/request-cancellation", new { reason = "X" }, 403, "ROLE_FORBIDDEN");
        foreach (var role in new[] { "a", "b", "superior", "admin" })
            await Send(role, "POST", $"/{id}/reject-cancellation", new { reject_reason = "X" }, 403, "ROLE_FORBIDDEN");
        await Send("b", "POST", $"/{id}/request-cancellation", new { reason = "X" }, 404, "WORK_ORDER_NOT_FOUND");
        var outside = await Plant(WorkOrderStatus.Assigned, commune: foreign);
        await Send("a", "POST", $"/{outside}/request-cancellation", new { reason = "X" }, 404, "WORK_ORDER_NOT_FOUND");
        foreach (var state in new[] { WorkOrderStatus.InProgress, WorkOrderStatus.Done, WorkOrderStatus.Verified, WorkOrderStatus.Cancelled })
        {
            var other = await Plant(state);
            await Send("a", "POST", $"/{other}/request-cancellation", new { reason = "X" }, 409, "INVALID_STATE_TRANSITION");
        }
        var open = await Plant(WorkOrderStatus.Open);
        await Send("a", "POST", $"/{open}/request-cancellation", new { reason = "X" }, 404, "WORK_ORDER_NOT_FOUND");
    }

    [Fact]
    public async Task Cancellation_validates_trimmed_reason_in_service_before_lookup()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<WorkOrderService>();
        foreach (var bad in new string?[] { null, "", " \t\r\n", new('a', 501) })
        {
            var request = await Assert.ThrowsAsync<LuxMapException>(() => service.RequestCancellation("unused", new() { Reason = bad }, default));
            var reject = await Assert.ThrowsAsync<LuxMapException>(() => service.RejectCancellation("unused", new() { RejectReason = bad }, default));
            Assert.Equal("VALIDATION_FAILED", request.Code);
            Assert.Equal("VALIDATION_FAILED", reject.Code);
        }
        var id = await Plant(WorkOrderStatus.Assigned);
        foreach (var body in new object[] { new { }, new { reason = (string?)null }, new { reason = "  " }, new { reason = new string('a', 501) } })
            await Send("a", "POST", $"/{id}/request-cancellation", body, 400, "VALIDATION_FAILED");
        var reason = new string('x', 498) + "😀";
        CancellationState(await RequestCancellation(id, " " + reason + " "), "pending", reason);
        await Send("manager", "POST", $"/{id}/reject-cancellation", new { reject_reason = new string('a', 501) }, 400, "VALIDATION_FAILED");
        CancellationState(await RejectCancellation(id, " x "), "rejected", reason, "x");
    }

    [Theory]
    [InlineData("pending")]
    [InlineData("rejected")]
    public async Task Cancellation_reset_and_noop_preserve_exactly_the_approved_fields(string state)
    {
        foreach (var action in new[] { "assign", "unassign", "cancel", "start", "scheduled_date", "due_date" })
        {
            var id = await Plant(WorkOrderStatus.Assigned);
            await RequestCancellation(id, "Original");
            if (state == "rejected") await RejectCancellation(id, "Rejection");
            var correlation = Guid.NewGuid().ToString();
            var result = action switch
            {
                "assign" => await Send("manager", "PUT", $"/{id}/assignee", new { assigned_to = users["b"].UserId }, 200, correlation: correlation),
                "unassign" => await Send("manager", "PUT", $"/{id}/assignee", new { assigned_to = (string?)null }, 200, correlation: correlation),
                "cancel" => await Send("manager", "POST", $"/{id}/cancel", new { note = "Cancel" }, 200, correlation: correlation),
                "start" => await Send("a", "POST", $"/{id}/start", null, 200, correlation: correlation),
                _ => await Send("manager", "PATCH", $"/{id}", new Dictionary<string, object?> { [action] = "2026-10-27" }, 200, correlation: correlation),
            };
            CancellationState(result, "none");
            var auditEvent = await Db(db => db.Set<AuditEvent>().IgnoreQueryFilters().SingleAsync(x => x.CorrelationId == correlation));
            CancellationState(JsonDocument.Parse(auditEvent.BeforeState!).RootElement, state, "Original", state == "rejected" ? "Rejection" : null);
            CancellationState(JsonDocument.Parse(auditEvent.AfterState!).RootElement, "none");
            if (action is "assign" or "unassign") await Send("a", "GET", $"/{id}", null, 404, "WORK_ORDER_NOT_FOUND");
        }
        var keep = await Plant(WorkOrderStatus.Assigned);
        await RequestCancellation(keep, "Keep");
        if (state == "rejected") await RejectCancellation(keep, "Keep rejection");
        foreach (var (method, suffix, body, audits) in new (string, string, object, int)[]
        {
            ("PUT", "/assignee", new { assigned_to = users["a"].UserId }, 0),
            ("PATCH", "", new { scheduled_date = (string?)null, due_date = (string?)null }, 0),
            ("PATCH", "", new { title = "Changed title", materials_note = "Tools" }, 1),
        }) CancellationState(await Send("manager", method, $"/{keep}{suffix}", body, 200, auditExpected: audits), state, "Keep", state == "rejected" ? "Keep rejection" : null);
        // Removing a date is a real change too.
        foreach (var date in new[] { "scheduled_date", "due_date" })
        {
            var id = await Plant(WorkOrderStatus.Assigned);
            await Send("manager", "PATCH", $"/{id}", new Dictionary<string, object?> { [date] = "2026-10-27" }, 200);
            await RequestCancellation(id);
            CancellationState(await Send("manager", "PATCH", $"/{id}", new Dictionary<string, object?> { [date] = null }, 200), "none");
        }
    }

    [Fact]
    public async Task Cancellation_filter_list_detail_agenda_and_bundle_agree()
    {
        var pending = await Plant(WorkOrderStatus.Assigned);
        var other = await Plant(WorkOrderStatus.Assigned);
        await RequestCancellation(pending, "Read me");
        var query = $"?commune_id={home}&cancellation_request_status=pending&page_size=1";
        var list = await Send("manager", "GET", query, null, 200);
        Assert.Equal(1, list.GetProperty("total").GetInt32());
        CancellationState(list.GetProperty("items")[0], "pending", "Read me");
        CancellationState(await Send("superior", "GET", $"/{pending}", null, 200), "pending", "Read me");
        var agenda = await Send("a", "GET", "/agenda?night_of=2026-10-26", null, 200);
        var item = agenda.GetProperty("groups").EnumerateArray().SelectMany(x => x.GetProperty("work_orders").EnumerateArray())
            .Single(x => x.GetProperty("work_order_id").GetString() == pending);
        CancellationState(item, "pending", "Read me");
        var bundle = await clients["a"].GetFromJsonAsync<JsonElement>($"/api/v1/sync/bundle?segment_id={segment}");
        CancellationState(bundle.GetProperty("work_orders").EnumerateArray().Single(x => x.GetProperty("work_order_id").GetString() == pending), "pending", "Read me");
        var none = await Send("manager", "GET", $"?commune_id={home}&cancellation_request_status=none", null, 200);
        Assert.Equal([other], none.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("work_order_id").GetString()!));
        var future = await Plant(WorkOrderStatus.Assigned);
        await Send("manager", "PATCH", $"/{future}", new { scheduled_date = "2027-01-01" }, 200);
        await RequestCancellation(future, "Future request");
        Assert.Equal(2, (await Send("manager", "GET", query, null, 200)).GetProperty("total").GetInt32());
        var dated = await Send("manager", "GET", query + "&scheduled_from=2027-01-01", null, 200);
        Assert.Equal(1, dated.GetProperty("total").GetInt32());
        Assert.Equal(future, dated.GetProperty("items")[0].GetProperty("work_order_id").GetString());
        foreach (var invalid in new[] { "bogus", "1", "pending,rejected", ",", "%20" })
            await Send("manager", "GET", "?cancellation_request_status=" + invalid, null, 400, "VALIDATION_FAILED");
    }

    [Fact]
    public async Task Cancellation_applies_to_repair_and_survey_and_notifies_only_active_anchor_managers()
    {
        var fault = await Fault(FaultStatus.Confirmed);
        var repair = await Create("repair", [fault], users["a"].UserId);
        var survey = (await Send("manager", "POST", "", new { task_kind = "survey", title = new string('T', 198) + "😀", commune_id = home,
            segment_ids = new[] { segment }, assigned_to = users["a"].UserId }, 201)).GetProperty("work_order_id").GetString()!;
        foreach (var id in new[] { repair, survey })
        {
            var reason = new string('R', 498) + "😀";
            await RequestCancellation(id, reason);
            var notices = await Db(db => db.Set<Notification>().IgnoreQueryFilters().Where(x => x.EntityId == id
                && x.Type == NotificationType.WorkOrderCancellationRequested).ToArrayAsync());
            var notice = Assert.Single(notices);
            Assert.Equal(users["manager"].UserId, notice.RecipientUserId);
            Assert.Contains("xin hủy", notice.Title);
            Assert.Contains(reason, notice.Body);
            Assert.True(notice.Title.Length <= 200 && notice.Body.Length <= 1000);
            await RejectCancellation(id, "Tiếp tục");
            var rejection = Assert.Single(await Db(db => db.Set<Notification>().IgnoreQueryFilters().Where(x => x.EntityId == id
                && x.Type == NotificationType.WorkOrderCancellationRejected).ToArrayAsync()));
            Assert.Equal(users["a"].UserId, rejection.RecipientUserId);
            Assert.Contains("bác bỏ", rejection.Body);
        }
        foreach (var unavailable in new[] { "locked", "invited", "other_commune" })
        {
            await Db(async db =>
            {
                var manager = await db.Set<LuxMap.Modules.Identity.Entities.AppUser>().SingleAsync(x => x.UserId == users["manager"].UserId);
                manager.IsLocked = unavailable == "locked";
                manager.PasswordHash = unavailable == "invited" ? null : users["manager"].PasswordHash;
                manager.PasswordSetAt = unavailable == "invited" ? null : users["manager"].PasswordSetAt;
                if (unavailable == "other_commune") db.Remove(await db.Set<LuxMap.Modules.Identity.Entities.AppUserCommune>()
                    .SingleAsync(x => x.UserId == manager.UserId && x.CommuneId == home));
                await db.SaveChangesAsync(); return 0;
            });
            var alone = await Plant(WorkOrderStatus.Assigned);
            await RequestCancellation(alone);
            Assert.Empty(await Db(db => db.Set<Notification>().IgnoreQueryFilters().Where(x => x.EntityId == alone).ToArrayAsync()));
        }
    }

    [Fact]
    public async Task Cancellation_online_only_unknown_op_does_not_block_offline_start_and_replay()
    {
        var id = await Plant(WorkOrderStatus.Assigned);
        await RequestCancellation(id);
        var startId = Guid.NewGuid();
        var batch = new { operations = new object[] {
            new { client_op_id = Guid.NewGuid(), op_type = "work_order_request_cancellation", payload = new { work_order_id = id, reason = "Unknown" } },
            new { client_op_id = startId, op_type = "work_order_start", payload = new { work_order_id = id } },
        } };
        var first = await (await clients["a"].PostAsJsonAsync("/api/v1/sync/push", batch)).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Single(first.GetProperty("rejected").EnumerateArray());
        Assert.Empty(first.GetProperty("conflicts").EnumerateArray());
        Assert.Single(first.GetProperty("applied").EnumerateArray());
        CancellationState(await Send("a", "GET", $"/{id}", null, 200), "none");
        var replay = await (await clients["a"].PostAsJsonAsync("/api/v1/sync/push", batch)).Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(Assert.Single(replay.GetProperty("applied").EnumerateArray()).GetProperty("replayed").GetBoolean());
        await Send("a", "POST", $"/{id}/request-cancellation", new { reason = "Too late" }, 409, "INVALID_STATE_TRANSITION");
        CancellationState(await Send("a", "POST", $"/{id}/complete", new { report_note = "Đã kiểm tra hết tuyến" }, 200), "none");
        CancellationState(await Send("manager", "POST", $"/{id}/return", new { note = "Check again" }, 200), "none");
        await Send("a", "POST", $"/{id}/complete", new { report_note = "Đã kiểm tra lại tuyến" }, 200);
        CancellationState(await Send("manager", "POST", $"/{id}/verify", new { note = "Accepted" }, 200), "none");
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("reassign")]
    [InlineData("request")]
    [InlineData("reject")]
    public async Task Cancellation_races_leave_only_winner_audit_and_notices(string competing)
    {
        var id = await Plant(WorkOrderStatus.Assigned);
        if (competing == "reject") await RequestCancellation(id);
        var beforeAudits = await AuditCount(id);
        var beforeNotices = await Db(db => db.Set<Notification>().IgnoreQueryFilters().CountAsync(x => x.EntityId == id));
        var barrier = new SaveBarrier(id);
        await using var race = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.AddDbContext<LuxMapDbContext>((_, options) => options.AddInterceptors(barrier))));
        using var a = race.CreateClient(); using var manager = race.CreateClient();
        a.DefaultRequestHeaders.Authorization = clients["a"].DefaultRequestHeaders.Authorization;
        manager.DefaultRequestHeaders.Authorization = clients["manager"].DefaultRequestHeaders.Authorization;
        var first = competing == "reject"
            ? manager.PostAsJsonAsync($"{Route}/{id}/reject-cancellation", new { reject_reason = "First" })
            : a.PostAsJsonAsync($"{Route}/{id}/request-cancellation", new { reason = "First" });
        var second = competing switch
        {
            "cancel" => manager.PostAsJsonAsync($"{Route}/{id}/cancel", new { note = "Cancelled" }),
            "reassign" => manager.PutAsJsonAsync($"{Route}/{id}/assignee", new { assigned_to = users["b"].UserId }),
            "reject" => manager.PostAsJsonAsync($"{Route}/{id}/reject-cancellation", new { reject_reason = "Second" }),
            _ => a.PostAsJsonAsync($"{Route}/{id}/request-cancellation", new { reason = "Second" }),
        };
        var responses = await Task.WhenAll(first, second);
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.OK);
        Assert.Single(responses, x => x.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.NotFound);
        Assert.Equal(2, barrier.Arrivals);
        Assert.Equal(beforeAudits + 1, await AuditCount(id));
        var winner = await responses.Single(x => x.StatusCode == HttpStatusCode.OK).Content.ReadFromJsonAsync<JsonElement>();
        var expectedNotices = winner.GetProperty("assigned_to").GetString() == users["b"].UserId ? 2 : 1;
        Assert.Equal(beforeNotices + expectedNotices, await Db(db => db.Set<Notification>().IgnoreQueryFilters().CountAsync(x => x.EntityId == id)));
    }
}
