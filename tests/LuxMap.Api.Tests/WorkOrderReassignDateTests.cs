using System.Text.Json;
using LuxMap.Modules.Notifications.Entities;
using LuxMap.Modules.WorkOrders.Entities;
using LuxMap.Persistence;
using LuxMap.Persistence.Audit;
using LuxMap.Shared.Contracts.Enums;
using Microsoft.EntityFrameworkCore;

namespace LuxMap.Api.Tests;

public partial class WorkOrderTests
{
    private async Task<string> PlantScheduled(string due = "2026-10-30", string scheduled = "2026-10-27")
    {
        var id = await Plant(WorkOrderStatus.Assigned, users["a"].UserId);
        await Send("manager", "PATCH", $"/{id}", new { due_date = due, scheduled_date = scheduled }, 200);
        return id;
    }

    private Task<NotificationType[]> NoticeTypes(string id, string recipient)
        => Db(async db => (await db.Set<Notification>().IgnoreQueryFilters()
            .Where(x => x.EntityId == id && x.RecipientUserId == recipient).OrderBy(x => x.NotificationId.Length).ThenBy(x => x.NotificationId)
            .ToArrayAsync()).Select(x => x.Type).ToArray());

    [Fact]
    public async Task Reassignment_with_new_dates_applies_both_in_one_audit_event_and_one_notice_each()
    {
        var id = await PlantScheduled();
        await RequestCancellation(id, "Xe hỏng");
        var beforeA = await NoticeTypes(id, users["a"].UserId);
        var correlation = Guid.NewGuid().ToString();
        var result = await Send("manager", "PUT", $"/{id}/assignee",
            new { assigned_to = users["b"].UserId, scheduled_date = "2026-11-02", due_date = "2026-11-05" }, 200, correlation: correlation);
        Assert.Equal(users["b"].UserId, result.GetProperty("assigned_to").GetString());
        Assert.Equal("2026-11-02", result.GetProperty("scheduled_date").GetString());
        Assert.Equal("2026-11-05", result.GetProperty("due_date").GetString());
        Assert.Equal("assigned", result.GetProperty("wo_status").GetString());
        CancellationState(result, "none");

        var auditEvent = await Db(db => db.Set<AuditEvent>().IgnoreQueryFilters().SingleAsync(x => x.CorrelationId == correlation));
        Assert.Equal(AuditAction.Reassigned, auditEvent.Action);
        var before = JsonDocument.Parse(auditEvent.BeforeState!).RootElement;
        var after = JsonDocument.Parse(auditEvent.AfterState!).RootElement;
        Assert.Equal("2026-10-27", before.GetProperty("scheduled_date").GetString());
        Assert.Equal("2026-10-30", before.GetProperty("due_date").GetString());
        Assert.Equal("2026-11-02", after.GetProperty("scheduled_date").GetString());
        Assert.Equal("2026-11-05", after.GetProperty("due_date").GetString());

        // The new engineer hears once, as an assignment that already carries the new dates — not "assigned" then "rescheduled".
        Assert.Equal(new[] { NotificationType.WorkOrderAssigned }, await NoticeTypes(id, users["b"].UserId));
        Assert.Equal(beforeA.Append(NotificationType.WorkOrderUnassigned), await NoticeTypes(id, users["a"].UserId));
        var assignedNotice = await Db(db => db.Set<Notification>().IgnoreQueryFilters()
            .SingleAsync(x => x.EntityId == id && x.RecipientUserId == users["b"].UserId));
        Assert.Contains("05/11/2026", assignedNotice.Body);
    }

    [Fact]
    public async Task Reassignment_keeps_a_date_that_is_absent_and_clears_one_sent_as_null()
    {
        var id = await PlantScheduled();
        var onlyDay = await Send("manager", "PUT", $"/{id}/assignee", new { assigned_to = users["b"].UserId, scheduled_date = "2026-10-28" }, 200);
        Assert.Equal("2026-10-28", onlyDay.GetProperty("scheduled_date").GetString());
        Assert.Equal("2026-10-30", onlyDay.GetProperty("due_date").GetString());

        var cleared = await Send("manager", "PUT", $"/{id}/assignee",
            new Dictionary<string, object?> { ["assigned_to"] = users["a"].UserId, ["due_date"] = null }, 200);
        Assert.Equal(JsonValueKind.Null, cleared.GetProperty("due_date").ValueKind);
        Assert.Equal("2026-10-28", cleared.GetProperty("scheduled_date").GetString());

        var plain = await Send("manager", "PUT", $"/{id}/assignee", new { assigned_to = users["b"].UserId }, 200);
        Assert.Equal("2026-10-28", plain.GetProperty("scheduled_date").GetString());
        Assert.Equal(JsonValueKind.Null, plain.GetProperty("due_date").ValueKind);
    }

    [Fact]
    public async Task A_bad_date_refuses_the_whole_reassignment_and_changes_nothing()
    {
        var id = await PlantScheduled();
        await RequestCancellation(id, "Xe hỏng");
        var notices = await NoticeTypes(id, users["b"].UserId);
        foreach (var body in new object[]
        {
            new { assigned_to = users["b"].UserId, scheduled_date = "2026-11-09", due_date = "2026-11-05" },
            new { assigned_to = users["b"].UserId, scheduled_date = "2026-11-09" },
            new { assigned_to = users["b"].UserId, due_date = "06/10/2026" },
            new { assigned_to = users["b"].UserId, scheduled_date = "not a date" },
        }) await Send("manager", "PUT", $"/{id}/assignee", body, 400, "VALIDATION_FAILED");

        var unchanged = await Send("manager", "GET", $"/{id}", null, 200);
        Assert.Equal(users["a"].UserId, unchanged.GetProperty("assigned_to").GetString());
        Assert.Equal("2026-10-27", unchanged.GetProperty("scheduled_date").GetString());
        CancellationState(unchanged, "pending", "Xe hỏng");
        Assert.Equal(notices, await NoticeTypes(id, users["b"].UserId));
    }

    [Fact]
    public async Task Dates_are_refused_when_the_order_is_taken_off_everyone()
    {
        var id = await PlantScheduled();
        await Send("manager", "PUT", $"/{id}/assignee",
            new Dictionary<string, object?> { ["assigned_to"] = null, ["scheduled_date"] = "2026-10-29" }, 400, "VALIDATION_FAILED");
        Assert.Equal(users["a"].UserId, (await Send("manager", "GET", $"/{id}", null, 200)).GetProperty("assigned_to").GetString());
    }

    [Fact]
    public async Task The_same_engineer_with_new_dates_is_a_reschedule_that_ends_the_request()
    {
        var id = await PlantScheduled();
        await RequestCancellation(id, "Xe hỏng");
        var correlation = Guid.NewGuid().ToString();
        var result = await Send("manager", "PUT", $"/{id}/assignee",
            new { assigned_to = users["a"].UserId, scheduled_date = "2026-10-29" }, 200, correlation: correlation);
        Assert.Equal("2026-10-29", result.GetProperty("scheduled_date").GetString());
        Assert.Equal(users["a"].UserId, result.GetProperty("assigned_to").GetString());
        CancellationState(result, "none");
        var auditEvent = await Db(db => db.Set<AuditEvent>().IgnoreQueryFilters().SingleAsync(x => x.CorrelationId == correlation));
        Assert.Equal(AuditAction.DetailsChanged, auditEvent.Action);
        Assert.Equal(NotificationType.WorkOrderRescheduled, (await NoticeTypes(id, users["a"].UserId)).Last());

        // The same date again changes nothing, so it writes nothing and leaves a new request alone.
        await RequestCancellation(id, "Lại xin");
        CancellationState(await Send("manager", "PUT", $"/{id}/assignee",
            new { assigned_to = users["a"].UserId, scheduled_date = "2026-10-29" }, 200, auditExpected: 0), "pending", "Lại xin");
    }
}
